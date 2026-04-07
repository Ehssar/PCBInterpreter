using TMPro;
using UnityEngine;

public class ComponentLabelCard : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Transform faceTargetRoot;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private TextMeshProUGUI detailText;

    [Header("Behavior")]
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private bool hideDetailWhenEmpty = true;

    private string componentId;
    private Vector3 anchorWorldPosition;
    private Vector3 userOffset;
    private bool hasUserMoved;
    private bool initialized;

    public string ComponentId => componentId;
    public bool HasUserMoved => hasUserMoved;

    public void Initialize(
        ComponentResult component,
        Vector3 anchorPos,
        Transform cameraTarget,
        bool detailsMode)
    {
        if (component == null) return;

        componentId = component.component_id;
        anchorWorldPosition = anchorPos;
        this.cameraTarget = cameraTarget;

        RefreshText(component, detailsMode);
        RefreshPosition();

        if (visualRoot != null)
            visualRoot.SetActive(true);

        initialized = true;
    }

    public void RefreshText(ComponentResult component, bool detailsMode)
    {
        if (component == null) return;

        if (titleText != null)
            titleText.text = component.GetResolvedLabelTitle();

        if (subtitleText != null)
            subtitleText.text = component.GetResolvedLabelSubtitle();

        if (detailText != null)
        {
            string detail = detailsMode ? GetResolvedDetailText(component) : string.Empty;
            detailText.text = detail;

            if (hideDetailWhenEmpty)
                detailText.gameObject.SetActive(!string.IsNullOrWhiteSpace(detail));
        }
    }

    public void SetVisible(bool visible)
    {
        if (visualRoot != null)
            visualRoot.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }

    public void SetAnchorWorldPosition(Vector3 anchorPos)
    {
        anchorWorldPosition = anchorPos;

        if (!hasUserMoved)
            RefreshPosition();
    }

    public void SetCameraTarget(Transform target)
    {
        cameraTarget = target;
    }

    public void ResetToAnchor()
    {
        userOffset = Vector3.zero;
        hasUserMoved = false;
        RefreshPosition();
    }

    private void LateUpdate()
    {
        if (!initialized) return;

        DetectUserMovement();

        if (faceCamera && cameraTarget != null)
        {
            Transform t = faceTargetRoot != null ? faceTargetRoot : transform;
            Vector3 direction = t.position - cameraTarget.position;

            if (direction.sqrMagnitude > 0.0001f)
                t.forward = direction.normalized;
        }
    }

    private void DetectUserMovement()
    {
        Vector3 expectedPosition = anchorWorldPosition + userOffset;
        float distance = Vector3.Distance(transform.position, expectedPosition);

        if (distance > 0.001f)
        {
            userOffset = transform.position - anchorWorldPosition;
            hasUserMoved = true;
        }
    }

    private void RefreshPosition()
    {
        transform.position = anchorWorldPosition + userOffset;
    }

    private string GetResolvedDetailText(ComponentResult component)
    {
        if (component.enrichment != null)
        {
            if (!string.IsNullOrWhiteSpace(component.enrichment.function_summary))
                return component.enrichment.function_summary;

            if (!string.IsNullOrWhiteSpace(component.enrichment.confidence_note))
                return component.enrichment.confidence_note;

            if (!string.IsNullOrWhiteSpace(component.enrichment.ocr_text))
                return component.enrichment.ocr_text;
        }

        if (component.candidates != null &&
            component.candidates.Count > 0 &&
            !string.IsNullOrWhiteSpace(component.candidates[0].part_number))
        {
            return component.candidates[0].part_number;
        }

        return string.Empty;
    }
}