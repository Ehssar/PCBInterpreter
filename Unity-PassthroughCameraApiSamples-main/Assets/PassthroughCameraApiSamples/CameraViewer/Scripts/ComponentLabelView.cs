using TMPro;
using UnityEngine;

public class ComponentLabelView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private TMP_Text detailText;

    [Header("Billboard")]
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Transform cameraTarget;

    public string Category { get; private set; }

    public void Setup(
        string category,
        string componentId,
        float confidence,
        string summary)
    {
        Category = category;

        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(category) ? "Unknown" : category;

        if (subtitleText != null)
            subtitleText.text = $"{componentId} • {confidence:F2}";

        if (detailText != null)
            detailText.text = string.IsNullOrEmpty(summary) ? "" : summary;
    }

    public void SetCameraTarget(Transform target)
    {
        cameraTarget = target;
    }

    private void LateUpdate()
    {
        if (!faceCamera || cameraTarget == null) return;

        Vector3 dir = transform.position - cameraTarget.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}