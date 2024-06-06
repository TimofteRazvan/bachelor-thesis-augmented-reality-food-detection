using UnityEngine;

public class OrientateText : MonoBehaviour
{

    private Camera mainCamera;
    private TextMesh textMesh;
    private float initialScale;
    private float maxDistance = 10f; // Maximum distance at which the text scales up fully
    private float minDistance = 1f;  // Minimum distance for scaling

    void Start()
    {
        mainCamera = Camera.main;
        textMesh = GetComponent<TextMesh>();
        initialScale = transform.localScale.x;
        OrientateTowardsCamera();
    }

    void LateUpdate()
    {
        OrientateTowardsCamera();
        AdjustScale();
        AdjustTransparency();
    }

    /// <summary>
    /// Orientates the text to face the camera.
    /// </summary>
    private void OrientateTowardsCamera()
    {
        transform.LookAt(mainCamera.transform);
        transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);
    }

    /// <summary>
    /// Adjusts the scale of the text based on its distance from the camera.
    /// </summary>
    private void AdjustScale()
    {
        float distance = Vector3.Distance(transform.position, mainCamera.transform.position);
        float scale = Mathf.Clamp(initialScale * (distance / minDistance), initialScale, initialScale * (maxDistance / minDistance));
        transform.localScale = new Vector3(scale, scale, scale);
    }

    /// <summary>
    /// Adjusts the transparency of the text based on its distance from the camera.
    /// </summary>
    private void AdjustTransparency()
    {
        float distance = Vector3.Distance(transform.position, mainCamera.transform.position);
        float alpha = Mathf.Clamp01(1 - (distance - minDistance) / (maxDistance - minDistance));

        Color color = textMesh.color;
        color.a = alpha;
        textMesh.color = color;
    }
}
