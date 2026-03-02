using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FrameCaptureAndSend : MonoBehaviour
{
    [SerializeField] private string baseUrl = "http://192.168.1.23:8000";
    [SerializeField] private int jpegQuality = 75;
    [SerializeField] private float minSecondsBetweenSends = 1.0f;

    private float lastSendTime = -999f;

    void Update()
    {
        // Use a button you like; this is common on Quest controllers
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
        {
            if (Time.time - lastSendTime >= minSecondsBetweenSends)
            {
                lastSendTime = Time.time;
                StartCoroutine(CaptureAndSend());
            }
        }
    }

    private IEnumerator CaptureAndSend()
    {
        yield return new WaitForEndOfFrame();

        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null)
        {
            Debug.LogError("CaptureScreenshotAsTexture returned null.");
            yield break;
        }

        byte[] jpg = tex.EncodeToJPG(jpegQuality);
        Destroy(tex);

        Debug.Log($"Captured frame: {jpg.Length} bytes");

        var form = new WWWForm();
        form.AddBinaryData("file", jpg, "frame.jpg", "image/jpeg");

        using var req = UnityWebRequest.Post($"{baseUrl}/analyze", form);
        req.timeout = 10;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"POST /analyze failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        Debug.Log($"Analyze response: {req.downloadHandler.text}");
    }
}