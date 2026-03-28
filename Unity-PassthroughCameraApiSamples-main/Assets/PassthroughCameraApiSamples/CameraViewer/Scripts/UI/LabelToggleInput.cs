using UnityEngine;

public class LabelToggleInput : MonoBehaviour
{
    [SerializeField] private BoardSessionManager boardSessionManager;
    [SerializeField] private JsonOverlayUI overlay;

    private void Update()
    {
        // Left secondary hand trigger toggle
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
        {
            Debug.Log("LEFT trigger pressed");
            ToggleLabels();
        }
    }

    private void ToggleLabels()
    {
        if (boardSessionManager == null)
        {
            Debug.LogWarning("LabelToggleInput: BoardSessionManager reference is missing.");
            overlay?.SetStatus("Label toggle failed: missing BoardSessionManager");
            return;
        }

        if (!boardSessionManager.HasSession)
        {
            Debug.Log("LabelToggleInput: no active board session.");
            overlay?.SetStatus("No analyzed board yet");
            return;
        }

        var session = boardSessionManager.CurrentSession;
        if (session == null)
        {
            overlay?.SetStatus("No analyzed board yet");
            return;
        }

        if (session.labelsVisible)
        {
            boardSessionManager.HideAllLabels();
            overlay?.SetStatus("Labels hidden");
            Debug.Log("LabelToggleInput: labels hidden.");
        }
        else
        {
            boardSessionManager.ShowAllLabels();
            overlay?.SetStatus("Labels shown");
            Debug.Log("LabelToggleInput: labels shown.");
        }
    }
}