using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interface for the detector, defining methods and properties required for detection.
/// </summary>
public interface Detector
{
    int IMAGE_SIZE { get; }
    void Start();

    /// <summary>
    /// Perform detection on the provided image.
    /// </summary>
    /// <param name="picture"> The image data </param>
    /// <param name="callback"> Callback to handle detected bounding boxes </param>
    /// <returns> IEnumerator for coroutine </returns>
    IEnumerator Detect(Color32[] picture, System.Action<IList<BoundingBox>> callback);

}

public class DimensionsBase
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }
    public float Width { get; set; }
}


/// <summary>
/// Dimensions specific to a bounding box.
/// </summary>
public class BoundingBoxDimensions : DimensionsBase { }

/// <summary>
/// Dimensions specific to a cell.
/// </summary>
class CellDimensions : DimensionsBase { }

/// <summary>
/// Represents a bounding box, including dimensions, label, confidence, and a used flag.
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// Dimensions of the bounding box.
    /// </summary>
    public BoundingBoxDimensions Dimensions { get; set; }

    /// <summary>
    /// Label of the detected object.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Confidence score of the detection.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Flag indicating whether the bounding box has been used.
    /// </summary>
    public bool Used { get; set; }

    /// <summary>
    /// Rect property for Unity's Rect structure.
    /// </summary>
    public Rect Rect
    {
        get { return new Rect(Dimensions.X, Dimensions.Y, Dimensions.Width, Dimensions.Height); }
    }
}
