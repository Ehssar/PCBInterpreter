using System;
using System.Collections.Generic;

[Serializable]
public class BoardSession
{
    public string requestId;
    public string boardId;
    public string mode;
    public long analyzedAtUnixMs;

    public bool labelsVisible;
    public string visibilityMode; // "hidden", "focused", "all"

    public List<ComponentResult> components = new List<ComponentResult>();

    public string selectedComponentId;
    public List<string> pinnedComponentIds = new List<string>();
}