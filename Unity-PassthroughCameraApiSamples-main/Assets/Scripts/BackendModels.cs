using System;
using System.Collections.Generic;

[Serializable]
public class AnalyzeResponse
{
    public string request_id;
    public string board_id;
    public int timing_ms;
    public int image_bytes;
    public string mode;
    public string label_visibility_default;
    public int component_count;
    public string fallback_reason;
    public List<ComponentResult> components;
}

[Serializable]
public class ComponentResult
{
    public string component_id;
    public string type;
    public float confidence;
    public int[] bbox; // [x, y, w, h]
    public string source_label;
    public ComponentLabel label;
    public ComponentDetails details;
    public List<CandidatePart> candidates;
}

[Serializable]
public class ComponentLabel
{
    public string title;
    public string subtitle;
    public bool visible;
    public bool pinned;
}

[Serializable]
public class ComponentDetails
{
    public string summary;
    public string ocr_text;
    public string datasheet_url;
    public string raw_model_label;
}

[Serializable]
public class CandidatePart
{
    public string part_number;
    public float confidence;
    public string datasheet_url;
}