using System;
using System.Collections.Generic;

[Serializable]
public class AnalyzeResponse
{
    public string request_id;
    public string board_id;
    public int timing_ms;
    public int image_bytes;

    // Recommended for accurate bbox -> world reprojection.
    // Safe to leave as 0 until the backend returns them.
    public int image_width;
    public int image_height;

    public string mode;
    public string label_visibility_default;
    public int component_count;
    public string fallback_reason;
    public BoardContext board_context;
    public List<ComponentResult> components = new();
}

[Serializable]
public class BoardContext
{
    public string summary;
    public List<string> region_hints = new();
    public List<string> notes = new();
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
    public List<CandidatePart> candidates = new();

    public string GetResolvedLabelTitle()
    {
        if (label != null && !string.IsNullOrWhiteSpace(label.title))
            return label.title;

        if (enrichment != null && !string.IsNullOrWhiteSpace(enrichment.display_name))
            return enrichment.display_name;

        if (!string.IsNullOrWhiteSpace(type))
            return type;

        if (!string.IsNullOrWhiteSpace(source_label))
            return source_label;

        return "Unknown Component";
    }

    public string GetResolvedLabelSubtitle()
    {
        if (label != null && !string.IsNullOrWhiteSpace(label.subtitle))
            return label.subtitle;

        if (enrichment != null && !string.IsNullOrWhiteSpace(enrichment.one_line_label))
            return enrichment.one_line_label;

        if (enrichment != null && !string.IsNullOrWhiteSpace(enrichment.function_summary))
            return enrichment.function_summary;

        if (enrichment != null &&
            enrichment.attributes != null &&
            !string.IsNullOrWhiteSpace(enrichment.attributes.likely_role))
        {
            return enrichment.attributes.likely_role;
        }

        if (candidates != null && candidates.Count > 0 && !string.IsNullOrWhiteSpace(candidates[0].part_number))
            return candidates[0].part_number;

        return string.Empty;
    }

    public bool IsLabelVisible()
    {
        // Default to visible unless explicitly hidden
        if (label == null) return true;
        return label.visible;
    }

    public LabelCategory GetCategory()
    {
        string raw = !string.IsNullOrWhiteSpace(type) ? type : source_label;

        if (string.IsNullOrWhiteSpace(raw))
            return LabelCategory.Unknown;

        raw = raw.Trim().ToLowerInvariant();

        return raw switch
        {
            "resistor" => LabelCategory.Resistor,
            "capacitor" => LabelCategory.Capacitor,
            "ic" => LabelCategory.IC,
            "integrated circuit" => LabelCategory.IC,
            "connector" => LabelCategory.Connector,
            "diode" => LabelCategory.Diode,
            "transistor" => LabelCategory.Transistor,
            "inductor" => LabelCategory.Inductor,
            _ => LabelCategory.Unknown
        };
    }
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
    public List<string> datasheet_search_terms = new();
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

public enum LabelCategory
{
    Unknown,
    Resistor,
    Capacitor,
    IC,
    Connector,
    Diode,
    Transistor,
    Inductor
}