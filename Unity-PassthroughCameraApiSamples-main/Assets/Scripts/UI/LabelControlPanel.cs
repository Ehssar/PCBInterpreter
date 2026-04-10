using UnityEngine;
using System.Collections;

public class LabelControlPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LabelSpawner labelSpawner;

    // Keep this as the visual object only.
    // The script should live on an always-active anchor parent.
    [SerializeField] private GameObject panelVisualRoot;

    [Header("User Facing")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private bool faceUserOnRelease = true;
    [SerializeField] private bool smoothFaceUser = true;
    [SerializeField] private float faceUserSmoothSpeed = 10f;

    [Header("Subpanels")]
    [SerializeField] private GameObject categoryPanel;
    [SerializeField] private GameObject controlsPanel;

    [Header("Input")]
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Two; // B button

    [Header("Grab")]
    [SerializeField] private Oculus.Interaction.GrabInteractable grabInteractable;

    private bool panelVisible = true;

    // Grab state
    private bool isGrabbed = false;
    private bool shouldSmoothRotateToUser = false;
    private bool wasGrabbedLastFrame = false;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.2f);

        if (headTransform == null && Camera.main != null)
            headTransform = Camera.main.transform;

        PlacePanel();

        if (panelVisualRoot != null)
        {
            panelVisualRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        SetPanelVisible(panelVisible);
        ShowControlsPanel();
    }

    private void Update()
    {
        if (OVRInput.GetDown(toggleButton))
        {
            TogglePanel();
        }

        UpdateGrabState();
    }

    private void UpdateGrabState()
    {
        if (grabInteractable == null)
            return;

        bool isCurrentlyGrabbed =
            grabInteractable.State == Oculus.Interaction.InteractableState.Select;

        if (isCurrentlyGrabbed && !wasGrabbedLastFrame)
        {
            BeginGrab();
        }
        else if (!isCurrentlyGrabbed && wasGrabbedLastFrame)
        {
            EndGrab();
        }

        wasGrabbedLastFrame = isCurrentlyGrabbed;
    }

    private void LateUpdate()
    {
        if (!faceUserOnRelease || !smoothFaceUser || !shouldSmoothRotateToUser || isGrabbed)
            return;

        if (headTransform == null)
            return;

        Quaternion targetRotation = GetYawFacingRotation();

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            faceUserSmoothSpeed * Time.deltaTime
        );

        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        if (angle < 0.5f)
        {
            transform.rotation = targetRotation;
            shouldSmoothRotateToUser = false;
        }
    }

    private void PlacePanel()
    {
        if (Camera.main == null)
            return;

        Transform cam = Camera.main.transform;

        Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;

        float forwardDistance = 0.45f;
        float rightOffset = 0.22f;
        float verticalOffset = 0.10f;

        Vector3 comfortAnchor = cam.position + Vector3.up * verticalOffset;

        Vector3 spawnPos =
            comfortAnchor +
            flatForward * forwardDistance +
            flatRight * rightOffset;

        transform.position = spawnPos;

        SnapFacingUser();
    }

    private Quaternion GetYawFacingRotation()
    {
        if (headTransform == null)
            return transform.rotation;

        Vector3 lookDir = headTransform.position - transform.position;
        lookDir = Vector3.ProjectOnPlane(lookDir, Vector3.up);

        if (lookDir.sqrMagnitude < 0.001f)
            return transform.rotation;

        return Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }

    public void SnapFacingUser()
    {
        transform.rotation = GetYawFacingRotation();
        shouldSmoothRotateToUser = false;
    }

    public void BeginGrab()
    {
        isGrabbed = true;
        shouldSmoothRotateToUser = false;
    }

    public void EndGrab()
    {
        isGrabbed = false;

        if (!faceUserOnRelease)
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

    public void TogglePanel()
    {
        SetPanelVisible(!panelVisible);
    }

    public void SetPanelVisible(bool visible)
    {
        panelVisible = visible;

        if (panelVisualRoot != null)
            panelVisualRoot.SetActive(panelVisible);
    }

    public void ShowCategoryPanel()
    {
        if (categoryPanel != null)
            categoryPanel.SetActive(true);

        if (controlsPanel != null)
            controlsPanel.SetActive(false);
    }

    public void ShowControlsPanel()
    {
        if (categoryPanel != null)
            categoryPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(true);
    }

    public void FilterAll()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.All);
    }

    public void FilterResistor()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Resistor);
    }

    public void FilterCapacitor()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Capacitor);
    }

    public void FilterIC()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.IC);
    }

    public void FilterDiode()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Diode);
    }

    public void FilterTransistor()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Transistor);
    }

    public void FilterInductor()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Inductor);
    }

    public void FilterLED()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.LED);
    }

    public void FilterUnknown()
    {
        labelSpawner?.SetFilter(LabelFilterCategory.Unknown);
    }
}