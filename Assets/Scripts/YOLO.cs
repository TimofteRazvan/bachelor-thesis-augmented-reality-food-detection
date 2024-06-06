using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Unity.Barracuda;

/// <summary>
/// YOLO detector implementation using Unity Barracuda.
/// </summary>
public class YOLO : MonoBehaviour, Detector
{
    public NNModel modelFile;
    public TextAsset labelsFile;
    public float MinimumConfidence = 0.2f;

    private IWorker worker;
    private string[] labels;

    private const int ImageSize = 416;

    private readonly float[] anchors = new float[]
    {
        0.57273F, 0.677385F, 1.87446F, 2.06253F, 3.33843F, 5.47434F, 7.88282F, 3.52778F, 9.77052F, 9.16828F
    };

    private const float ImageMean = 0;
    private const float ImageStd = 255.0F;

    private const string InputName = "yolov2-tiny/net1";
    private const string OutputName = "yolov2-tiny/convolutional9/BiasAdd";
    private const int ClassCount = 100;
    private const int BoxInfoFeatureCount = 5;
    private const float CellWidth = 32;
    private const float CellHeight = 32;
    private const int RowCount = 13;
    private const int ColCount = 13;
    private const int BoxesPerCell = 5;

    public int IMAGE_SIZE { get => ImageSize; }

    /// <summary>
    /// Check successful initialization of YOLO detector.
    /// </summary>
    public void Start()
    {
        if (!InitializeModelAndLabels())
        {
            Debug.LogError("Failed to initialize model and labels.");
            return;
        }

        Debug.Log("Model and labels successfully initialized.");
    }

    /// <summary>
    /// Initialize the model and labels file.
    /// </summary>
    private bool InitializeModelAndLabels()
    {
        if (modelFile == null || labelsFile == null)
            return false;

        labels = labelsFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0)
            return false;

        var model = ModelLoader.Load(modelFile);
        if (model == null)
            return false;

        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, model);
        if (worker == null)
            return false;

        return true;
    }

    /// <summary>
    /// Runs object detection on the provided image.
    /// </summary>
    /// <param name="picture"> Image to process </param>
    /// <param name="callback"> Callback to handle the results </param>
    /// <returns> IEnumerator for coroutine </returns>
    public IEnumerator Detect(Color32[] picture, Action<IList<BoundingBox>> callback)
    {
        if (worker == null)
        {
            Debug.LogError("Worker is not initialized.");
            yield break;
        }

        if (picture == null)
        {
            Debug.LogError("Picture is null.");
            yield break;
        }

        Debug.Log("Transforming input...");
        using (var tensor = TransformInput(picture, ImageSize, ImageSize))
        {
            var inputs = new Dictionary<string, Tensor> { { InputName, tensor } };
            yield return StartCoroutine(worker.StartManualSchedule(inputs));

            var output = worker.PeekOutput(OutputName);
            if (output == null)
            {
                Debug.LogError("Model output is null.");
                yield break;
            }

            Debug.Log("Parsing outputs...");
            var results = ParseOutputs(output, MinimumConfidence);
            Debug.Log($"Parsed {results.Count} results.");

            var boxes = FilterBoundingBoxes(results, 5, MinimumConfidence);
            Debug.Log($"Filtered to {boxes.Count} bounding boxes.");

            callback(boxes);
        }
    }

    /// <summary>
    /// Transforms the input image to a tensor.
    /// </summary>
    /// <param name="pic"> Input image </param>
    /// <param name="width"> Image width </param>
    /// <param name="height"> Image height </param>
    /// <returns> Tensor representation of the image </returns>
    private static Tensor TransformInput(Color32[] pic, int width, int height)
    {
        var floatValues = pic.SelectMany(color =>
        {
            return new[] { (color.r - ImageMean) / ImageStd, (color.g - ImageMean) / ImageStd, (color.b - ImageMean) / ImageStd };
        }).ToArray();

        return new Tensor(1, height, width, 3, floatValues);
    }

    /// <summary>
    /// Parses the outputs of the YOLO model.
    /// </summary>
    /// <param name="modelOutput"> Model output tensor </param>
    /// <param name="threshold"> Confidence threshold </param>
    /// <returns> List of bounding boxes </returns>
    private IList<BoundingBox> ParseOutputs(Tensor modelOutput, float threshold)
    {
        var boxes = new List<BoundingBox>();

        for (int y = 0; y < ColCount; y++)
        {
            for (int x = 0; x < RowCount; x++)
            {
                for (int b = 0; b < BoxesPerCell; b++)
                {
                    var channel = (b * (ClassCount + BoxInfoFeatureCount));
                    var boxDimensions = ExtractBoundingBoxDimensions(modelOutput, x, y, channel);
                    float confidence = GetConfidence(modelOutput, x, y, channel);

                    if (confidence < threshold)
                    {
                        continue;
                    }

                    float[] predictedClasses = ExtractClasses(modelOutput, x, y, channel);
                    var (topResultIndex, topResultScore) = GetTopResult(predictedClasses);
                    var topScore = topResultScore * confidence;

                    if (topScore < threshold)
                    {
                        continue;
                    }

                    var mappedBoundingBox = MapBoundingBoxToCell(x, y, b, boxDimensions);
                    boxes.Add(new BoundingBox
                    {
                        Dimensions = new BoundingBoxDimensions
                        {
                            X = (mappedBoundingBox.X - mappedBoundingBox.Width / 2),
                            Y = (mappedBoundingBox.Y - mappedBoundingBox.Height / 2),
                            Width = mappedBoundingBox.Width,
                            Height = mappedBoundingBox.Height,
                        },
                        Confidence = topScore,
                        Label = labels[topResultIndex],
                        Used = false
                    });
                }
            }
        }

        return boxes;
    }

    /// <summary>
    /// Applies the sigmoid function.
    /// </summary>
    /// <param name="value"> Input value </param>
    /// <returns> Sigmoid of the input value </returns>
    private float Sigmoid(float value)
    {
        var k = (float)Math.Exp(value);
        return k / (1.0f + k);
    }

    /// <summary>
    /// Applies the softmax function.
    /// </summary>
    /// <param name="values"> Input values </param>
    /// <returns> Softmax of the input values </returns>
    private float[] Softmax(float[] values)
    {
        var maxVal = values.Max();
        var exp = values.Select(v => Math.Exp(v - maxVal));
        var sumExp = exp.Sum();

        return exp.Select(v => (float)(v / sumExp)).ToArray();
    }

    /// <summary>
    /// Extracts bounding box dimensions from the model output.
    /// </summary>
    /// <param name="modelOutput"> Model output tensor </param>
    /// <param name="x"> X coordinate </param>
    /// <param name="y"> Y coordinate </param>
    /// <param name="channel"> Channel index </param>
    /// <returns> Bounding box dimensions </returns>
    private BoundingBoxDimensions ExtractBoundingBoxDimensions(Tensor modelOutput, int x, int y, int channel)
    {
        return new BoundingBoxDimensions
        {
            X = modelOutput[0, x, y, channel],
            Y = modelOutput[0, x, y, channel + 1],
            Width = modelOutput[0, x, y, channel + 2],
            Height = modelOutput[0, x, y, channel + 3]
        };
    }

    /// <summary>
    /// Gets the confidence score from the model output.
    /// </summary>
    /// <param name="modelOutput"> Model output tensor </param>
    /// <param name="x"> X coordinate </param>
    /// <param name="y"> Y coordinate </param>
    /// <param name="channel"> Channel index </param>
    /// <returns> Confidence score </returns>
    private float GetConfidence(Tensor modelOutput, int x, int y, int channel)
    {
        return Sigmoid(modelOutput[0, x, y, channel + 4]);
    }

    /// <summary>
    /// Maps the bounding box to the cell.
    /// </summary>
    /// <param name="x"> X coordinate </param>
    /// <param name="y"> Y coordinate </param>
    /// <param name="box"> Box index </param>
    /// <param name="boxDimensions"> Bounding box dimensions </param>
    /// <returns> Cell dimensions </returns>
    private CellDimensions MapBoundingBoxToCell(int x, int y, int box, BoundingBoxDimensions boxDimensions)
    {
        return new CellDimensions
        {
            X = ((float)x + Sigmoid(boxDimensions.X)) * CellWidth,
            Y = ((float)y + Sigmoid(boxDimensions.Y)) * CellHeight,
            Width = (float)Math.Exp(boxDimensions.Width) * CellWidth * anchors[box * 2],
            Height = (float)Math.Exp(boxDimensions.Height) * CellHeight * anchors[box * 2 + 1],
        };
    }

    /// <summary>
    /// Extracts class probabilities from the model output.
    /// </summary>
    /// <param name="modelOutput"> Model output tensor </param>
    /// <param name="x"> X coordinate </param>
    /// <param name="y"> Y coordinate </param>
    /// <param name="channel"> Channel index </param>
    /// <returns> Class probabilities </returns>
    private float[] ExtractClasses(Tensor modelOutput, int x, int y, int channel)
    {
        float[] predictedClasses = new float[ClassCount];
        int predictedClassOffset = channel + BoxInfoFeatureCount;

        for (int i = 0; i < ClassCount; i++)
        {
            predictedClasses[i] = modelOutput[0, x, y, i + predictedClassOffset];
        }

        return Softmax(predictedClasses);
    }

    /// <summary>
    /// Gets the top class prediction.
    /// </summary>
    /// <param name="predictedClasses"> Class probabilities </param>
    /// <returns> Index and score of the top class </returns>
    private ValueTuple<int, float> GetTopResult(float[] predictedClasses)
    {
        return predictedClasses
            .Select((predictedClass, index) => (Index: index, Value: predictedClass))
            .OrderByDescending(result => result.Value)
            .First();
    }

    /// <summary>
    /// Calculates the intersection over union of two bounding boxes.
    /// </summary>
    /// <param name="boundingBoxA"> First bounding box </param>
    /// <param name="boundingBoxB"> Second bounding box </param>
    /// <returns> Intersection over union value </returns>
    private float IntersectionOverUnion(Rect boundingBoxA, Rect boundingBoxB)
    {
        var areaA = boundingBoxA.width * boundingBoxA.height;
        if (areaA <= 0) return 0;

        var areaB = boundingBoxB.width * boundingBoxB.height;
        if (areaB <= 0) return 0;

        var minX = Math.Max(boundingBoxA.xMin, boundingBoxB.xMin);
        var minY = Math.Max(boundingBoxA.yMin, boundingBoxB.yMin);
        var maxX = Math.Min(boundingBoxA.xMax, boundingBoxB.xMax);
        var maxY = Math.Min(boundingBoxA.yMax, boundingBoxB.yMax);

        var intersectionArea = Math.Max(maxY - minY, 0) * Math.Max(maxX - minX, 0);

        return intersectionArea / (areaA + areaB - intersectionArea);
    }

    /// <summary>
    /// Filters the bounding boxes using non-maximum suppression.
    /// </summary>
    /// <param name="boxes"> List of bounding boxes </param>
    /// <param name="limit"> Maximum number of boxes to keep </param>
    /// <param name="threshold"> IoU threshold </param>
    /// <returns> Filtered list of bounding boxes </returns>
    private IList<BoundingBox> FilterBoundingBoxes(IList<BoundingBox> boxes, int limit, float threshold)
    {
        var isActiveBoxes = new bool[boxes.Count];
        for (int i = 0; i < isActiveBoxes.Length; i++) isActiveBoxes[i] = true;

        var sortedBoxes = boxes.Select((b, i) => new { Box = b, Index = i })
                               .OrderByDescending(b => b.Box.Confidence)
                               .ToList();

        var results = new List<BoundingBox>();

        for (int i = 0; i < boxes.Count; i++)
        {
            if (isActiveBoxes[i])
            {
                var boxA = sortedBoxes[i].Box;
                results.Add(boxA);

                if (results.Count >= limit) break;

                for (var j = i + 1; j < boxes.Count; j++)
                {
                    if (isActiveBoxes[j])
                    {
                        var boxB = sortedBoxes[j].Box;
                        if (IntersectionOverUnion(boxA.Rect, boxB.Rect) > threshold)
                        {
                            isActiveBoxes[j] = false;
                        }
                    }
                }
            }
        }
        return results;
    }
}
