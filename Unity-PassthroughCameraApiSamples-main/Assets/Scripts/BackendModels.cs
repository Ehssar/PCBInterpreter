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
    public BoardContext board_context;
    public List<ComponentResult> components;
}

[Serializable]
public class BoardContext
{
    public string summary;
    public List<string> region_hints;
    public List<string> notes;
}

[Serializable]
public class ComponentResult
{
    public string component_id;
    public string type;
    public float confidence;
    public int[] bbox; // [x, y, w, h]
    public string source_label;

    public DetectionInfo detection;
    public EnrichmentInfo enrichment;
    public ComponentLabel label;
    public List<CandidatePart> candidates;
}

[Serializable]
public class DetectionInfo
{
    public string source;
    public string model_id;
    public string raw_model_label;
    public string normalized_type;
    public float confidence;
}

[Serializable]
public class EnrichmentInfo
{
    public string display_name;
    public string one_line_label;
    public string function_summary;
    public string confidence_note;
    public string ocr_text;
    public string datasheet_url;
    public bool needs_human_verification;
    public List<string> datasheet_search_terms;
    public ComponentAttributes attributes;
}

[Serializable]
public class ComponentAttributes
{
    public string package;
    public float package_confidence;
    public string marking_text;
    public string part_family;
    public float part_family_confidence;
    public string electrical_value;
    public float electrical_value_confidence;
    public string likely_role;
    public float likely_role_confidence;
    public string mount_type;
    public int pin_count;
    public bool polarized;
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
public class CandidatePart
{
    public string part_number;
    public float confidence;
    public string datasheet_url;
}