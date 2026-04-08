using UnityEngine;
using System.Collections;

public class LabelControlPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LabelSpawner labelSpawner;

    // Keep this as the visual object only.
    // The script should live on an always-active anchor parent.
    [SerializeField] private GameObject panelVisualRoot;

    [Header("Subpanels")]
    [SerializeField] private GameObject categoryPanel;
    [SerializeField] private GameObject controlsPanel;

    [Header("Input")]
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Two; // B button

    private bool panelVisible = true;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.2f);
        PlacePanel();
        if (panelVisualRoot != null)
        {
            panelVisualRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        SetPanelVisible(panelVisible);
        ShowCategoryPanel();
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
        float verticalOffset = 0.10f;   // raise it noticeably above current camera origin

        Vector3 comfortAnchor = cam.position + Vector3.up * verticalOffset;

        Vector3 spawnPos =
            comfortAnchor +
            flatForward * forwardDistance +
            flatRight * rightOffset;

        transform.position = spawnPos;

        Vector3 lookDir = cam.position - transform.position;
        lookDir = Vector3.ProjectOnPlane(lookDir, Vector3.up).normalized;

        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
    }

    private void Update()
    {
        if (OVRInput.GetDown(toggleButton))
        {
            TogglePanel();
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
}