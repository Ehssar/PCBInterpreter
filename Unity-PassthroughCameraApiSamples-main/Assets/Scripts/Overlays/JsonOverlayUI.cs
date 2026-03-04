using UnityEngine;
using TMPro;

public class JsonOverlayUI : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    public void SetStatus(string s)
    {
        if (text != null) text.text = s;
    }

    public void SetResponse(AnalyzeResponse resp)
    {
        if (text == null) return;

        if (resp == null)
        {
            text.text = "No response (null)";
            return;
        }

        // Show the first component (simple)
        if (resp.components != null && resp.components.Count > 0)
        {
            var c = resp.components[0];
            string bboxStr = c.bbox != null ? $"[{string.Join(",", c.bbox)}]" : "null";

            text.text =
                $"request_id: {resp.request_id}\n" +
                $"image_bytes: {resp.image_bytes}\n" +
                $"type: {c.type}\n" +
                $"conf: {c.confidence:0.00}\n" +
                $"bbox: {bboxStr}\n";
        }
        else
        {
            text.text =
                $"request_id: {resp.request_id}\n" +
                $"image_bytes: {resp.image_bytes}\n" +
                $"(no components)";
        }
    }
}