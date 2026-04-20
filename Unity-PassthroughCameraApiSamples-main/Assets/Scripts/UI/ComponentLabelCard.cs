using System;
using TMPro;
using UnityEngine;
using Oculus.Interaction;

public class ComponentLabelCard : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Transform faceTargetRoot;

    [Header("Sizing")]
    [SerializeField] private RectTransform canvasRootRect;
    [SerializeField] private RectTransform backplateRect;
    [SerializeField] private RectTransform contentRootRect;

    [SerializeField] private float compactCanvasHeight = 210f;
    [SerializeField] private float expandedCanvasHeight = 300f;

    [SerializeField] private float compactBackplateHeight = 178f;
    [SerializeField] private float expandedBackplateHeight = 268f;

    [SerializeField] private float compactContentHeight = 136f;
    [SerializeField] private float expandedContentHeight = 220f;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private TextMeshProUGUI detailText;

    [Header("Behavior")]
    [SerializeField] private bool hideDetailWhenEmpty = true;

    [Header("User Facing")]
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Transform headTransform;
    [SerializeField] private bool faceUserOnRelease = true;
    [SerializeField] private bool smoothFaceUser = true;
    [SerializeField] private float faceUserSmoothSpeed = 10f;

    [Header("Grab")]
    [SerializeField] private GrabInteractable grabInteractable;

    private string componentId;
    private Vector3 anchorWorldPosition;
    private Vector3 userOffset;
    private bool hasUserMoved;
    private bool initialized;

    // Grab / rotate state
    private bool isGrabbed = false;
    private bool shouldSmoothRotateToUser = false;
    private bool wasGrabbedLastFrame = false;

    // Hover state
    private bool isHovered = false;
    private bool wasHoveredLastFrame = false;

    public string ComponentId => componentId;
    public bool HasUserMoved => hasUserMoved;
    public bool IsGrabbed => isGrabbed;
    public bool IsHovered => isHovered;

    public event Action<string> HoverStarted;
    public event Action<string> HoverEnded;
    public event Action<string> SelectedStarted;
    public event Action<string> SelectedEnded;

    private void Awake()
    {
        if (visualRoot != null)
            visualRoot.SetActive(false);

        if (grabInteractable == null)
            grabInteractable = GetComponent<GrabInteractable>();

        if (grabInteractable == null)
            grabInteractable = GetComponentInParent<GrabInteractable>();

        initialized = false;
    }

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

        if (headTransform == null)
        {
            if (cameraTarget != null)
                headTransform = cameraTarget;
            else if (Camera.main != null)
                headTransform = Camera.main.transform;
        }

        RefreshText(component, detailsMode);
        RefreshPosition();

        if (visualRoot != null)
            visualRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        if (visualRoot != null)
            visualRoot.SetActive(true);

        initialized = true;
        SnapFacingUser();
    }

    private void ApplySize(bool expanded)
    {
        SetHeight(canvasRootRect, expanded ? expandedCanvasHeight : compactCanvasHeight);
        SetHeight(backplateRect, expanded ? expandedBackplateHeight : compactBackplateHeight);
        SetHeight(contentRootRect, expanded ? expandedContentHeight : compactContentHeight);
    }

    private void SetHeight(RectTransform rt, float height)
    {
        if (rt == null)
            return;

        Vector2 size = rt.sizeDelta;
        size.y = height;
        rt.sizeDelta = size;
    }

    public void RefreshText(ComponentResult component, bool detailsMode)
    {
        if (component == null) return;

        if (titleText != null)
            titleText.text = component.GetResolvedLabelTitle();

        if (subtitleText != null)
            subtitleText.text = component.GetResolvedLabelSubtitle();

        bool showDetail = false;

        if (detailText != null)
        {
            string detail = detailsMode ? GetResolvedDetailText(component) : string.Empty;
            detailText.text = detail;

            showDetail = !string.IsNullOrWhiteSpace(detail);

            if (hideDetailWhenEmpty)
                detailText.gameObject.SetActive(showDetail);
        }

        ApplySize(detailsMode && showDetail);
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

        if (headTransform == null && target != null)
            headTransform = target;
    }

    public void ResetToAnchor()
    {
        userOffset = Vector3.zero;
        hasUserMoved = false;
        RefreshPosition();
        SnapFacingUser();
    }

    private void Update()
    {
        if (!initialized) return;
        UpdateInteractionState();
    }

    private void LateUpdate()
    {
        if (!initialized) return;

        DetectUserMovement();

        if (!faceCamera || !faceUserOnRelease || !smoothFaceUser || !shouldSmoothRotateToUser || isGrabbed)
            return;

        Transform rotateTarget = transform;
        Quaternion targetRotation = GetYawFacingRotation(rotateTarget);

        rotateTarget.rotation = Quaternion.Slerp(
            rotateTarget.rotation,
            targetRotation,
            faceUserSmoothSpeed * Time.deltaTime
        );

        float angle = Quaternion.Angle(rotateTarget.rotation, targetRotation);
        if (angle < 0.5f)
        {
            rotateTarget.rotation = targetRotation;
            shouldSmoothRotateToUser = false;
        }
    }

    private InteractableState? lastLoggedState = null;

    private void UpdateInteractionState()
    {
        if (grabInteractable == null)
            return;

        InteractableState currentState = grabInteractable.State;

        if (lastLoggedState == null || currentState != lastLoggedState.Value)
        {
            // Debug.Log($"[ComponentLabelCard] {name} interactable state = {currentState}");
            lastLoggedState = currentState;
        }

        bool isCurrentlyGrabbed = currentState == InteractableState.Select;
        bool isCurrentlyHovered = currentState == InteractableState.Hover;

        if (isCurrentlyHovered && !wasHoveredLastFrame)
        {
            BeginHover();
        }
        else if (!isCurrentlyHovered && wasHoveredLastFrame)
        {
            EndHover();
        }

        if (isCurrentlyGrabbed && !wasGrabbedLastFrame)
        {
            BeginGrab();
        }
        else if (!isCurrentlyGrabbed && wasGrabbedLastFrame)
        {
            EndGrab();
        }

        wasHoveredLastFrame = isCurrentlyHovered;
        wasGrabbedLastFrame = isCurrentlyGrabbed;
    }

    public void BeginHover()
    {
        isHovered = true;

        if (!string.IsNullOrWhiteSpace(componentId))
            HoverStarted?.Invoke(componentId);
    }

    public void EndHover()
    {
        isHovered = false;

        if (!string.IsNullOrWhiteSpace(componentId))
            HoverEnded?.Invoke(componentId);
    }

    public void BeginGrab()
    {
        Debug.Log($"[ComponentLabelCard] BeginGrab {name}");
        isGrabbed = true;
        shouldSmoothRotateToUser = false;

        if (!string.IsNullOrWhiteSpace(componentId))
            SelectedStarted?.Invoke(componentId);
    }

    public void EndGrab()
    {
        Debug.Log($"[ComponentLabelCard] EndGrab {name}");
        isGrabbed = false;

        if (!string.IsNullOrWhiteSpace(componentId))
            SelectedEnded?.Invoke(componentId);

        if (!faceCamera || !faceUserOnRelease)
            return;

        if (smoothFaceUser)
        {
            shouldSmoothRotateToUser = true;
        }
        else
        {
            SnapFacingUser();
        }
    }

    public void SnapFacingUser()
    {
        if (!faceCamera) return;

        Transform rotateTarget = transform;
        rotateTarget.rotation = GetYawFacingRotation(rotateTarget);
        shouldSmoothRotateToUser = false;
    }

    private Quaternion GetYawFacingRotation(Transform rotateTarget)
    {
        Transform userTarget = headTransform != null ? headTransform : cameraTarget;
        if (userTarget == null)
            return rotateTarget.rotation;

        Vector3 lookDir = userTarget.position - rotateTarget.position;
        lookDir = Vector3.ProjectOnPlane(lookDir, Vector3.up);

        if (lookDir.sqrMagnitude < 0.001f)
            return rotateTarget.rotation;

        return Quaternion.LookRotation(lookDir.normalized, Vector3.up);
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