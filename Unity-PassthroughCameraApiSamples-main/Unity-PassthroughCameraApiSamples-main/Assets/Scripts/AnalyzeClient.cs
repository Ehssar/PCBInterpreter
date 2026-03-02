using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AnalyzeClient : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string analyzeUrl = "http://127.0.0.1:8000/analyze";

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
        Texture2D tex = null;

        if (testTexture != null)
        {
            tex = testTexture;
        }
        else if (webcam != null && webcam.isPlaying && webcam.width > 16)
        {
            tex = new Texture2D(webcam.width, webcam.height, TextureFormat.RGB24, false);
            tex.SetPixels(webcam.GetPixels());
            tex.Apply();
        }
        else
        {
            Debug.LogError("No image source set. Assign a testTexture or enable webcam capture.");
            yield break;
        }

        // 2) Encode to JPG 
        byte[] jpgBytes = tex.EncodeToJPG(90);
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("Failed to encode JPG.");
            yield break;
        }

        // 3) Build multipart form-data request
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

            // 4) Parse
            AnalyzeResponse parsed = JsonUtility.FromJson<AnalyzeResponse>(json);
            if (parsed == null || parsed.components == null)
            {
                Debug.LogError("Failed to parse JSON into AnalyzeResponse.");
                yield break;
            }

            // 5) Use results
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
}