using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FrameCaptureAndSend : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string editorHost = "127.0.0.1";
    [SerializeField] private string androidHost = "http://10.9.43.54:8000"; // Swap for ngrok tunnel
    [SerializeField] private int port = 8000;
    // [SerializeField] private string analyzePath = "/analyze";

    [Header("UI")]
    [SerializeField] private JsonOverlayUI overlay;
    [SerializeField] private BoardSessionManager boardSessionManager;

    [Header("Capture")]
    [SerializeField] private PcaDirectCapture pcaDirectCapture;
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
        // A button on right controller to capture and send frame
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("A detected in FrameCaptureAndSend.");

            if (inFlight) return; // avoid pilin up requests
            if (Time.time - lastSendTime >= minSecondsBetweenSends)
            {
                lastSendTime = Time.time;
                overlay?.SetStatus("Image Sent Analyzing...");
                StartCoroutine(CaptureAndSend());
            }
        }
    }

    // Capture the screen, encode to JPG, and send to backend
    private IEnumerator CaptureAndSend()
    {
        inFlight = true;

        yield return new WaitForEndOfFrame();

        // using var request = UnityWebRequest.Get($"{BaseUrl}/ping");
        // request.timeout = 20;
        // yield return request.SendWebRequest();
        // Debug.Log($"Ping result: {request.result}, code={request.responseCode}, text={request.downloadHandler.text}");

        Texture2D tex = null;
        bool captureDone = false;
        string captureError = null;
        try
        {
            if (pcaDirectCapture == null)
            {
                overlay?.SetStatusTimed("Capture failed: PcaDirectCapture missing", 3.0f);
                Debug.LogError("PcaDirectCapture reference is missing.");
                yield break;
            }

            Debug.Log(pcaDirectCapture != null
            ? $"Using PcaDirectCapture from GameObject: {pcaDirectCapture.gameObject.name}"
            : "pcaDirectCapture reference is null");

            pcaDirectCapture.TryCaptureLatestFrame(
                onSuccess: capturedTex =>
                {
                    tex = capturedTex;
                    captureDone = true;
                },
                onError: err =>
                {
                    captureError = err;
                    captureDone = true;
                });

            yield return new WaitUntil(() => captureDone);

            if (!string.IsNullOrEmpty(captureError))
            {
                overlay?.SetStatusTimed($"Capture failed: {captureError}", 4.0f);
                Debug.LogError(captureError);
                yield break;
            }

            if (tex == null)
            {
                overlay?.SetStatusTimed("Capture failed: null passthrough frame", 3.0f);
                Debug.LogError("TryCaptureLatestFrame returned null texture.");
                yield break;
            }

            Debug.Log($"Using passthrough frame: {tex.width}x{tex.height}");

            if (tex.width <= 0 || tex.height <= 0)
            {
                overlay?.SetStatusTimed("Capture failed: invalid texture dimensions", 3.0f);
                Debug.LogError("Captured texture had invalid dimensions.");
                yield break;
            }

            byte[] jpg = tex.EncodeToJPG(jpegQuality);
            if (jpg == null || jpg.Length == 0)
            {
                overlay?.SetStatusTimed("Capture failed: EncodeToJPG returned no data", 3.0f);
                Debug.LogError("EncodeToJPG returned null or empty bytes.");
                yield break;
            }
        
            Debug.Log($"Captured frame: {jpg.Length} bytes");

            var form = new WWWForm();
            form.AddBinaryData("file", jpg, "frame.jpg", "image/jpeg");

            using var req = UnityWebRequest.Post($"{BaseUrl}/analyze", form);
            req.timeout = 60;

            Debug.Log("POST URL = " + $"{BaseUrl}/analyze");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"POST failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}";
                Debug.LogError(err);
                overlay?.SetStatusTimed(err, 5.0f);
                yield break; // IMPORTANT: don't fall through and overwrite status
            }

            string json = req.downloadHandler.text;
            Debug.Log($"Analyze response: {json}");

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
                overlay?.SetStatusTimed("Parsed response was null (JSON mismatch?)", 3.0f);
            }
            else
            {
                // Frontend is the source of truth for the captured image dimensions
                resp.image_width = tex.width;
                resp.image_height = tex.height;
                
                if (boardSessionManager != null)
                {
                    boardSessionManager.CreateSession(resp, jpg);
                    overlay?.SetStatusTimed("Labels shown", 2.0f);
                    // temporary visual test
                    boardSessionManager.ShowAllLabels();
                }
                else
                {
                    Debug.LogWarning("BoardSessionManager reference is missing.");
                }
            }
        }
        finally
        {
            if (tex != null) Destroy(tex);
            inFlight = false;
        }
    }
}