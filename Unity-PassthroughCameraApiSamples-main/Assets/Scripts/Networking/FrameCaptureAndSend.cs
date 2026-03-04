using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FrameCaptureAndSend : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string editorHost = "127.0.0.1";
    [SerializeField] private string androidHost = "https://kip-unerasing-twitchily.ngrok-free.dev";
    [SerializeField] private int port = 8000;
    // [SerializeField] private string analyzePath = "/analyze";

    [Header("UI")]
    [SerializeField] private JsonOverlayUI overlay;

    [Header("Capture")]
    [SerializeField] private int jpegQuality = 75;
    [SerializeField] private float minSecondsBetweenSends = 1.0f;
    private bool inFlight = false;

    private float lastSendTime = -999f;

    // Allow overriding host for editor for testing without hardware
    private string BaseUrl =>
#if UNITY_ANDROID && !UNITY_EDITOR
        androidHost;
#else
        $"http://{editorHost}:{port}";
#endif

    // private string AnalyzeUrl => BaseUrl + analyzePath;


    // Added for testing in editor without needing to press the button
    [ContextMenu("Send One Frame")]
    private void SendOneFrame()
    {
        StartCoroutine(CaptureAndSend());
    }

    void Update()
    {
        // Use a button you like; this is common on Quest controllers
        if (OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
        {
            if (inFlight) return; // avoid pilin up requests
            if (Time.time - lastSendTime >= minSecondsBetweenSends)
            {
                lastSendTime = Time.time;
                StartCoroutine(CaptureAndSend());
            }
        }
    }

    // Capture the screen, encode to JPG, and send to backend
    private IEnumerator CaptureAndSend()
    {
        inFlight = true;
        overlay?.SetStatus("Capturing...");

        yield return new WaitForEndOfFrame();

        Texture2D tex = null;
        try
        {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null)
            {
                overlay?.SetStatus("Capture failed: null texture");
                Debug.LogError("CaptureScreenshotAsTexture returned null.");
                yield break;
            }

            byte[] jpg = tex.EncodeToJPG(jpegQuality);
            overlay?.SetStatus($"Sending {jpg.Length} bytes...");
            Debug.Log($"Captured frame: {jpg.Length} bytes");

            var form = new WWWForm();
            form.AddBinaryData("file", jpg, "frame.jpg", "image/jpeg");

            using var req = UnityWebRequest.Post($"{BaseUrl}/analyze", form);
            req.timeout = 20;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"POST failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}";
                Debug.LogError(err);
                overlay?.SetStatus(err);
                yield break; // IMPORTANT: don't fall through and overwrite status
            }

            string json = req.downloadHandler.text;
            Debug.Log($"Analyze response: {json}");

            // Optional: show raw JSON briefly for debugging
            // overlay?.SetStatus(json);

            AnalyzeResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<AnalyzeResponse>(json);
            }
            catch
            {
                // rare, but safe
            }

            if (resp == null)
            {
                overlay?.SetStatus("Parsed response was null (JSON mismatch?)");
            }
            else
            {
                overlay?.SetResponse(resp);
            }
        }
        finally
        {
            if (tex != null) Destroy(tex);
            inFlight = false;
        }
    }
}