using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
public static class TextureUtils
{
    public static Texture2D ToReadableRGB24(Texture source)
    {
        if (source == null) return null;

        int w = source.width;
        int h = source.height;

        // RenderTexture is uncompressed GPU buffer we can read back
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;

        // Read back into a CPU Texture2D with a supported format
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false, false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return tex;
    }
}
public class AnalyzeClient : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string analyzeUrl = "https://kip-unerasing-twitchily.ngrok-free.dev/analyze";

    [Header("Input")]
    [Tooltip("If set, we'll upload this texture. Otherwise we'll try webcam.")]
    [SerializeField] private Texture2D testTexture;

    [Header("Webcam (optional)")]
    private WebCamTexture webcam;

    void Start()
    {
    }

    [ContextMenu("Analyze Now")]
    public void AnalyzeNow()
    {
        StartCoroutine(UploadAndAnalyze());
    }

    private IEnumerator UploadAndAnalyze()
    {
        // 1) Choose an image source
        Texture tex = null;

        if (testTexture != null)
        {
            tex = testTexture;
        }
        else if (webcam != null && webcam.isPlaying && webcam.width > 16)
        {
            tex = webcam;
        }
        else
        {
            Debug.LogError("No image source set. Assign a testTexture or enable webcam capture.");
            yield break;
        }

        // 2) Convert to CPU-readable RGB24 
        Texture2D readable = TextureUtils.ToReadableRGB24(tex);
        if (readable == null)
        {
            Debug.LogError("Failed to convert texture to readable RGB24.");
            yield break;
        }

        // 3)Encode to JPG
        byte[] jpgBytes = readable.EncodeToJPG(90);

        Destroy(readable); // free CPU memory since we don't need it anymore

        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("Failed to encode JPG.");
            yield break;
        }

        // 4) Build multipart form-data request
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", jpgBytes, "frame.jpg", "image/jpeg");

        using (UnityWebRequest req = UnityWebRequest.Post(analyzeUrl, form))
        {
            // Some environments need this for local dev
            req.timeout = 15;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Analyze request failed: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                yield break;
            }

            string json = req.downloadHandler.text;
            Debug.Log("Analyze response JSON:\n" + json);

            // 5) Parse
            AnalyzeResponse parsed = JsonUtility.FromJson<AnalyzeResponse>(json);
            if (parsed == null || parsed.components == null)
            {
                Debug.LogError("Failed to parse JSON into AnalyzeResponse.");
                yield break;
            }

            // 6) Use results
            foreach (var comp in parsed.components)
            {
                Debug.Log($"Component {comp.component_id} type={comp.type} conf={comp.confidence}");
                if (comp.bbox != null && comp.bbox.Length == 4)
                    Debug.Log($"  bbox: x={comp.bbox[0]} y={comp.bbox[1]} w={comp.bbox[2]} h={comp.bbox[3]}");
                if (comp.candidates != null)
                {
                    foreach (var cand in comp.candidates)
                        Debug.Log($"  candidate {cand.part_number} conf={cand.confidence} url={cand.datasheet_url}");
                }
            }
        }
    }

    private void OnDisable()
    {
        if (webcam != null && webcam.isPlaying)
        {
            webcam.Stop();
        }
    }
}