using System;
using System.Collections.Generic;

[Serializable]
public class BoardSession
{
    public string requestId;
    public string boardId;
    public string mode;
    public long analyzedAtUnixMs;

    public byte[] capturedImageJpg;

    public int imageWidth;
    public int imageHeight;

    public bool labelsVisible;
    public string visibilityMode;

    public List<ComponentResult> components = new();

    public string selectedComponentId;
    public List<string> pinnedComponentIds = new();
}