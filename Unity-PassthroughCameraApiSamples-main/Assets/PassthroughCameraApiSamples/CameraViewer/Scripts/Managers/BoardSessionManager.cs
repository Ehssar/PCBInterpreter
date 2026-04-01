using System;
using System.Collections.Generic;
using UnityEngine;

public class BoardSessionManager : MonoBehaviour
{
    public BoardSession CurrentSession { get; private set; }

    public event Action<BoardSession> OnSessionCreated;

    public bool HasSession => CurrentSession != null;

    [ContextMenu("Show All Labels")]
    public void DebugShowAllLabels()
    {
        ShowAllLabels();
    }

    [ContextMenu("Hide All Labels")]
    public void DebugHideAllLabels()
    {
        HideAllLabels();
    }

    public void CreateSession(AnalyzeResponse response)
    {
        if (response == null)
        {
            Debug.LogWarning("CreateSession called with null response.");
            return;
        }

        CurrentSession = new BoardSession
        {
            requestId = response.request_id,
            boardId = string.IsNullOrEmpty(response.board_id)
                ? (!string.IsNullOrEmpty(response.request_id) ? $"board_{response.request_id}" : Guid.NewGuid().ToString())
                : response.board_id,
            mode = response.mode,
            analyzedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            imageWidth = response.image_width,
            imageHeight = response.image_height,
            labelsVisible = false,
            visibilityMode = string.IsNullOrEmpty(response.label_visibility_default)
                ? "hidden"
                : response.label_visibility_default,
            components = response.components ?? new List<ComponentResult>(),
            selectedComponentId = null,
            pinnedComponentIds = new List<string>()
        };

        InitializeRuntimeFlags();

        // Apply initial visibility mode after labels are normalized.
        ApplyInitialVisibilityMode();

        Debug.Log(
            $"BoardSession created: boardId={CurrentSession.boardId}, components={CurrentSession.components.Count}, visibilityMode={CurrentSession.visibilityMode}"
        );

        OnSessionCreated?.Invoke(CurrentSession);
    }

    private void InitializeRuntimeFlags()
    {
        if (CurrentSession == null || CurrentSession.components == null)
            return;

        foreach (var component in CurrentSession.components)
        {
            if (component == null)
                continue;

            if (component.label == null)
            {
                component.label = new ComponentLabel();
            }

            // Normalize title/subtitle from the resolved backend/model fallbacks.
            component.label.title = component.GetResolvedLabelTitle();
            component.label.subtitle = component.GetResolvedLabelSubtitle();

            // Preserve pinned only if explicitly set; otherwise default false.
            component.label.pinned = component.label.pinned;

            // Visible state is determined by session mode, not stale JSON state.
            component.label.visible = false;

            if (component.label.pinned && !CurrentSession.pinnedComponentIds.Contains(component.component_id))
            {
                CurrentSession.pinnedComponentIds.Add(component.component_id);
            }
        }
    }

    private void ApplyInitialVisibilityMode()
    {
        if (!HasSession)
            return;

        string mode = string.IsNullOrEmpty(CurrentSession.visibilityMode)
            ? "hidden"
            : CurrentSession.visibilityMode.ToLowerInvariant();

        switch (mode)
        {
            case "all":
            case "visible":
                ShowAllLabels();
                break;

            case "hidden":
            default:
                HideAllLabels();
                break;
        }
    }

    public void ClearSession()
    {
        CurrentSession = null;
        Debug.Log("BoardSession cleared.");
        OnSessionCreated?.Invoke(null);
    }

    public void ShowAllLabels()
    {
        if (!HasSession)
            return;

        CurrentSession.labelsVisible = true;
        CurrentSession.visibilityMode = "all";

        foreach (var component in CurrentSession.components)
        {
            if (component?.label == null)
                continue;

            component.label.visible = true;
        }
    }

    public void HideAllLabels()
    {
        if (!HasSession)
            return;

        CurrentSession.labelsVisible = false;
        CurrentSession.visibilityMode = "hidden";

        foreach (var component in CurrentSession.components)
        {
            if (component?.label == null)
                continue;

            component.label.visible = component.label.pinned;
        }
    }

    public void SelectComponent(string componentId)
    {
        if (!HasSession || string.IsNullOrWhiteSpace(componentId))
            return;

        CurrentSession.selectedComponentId = componentId;
        CurrentSession.visibilityMode = "focused";
        CurrentSession.labelsVisible = true;

        foreach (var component in CurrentSession.components)
        {
            if (component?.label == null)
                continue;

            bool isSelected = component.component_id == componentId;
            component.label.visible = isSelected || component.label.pinned;
        }
    }

    public ComponentResult GetSelectedComponent()
    {
        if (!HasSession || string.IsNullOrWhiteSpace(CurrentSession.selectedComponentId))
            return null;

        foreach (var component in CurrentSession.components)
        {
            if (component != null && component.component_id == CurrentSession.selectedComponentId)
                return component;
        }

        return null;
    }

    public ComponentResult GetComponentById(string componentId)
    {
        if (!HasSession || string.IsNullOrWhiteSpace(componentId))
            return null;

        foreach (var component in CurrentSession.components)
        {
            if (component != null && component.component_id == componentId)
                return component;
        }

        return null;
    }

    public void PinSelectedComponent()
    {
        if (!HasSession)
            return;

        var selected = GetSelectedComponent();
        if (selected?.label == null)
            return;

        selected.label.pinned = true;
        selected.label.visible = true;

        if (!CurrentSession.pinnedComponentIds.Contains(selected.component_id))
        {
            CurrentSession.pinnedComponentIds.Add(selected.component_id);
        }
    }

    public void UnpinComponent(string componentId)
    {
        if (!HasSession || string.IsNullOrWhiteSpace(componentId))
            return;

        var component = GetComponentById(componentId);
        if (component?.label == null)
            return;

        component.label.pinned = false;
        CurrentSession.pinnedComponentIds.Remove(componentId);

        // Recompute visibility based on current mode.
        switch (CurrentSession.visibilityMode)
        {
            case "all":
                component.label.visible = true;
                break;
            case "focused":
                component.label.visible = component.component_id == CurrentSession.selectedComponentId;
                break;
            case "hidden":
            default:
                component.label.visible = false;
                break;
        }
    }

    public void UnpinAll()
    {
        if (!HasSession)
            return;

        CurrentSession.pinnedComponentIds.Clear();

        foreach (var component in CurrentSession.components)
        {
            if (component?.label == null)
                continue;

            component.label.pinned = false;
        }

        // Reapply current visibility mode after clearing pins.
        switch (CurrentSession.visibilityMode)
        {
            case "all":
                ShowAllLabels();
                break;
            case "focused":
                if (!string.IsNullOrWhiteSpace(CurrentSession.selectedComponentId))
                    SelectComponent(CurrentSession.selectedComponentId);
                else
                    HideAllLabels();
                break;
            case "hidden":
            default:
                HideAllLabels();
                break;
        }
    }
}