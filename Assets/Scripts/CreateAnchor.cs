using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class CreateAnchor : MonoBehaviour
{
    private List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();
    private IDictionary<ARAnchor, BoundingBox> anchors = new Dictionary<ARAnchor, BoundingBox>();
    private List<BoundingBox> savedBoundingBoxes;
    private float offsetX;
    private float offsetY;
    private float scale;

    public CaptureAndDetect arCamera;
    public ARRaycastManager raycastManager;
    public TextMesh anchorLabel;
    public ARAnchorManager anchorManager;
    public NutritionFetcher nutritionFetcher;

    private const TrackableType raycastTrackableTypes = TrackableType.Planes | TrackableType.FeaturePoint;

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Initializes the raycast manager, anchor manager, and AR camera components.
    /// </summary>
    void Awake()
    {
        arCamera = GameObject.Find("Camera").GetComponent<CaptureAndDetect>();
        anchorManager = GetComponent<ARAnchorManager>();
        raycastManager = GetComponent<ARRaycastManager>();
        nutritionFetcher = GameObject.Find("Nutrition").GetComponent<NutritionFetcher>();
    }

    /// <summary>
    /// Clears all anchors from the scene and resets the collections.
    /// </summary>
    public void ClearAnchors()
    {
        foreach (var anchor in anchors.Keys)
        {
            Destroy(anchor.gameObject);
        }
        raycastHits.Clear();
        anchors.Clear();
    }

    /// <summary>
    /// Creates an anchor at the position of the given raycast hit.
    /// </summary>
    /// <param name="hit"> The raycast hit where the anchor should be created </param>
    /// <returns> The created ARAnchor </returns>
    private ARAnchor CreateAnchorAtHit(ARRaycastHit hit)
    {
        return anchorManager.AddAnchor(hit.pose);
    }

    /// <summary>
    /// Attempts to create an anchor at the given screen position for the specified bounding box.
    /// </summary>
    /// <param name="x"> The x-coordinate on the screen </param>
    /// <param name="y"> The y-coordinate on the screen </param>
    /// <param name="boundingBox"> The bounding box to associate with the anchor </param>
    /// <returns> True if the anchor was created successfully, false otherwise </returns>
    private bool TryCreateAnchor(float x, float y, BoundingBox boundingBox)
    {
        anchorLabel.text = $"{boundingBox.Label} - {boundingBox.Confidence}";
        string nutritionalInfo = nutritionFetcher.GetNutritionalInfo(boundingBox.Label);
        if (!string.IsNullOrEmpty(nutritionalInfo))
        {
            anchorLabel.text += $"\n{nutritionalInfo}";
        }
        if (raycastManager.Raycast(new Vector2(x, y), raycastHits, raycastTrackableTypes))
        {
            var hit = raycastHits[0];
            var anchor = CreateAnchorAtHit(hit);
            if (anchor)
            {
                anchors[anchor] = boundingBox;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Called once per frame. Updates the anchor positions based on the bounding boxes detected by the AR camera.
    /// </summary>
    void Update()
    {
        if (!arCamera.localizationComplete)
        {
            return;
        }

        savedBoundingBoxes = arCamera.cumulativeBoxes;
        scale = arCamera.scale;
        offsetX = arCamera.offsetX;
        offsetY = arCamera.offsetY;
        

        //if (anchors.Count > 0)
        //{
        //    List<ARAnchor> anchorsToRemove = new List<ARAnchor>();
        //    foreach (var pair in anchors)
        //    {
        //        if (!savedBoundingBoxes.Contains(pair.Value))
        //        {
        //            anchorsToRemove.Add(pair.Key);
        //            anchorManager.RemoveAnchor(pair.Key);
        //        }
        //    }

        //    foreach (var anchor in anchorsToRemove)
        //    {
        //        anchors.Remove(anchor);
        //    }
        //}

        if (savedBoundingBoxes.Count == 0)
        {
            return;
        }

        foreach (var boundingBox in savedBoundingBoxes)
        {
            if (boundingBox.Used)
            {
                continue;
            }

            float centerX = boundingBox.Dimensions.X * scale + offsetX + (boundingBox.Dimensions.Width * scale) / 2f;
            float centerY = Screen.height - (boundingBox.Dimensions.Y * scale + offsetY + (boundingBox.Dimensions.Height * scale) / 2f);

            if (TryCreateAnchor(centerX, centerY, boundingBox))
            {
                boundingBox.Used = true;
            }
        }
    }
}
