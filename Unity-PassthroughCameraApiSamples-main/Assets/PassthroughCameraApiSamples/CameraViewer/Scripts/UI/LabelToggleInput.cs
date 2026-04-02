using UnityEngine;

public class LabelToggleInput : MonoBehaviour
{
    [SerializeField] private BoardSessionManager boardSessionManager;
    [SerializeField] private JsonOverlayUI overlay;
    [SerializeField] private LabelSpawner labelSpawner;

    private void Update()
    {
        // Left secondary hand trigger toggle
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger))
        {
            Debug.Log("LEFT grip pressed");
            ToggleLabels();
        }

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
        {
            labelSpawner.ToggleDetailsMode();

            if (overlay != null)
                overlay.SetStatusTimed("Details Mode", 2.0f);
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
            overlay?.SetStatusTimed("Labels hidden", 2.0f);
            Debug.Log("LabelToggleInput: labels hidden.");
        }
        else
        {
            boardSessionManager.ShowAllLabels();
            overlay?.SetStatusTimed("Labels shown", 2.0f);
            Debug.Log("LabelToggleInput: labels shown.");
        }
    }
}