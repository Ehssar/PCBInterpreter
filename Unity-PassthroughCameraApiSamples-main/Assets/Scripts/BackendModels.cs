using System;
using System.Collections.Generic;

[Serializable]
public class AnalyzeResponse
{
    public string request_id;
    public int image_bytes;
    public List<ComponentResult> components;
}

[Serializable]
public class ComponentResult
{
    public string component_id;
    public string type;
    public float confidence;
    public int[] bbox; // [x, y, w, h]
    public List<CandidatePart> candidates;

    // frontend-only runtime state
    public bool isVisible;
    public bool isPinned;
}

[Serializable]
public class CandidatePart
{
    public string part_number;
    public float confidence;
    public string datasheet_url;
}