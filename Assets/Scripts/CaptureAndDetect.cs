using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Serialized Fields:
/// ARCameraManager arCameraManager: Manages the camera feed.
/// RawImage cameraFeedImage: Displays the camera feed on the screen.
/// </summary>
public class CaptureAndDetect : MonoBehaviour
{
    [SerializeField]
    ARCameraManager arCameraManager;

    public ARCameraManager CameraManager
    {
        get => arCameraManager;
        set => arCameraManager = value;
    }

    // Texture for the camera feed.
    Texture2D cameraTexture;

    [SerializeField]
    RawImage cameraFeedImage;
    public RawImage CameraFeedImage
    {
        get { return cameraFeedImage; }
        set { cameraFeedImage = value; }
    }
    public Color boundingBoxColor = new Color(0.3f, 0, 0.9f); // Color for the bounding boxes. 
    private static GUIStyle boundingBoxLabelStyle; // Style for bounding box labels.
    private static Texture2D boundingBoxTexture; // Texture used to draw bounding box outlines.
    private IList<BoundingBox> currentFrameBoxes;
    public List<BoundingBox> cumulativeBoxes = new List<BoundingBox>();

    public Detector objectDetector = null;

    // offsetX, offsetY, scale: Used for scaling and positioning the bounding boxes.
    public float offsetX = 0f;
    public float offsetY = 0f;
    public float scale = 1;

    private bool isProcessingFrame = false; // Flag to prevent concurrent frame processing.
    private int staticFrameCount = 0; // Counter to track how many frames bounding boxes have remained static.
    public bool localizationComplete = false; // Flag indicating if localization is done.

    float detectionStartTime = 0f;
    float detectionDuration = 3f; // Time to wait before showing result

    /// <summary>
    /// Unsubscribes from the camera frame event.
    /// </summary>
    void OnDisable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }

    /// <summary>
    /// Subscribes to the camera frame event.
    /// Initializes bounding box textures and styles.
    /// Starts the YOLO detector.
    /// Calculates offsets for the bounding boxes.
    /// </summary>
    void OnEnable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnCameraFrameReceived;
        }

        boundingBoxTexture = new Texture2D(1, 1);
        boundingBoxTexture.SetPixel(0, 0, this.boundingBoxColor);
        boundingBoxTexture.Apply();
        boundingBoxLabelStyle = new GUIStyle
        {
            fontSize = 44,
            normal = { textColor = boundingBoxColor }
        };
        objectDetector = GameObject.Find("DetectorYolo").GetComponent<YOLO>();
        this.objectDetector.Start();
        CalculateOffset(this.objectDetector.IMAGE_SIZE);
    }

    /// <summary>
    /// Resets the state for bounding boxes and localization.
    /// Clears all anchors using AnchorManager.
    /// </summary>
    public void Refresh()
    {
        localizationComplete = false;
        staticFrameCount = 0;
        cumulativeBoxes.Clear();
        currentFrameBoxes.Clear();
        CreateAnchor anchorCreator = FindObjectOfType<CreateAnchor>();
        anchorCreator.ClearAnchors();
    }

    /// <summary>
    /// Resets the state for bounding boxes and localization.
    /// Does NOT clear anchors.
    /// </summary>
    public void Continue()
    {
        localizationComplete = false;
        staticFrameCount = 0;
        cumulativeBoxes.Clear();
        currentFrameBoxes.Clear();
    }

    /// <summary>
    /// Sets the localization flag to true.
    /// </summary>
    public void Localize()
    {
        localizationComplete = true;
    }

    /// <summary>
    /// Initiates the object detection process if no other frame is being processed.
    /// Calls ProcessImage to process the image and then starts the YOLO detection.
    /// </summary>
    private void PerformDetection()
    {
        if (this.isProcessingFrame)
        {
            return;
        }

        this.isProcessingFrame = true;
        StartCoroutine(ProcessImage(this.objectDetector.IMAGE_SIZE, result =>
        {
            StartCoroutine(this.objectDetector.Detect(result, boxes =>
            {
                this.currentFrameBoxes = boxes;
                Resources.UnloadUnusedAssets();
                this.isProcessingFrame = false;
            }));
        }));
    }

    /// <summary>
    /// Merges bounding boxes from the current frame with previously detected bounding boxes.
    /// Ensures no duplicate bounding boxes and updates the confidence levels.
    /// </summary>
    private void MergeBoundingBoxes()
    {
        if (this.cumulativeBoxes.Count == 0)
        {
            if (this.currentFrameBoxes == null || this.currentFrameBoxes.Count == 0)
            {
                return;
            }
            foreach (var outline in this.currentFrameBoxes)
            {
                this.cumulativeBoxes.Add(outline);
            }
            return;
        }

        bool newBoxAdded = false;
        foreach (var outline1 in this.currentFrameBoxes)
        {
            bool isUnique = true;
            List<BoundingBox> itemsToAdd = new List<BoundingBox>();
            List<BoundingBox> itemsToRemove = new List<BoundingBox>();
            foreach (var outline2 in this.cumulativeBoxes)
            {
                if (AreSameObject(outline1, outline2))
                {
                    isUnique = false;
                    if (outline1.Confidence > outline2.Confidence + 0.05F)
                    {
                        itemsToRemove.Add(outline2);
                        itemsToAdd.Add(outline1);
                        newBoxAdded = true;
                        staticFrameCount = 0;
                        break;
                    }
                }
            }
            this.cumulativeBoxes.RemoveAll(item => itemsToRemove.Contains(item));
            this.cumulativeBoxes.AddRange(itemsToAdd);
            if (isUnique)
            {
                newBoxAdded = true;
                staticFrameCount = 0;
                this.cumulativeBoxes.Add(outline1);
            }
        }
        if (!newBoxAdded)
        {
            staticFrameCount += 1;
        }
        List<BoundingBox> temp = new List<BoundingBox>();
        foreach (var outline1 in this.cumulativeBoxes)
        {
            if (temp.Count == 0)
            {
                temp.Add(outline1);
                continue;
            }

            List<BoundingBox> itemsToAdd = new List<BoundingBox>();
            List<BoundingBox> itemsToRemove = new List<BoundingBox>();
            foreach (var outline2 in temp)
            {
                if (AreSameObject(outline1, outline2))
                {
                    if (outline1.Confidence > outline2.Confidence)
                    {
                        itemsToRemove.Add(outline2);
                        itemsToAdd.Add(outline1);
                    }
                }
                else
                {
                    itemsToAdd.Add(outline1);
                }
            }
            temp.RemoveAll(item => itemsToRemove.Contains(item));
            temp.AddRange(itemsToAdd);
        }
        this.cumulativeBoxes = temp;
    }

    /// <summary>
    /// Acquires the latest camera image.
    /// Converts the image to a Texture2D.
    /// Processes the image for object detection.
    /// Updates the displayed camera feed.
    /// </summary>
    /// <param name="eventArgs"> Event arguments containing the camera frame </param>
    unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        XRCpuImage image;
        if (!CameraManager.TryAcquireLatestCpuImage(out image))
        {
            return;
        }

        var format = TextureFormat.RGBA32;
        if (cameraTexture == null || cameraTexture.width != image.width || cameraTexture.height != image.height)
        {
            cameraTexture = new Texture2D(image.width, image.height, format, false);
        }

        var conversionParams = new XRCpuImage.ConversionParams(image, format, XRCpuImage.Transformation.None);
        var rawTextureData = cameraTexture.GetRawTextureData<byte>();
        try
        {
            image.Convert(conversionParams, new IntPtr(rawTextureData.GetUnsafePtr()), rawTextureData.Length);
        }
        finally
        {
            image.Dispose();
        }

        cameraTexture.Apply();
        if (staticFrameCount > 50)
        {
            localizationComplete = true;
        }
        else
        {
            PerformDetection();
            MergeBoundingBoxes();
        }
        CameraFeedImage.texture = cameraTexture;
    }

    /// <summary>
    /// Draws bounding boxes on the screen unless localization is complete.
    /// </summary>
    public void OnGUI()
    {
        if (localizationComplete)
        {
            return;
        }

        if (this.cumulativeBoxes != null && this.cumulativeBoxes.Any())
        {
            foreach (var outline in this.cumulativeBoxes)
            {
                DrawBoxOutline(outline, scale, offsetX, offsetY);
            }
        }
    }

    /// <summary>
    /// Determines if two bounding boxes represent the same object by checking if their centers overlap.
    /// </summary>
    /// <param name="outline1"> First box to be compared </param>
    /// <param name="outline2"> Second box to be compared </param>
    /// <returns> True if the bounding boxes are for the same object; otherwise, false </returns>
    private bool AreSameObject(BoundingBox outline1, BoundingBox outline2)
    {
        var xMin1 = outline1.Dimensions.X * this.scale + this.offsetX;
        var width1 = outline1.Dimensions.Width * this.scale;
        var yMin1 = outline1.Dimensions.Y * this.scale + this.offsetY;
        var height1 = outline1.Dimensions.Height * this.scale;
        float center_x1 = xMin1 + width1 / 2f;
        float center_y1 = yMin1 + height1 / 2f;

        var xMin2 = outline2.Dimensions.X * this.scale + this.offsetX;
        var width2 = outline2.Dimensions.Width * this.scale;
        var yMin2 = outline2.Dimensions.Y * this.scale + this.offsetY;
        var height2 = outline2.Dimensions.Height * this.scale;
        float center_x2 = xMin2 + width2 / 2f;
        float center_y2 = yMin2 + height2 / 2f;

        bool cover_x = (xMin2 < center_x1) && (center_x1 < (xMin2 + width2));
        bool cover_y = (yMin2 < center_y1) && (center_y1 < (yMin2 + height2));
        bool contain_x = (xMin1 < center_x2) && (center_x2 < (xMin1 + width1));
        bool contain_y = (yMin1 < center_y2) && (center_y2 < (yMin1 + height1));

        return (cover_x && cover_y) || (contain_x && contain_y);
    }

    /// <summary>
    /// Calculates the offset and scale for bounding boxes based on the screen size and input image size.
    /// </summary>
    /// <param name="inputSize"> Input image size </param>
    private void CalculateOffset(int inputSize)
    {
        int smallest;

        if (Screen.width < Screen.height)
        {
            smallest = Screen.width;
            this.offsetY = (Screen.height - smallest) / 2f;
        }
        else
        {
            smallest = Screen.height;
            this.offsetX = (Screen.width - smallest) / 2f;
        }

        this.scale = smallest / (float)inputSize;
    }

    /// <summary>
    /// Crops and scales the image to the input size required by the detector.
    /// Rotates the image if necessary.
    /// </summary>
    /// <param name="inputSize"> Input image size </param>
    /// <param name="callback"> Callback function to handle the processed image data </param>
    /// <returns> IEnumerator for coroutine </returns>
    private IEnumerator ProcessImage(int inputSize, System.Action<Color32[]> callback)
    {
        Coroutine croped = StartCoroutine(CropSquare(cameraTexture,
           RectOptions.Center, snap =>
           {
               var scaled = Scale(snap, inputSize);
               var rotated = Rotate(scaled.GetPixels32(), scaled.width, scaled.height);
               callback(rotated);
           }));
        yield return croped;
    }

    /// <summary>
    /// Draws a bounding box and label on the screen.
    /// </summary>
    /// <param name="outline"> Box outline </param>
    /// <param name="scale"> Scaling factor </param>
    /// <param name="offsetX"> Horizontal shift for the bounding box </param>
    /// <param name="offsetY"> Vertical shift for the bounding box </param>
    private void DrawBoxOutline(BoundingBox outline, float scale, float offsetX, float offsetY)
    {
        var x = outline.Dimensions.X * scale + offsetX;
        var width = outline.Dimensions.Width * scale;
        var y = outline.Dimensions.Y * scale + offsetY;
        var height = outline.Dimensions.Height * scale;

        DrawRectangle(new Rect(x, y, width, height), 10, this.boundingBoxColor);
        DrawLabel(new Rect(x, y - 80, 200, 20), $"Localizing {outline.Label}: {(int)(outline.Confidence * 100)}%");
    }

    /// <summary>
    /// Draws the rectangle for the bounding box.
    /// </summary>
    /// <param name="area"> Area of the rectangle </param>
    /// <param name="frameWidth"></param>
    /// <param name="color"></param>
    public static void DrawRectangle(Rect area, int frameWidth, Color color)
    {
        Rect lineArea = area;
        lineArea.height = frameWidth;
        GUI.DrawTexture(lineArea, boundingBoxTexture); // Top line

        lineArea.y = area.yMax - frameWidth;
        GUI.DrawTexture(lineArea, boundingBoxTexture); // Bottom line

        lineArea = area;
        lineArea.width = frameWidth;
        GUI.DrawTexture(lineArea, boundingBoxTexture); // Left line

        lineArea.x = area.xMax - frameWidth;
        GUI.DrawTexture(lineArea, boundingBoxTexture); // Right line
    }

    /// <summary>
    /// Draw a label on the screen for a bounding box.
    /// </summary>
    /// <param name="label"> The text of the label to draw </param>
    /// <param name="xLabel"> The x-coordinate for the label </param>
    /// <param name="yLabel"> The y-coordinate for the label </param>
    private static void DrawLabel(Rect position, string text)
    {
        GUI.Label(position, text, boundingBoxLabelStyle);
    }

    /// <summary>
    /// Scale the input texture to the specified size.
    /// </summary>
    /// <param name="texture"> Input texture to scale </param>
    /// <param name="imageSize"> Target size for scaling </param>
    /// <returns> Scaled Texture2D </returns>
    private Texture2D Scale(Texture2D texture, int imageSize)
    {
        var scaled = Scale(texture, imageSize, imageSize, FilterMode.Bilinear);
        return scaled;
    }

    /// <summary>
    /// Rotate the image data by 90 degrees.
    /// </summary>
    /// <param name="pixels"> Array of pixel data </param>
    /// <param name="width"> Width of the image </param>
    /// <param name="height"> Height of the image </param>
    /// <returns> Rotated pixel data array </returns>
    private Color32[] Rotate(Color32[] pixels, int width, int height)
    {
        var rotate = RotateImageMatrix(
                pixels, width, height, 90);
        return rotate;
    }

    public enum RectOptions
    {
        Center = 0,
        BottomRight = 1,
        TopRight = 2,
        BottomLeft = 3,
        TopLeft = 4,
        Custom = 9
    }

    /// <summary>
    /// Crops a square region from a texture.
    /// </summary>
    /// <param name="texture">Input texture to crop.</param>
    /// <param name="rectOptions">Options for cropping.</param>
    /// <param name="callback">Callback for returning the cropped texture.</param>
    /// <returns>Coroutine for cropping the texture.</returns>
    public static IEnumerator CropSquare(Texture2D texture, RectOptions rectOptions, Action<Texture2D> callback)
    {
        var smallest = Mathf.Min(texture.width, texture.height);
        var rect = new Rect(0, 0, smallest, smallest);

        if (rect.height < 0 || rect.width < 0)
        {
            throw new ArgumentException("Invalid texture size");
        }

        Texture2D result = new Texture2D((int)rect.width, (int)rect.height);

        if (rect.width != 0 && rect.height != 0)
        {
            float xRect = rect.x;
            float yRect = rect.y;
            float widthRect = rect.width;
            float heightRect = rect.height;

            switch (rectOptions)
            {
                case RectOptions.Center:
                    xRect = (texture.width - rect.width) / 2;
                    yRect = (texture.height - rect.height) / 2;
                    break;

                case RectOptions.BottomRight:
                    xRect = texture.width - rect.width;
                    break;

                case RectOptions.TopLeft:
                    yRect = texture.height - rect.height;
                    break;

                case RectOptions.TopRight:
                    xRect = texture.width - rect.width;
                    yRect = texture.height - rect.height;
                    break;

                case RectOptions.Custom:
                    float tempWidth = texture.width - rect.width;
                    float tempHeight = texture.height - rect.height;
                    xRect = tempWidth > texture.width ? 0 : tempWidth;
                    yRect = tempHeight > texture.height ? 0 : tempHeight;
                    break;
            }

            if (xRect < 0 || yRect < 0 || widthRect < 0 || heightRect < 0)
            {
                throw new ArgumentException("Set value crop less than origin texture size");
            }

            result.SetPixels(texture.GetPixels(Mathf.FloorToInt(xRect), Mathf.FloorToInt(yRect),
                                            Mathf.FloorToInt(widthRect), Mathf.FloorToInt(heightRect)));
            result.Apply();
        }

        yield return null;

        if (result == null)
        {
            Debug.Log("DEBUG: result is null in CropSquare");
        }

        callback(result);
    }

    /// <summary>
    /// Scales the given texture to the specified width and height.
    /// </summary>
    /// <param name="src">Source texture to scale.</param>
    /// <param name="width">Destination texture width.</param>
    /// <param name="height">Destination texture height.</param>
    /// <param name="mode">Filtering mode.</param>
    /// <returns>The scaled texture.</returns>
    public static Texture2D Scale(Texture2D src, int width, int height, FilterMode mode = FilterMode.Trilinear)
    {
        Rect texR = new Rect(0, 0, width, height);
        GPUScale(src, width, height, mode);

        // Get rendered data back to a new texture
        Texture2D result = new Texture2D(width, height, TextureFormat.ARGB32, true);
        result.Resize(width, height);
        result.ReadPixels(texR, 0, 0, true);
        return result;
    }

    static void GPUScale(Texture2D src, int width, int height, FilterMode fmode)
    {
        src.filterMode = fmode;
        src.Apply(true);
        RenderTexture rtt = new RenderTexture(width, height, 32);
        Graphics.SetRenderTarget(rtt);
        GL.LoadPixelMatrix(0, 1, 1, 0);
        GL.Clear(true, true, new Color(0, 0, 0, 0));
        Graphics.DrawTexture(new Rect(0, 0, 1, 1), src);
    }

    /// <summary>
    /// Rotates the given pixel data array by the specified angle in degrees.
    /// </summary>
    /// <param name="pixels">Array of pixel data</param>
    /// <param name="width">Width of the image</param>
    /// <param name="height">Height of the image</param>
    /// <param name="angle">Angle of rotation in degrees</param>
    /// <returns>Rotated pixel data array</returns>
    public static Color32[] RotateImageMatrix(Color32[] pixels, int width, int height, int angle)
    {
        Color32[] pix1 = new Color32[pixels.Length];
        int x = 0;
        int y = 0;
        int i;
        int j;
        double phi = Math.PI / 180 * angle;
        double sn = Math.Sin(phi);
        double cs = Math.Cos(phi);

        int xc = width / 2;
        int yc = height / 2;

        for (j = 0; j < height; j++)
        {
            for (i = 0; i < width; i++)
            {
                pix1[j * width + i] = new Color32(0, 0, 0, 0);
                x = (int)(cs * (i - xc) + sn * (j - yc) + xc);
                y = (int)(-sn * (i - xc) + cs * (j - yc) + yc);
                if ((x > -1) && (x < width) && (y > -1) && (y < height))
                {
                    pix1[j * width + i] = pixels[y * width + x];
                }
            }
        }
        return pix1;
    }
}
