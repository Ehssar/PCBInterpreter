using System;
using System.Collections.Generic;
using UnityEngine;

public class BoardSessionManager : MonoBehaviour
{
    public BoardSession CurrentSession { get; private set; }

    public event Action<BoardSession> OnSessionCreated;

    public bool HasSession => CurrentSession != null;

    // Context Menu for testing in editor without needing to capture an image
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
                ? $"board_{response.request_id}"
                : response.board_id,
            mode = response.mode,
            analyzedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            labelsVisible = false,
            visibilityMode = string.IsNullOrEmpty(response.label_visibility_default)
                ? "hidden"
                : response.label_visibility_default,
            components = response.components ?? new List<ComponentResult>(),
            selectedComponentId = null,
            pinnedComponentIds = new List<string>()
        };

        // Notify Label Spawner of new Session
        OnSessionCreated?.Invoke(CurrentSession);

        InitializeRuntimeFlags();
        Debug.Log(
            $"BoardSession created: boardId={CurrentSession.boardId}, components={CurrentSession.components.Count}"
        );
    }

    private void InitializeRuntimeFlags()
    {
        if (CurrentSession == null || CurrentSession.components == null)
            return;

        foreach (var component in CurrentSession.components)
        {
            if (component.label == null)
            {
                component.label = new ComponentLabel
                {
                    title = component.type,
                    subtitle = "",
                    visible = false,
                    pinned = false
                };
            }

            component.label.visible = false;
            component.label.pinned = false;
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
        if (!HasSession) return;

        CurrentSession.labelsVisible = true;
        CurrentSession.visibilityMode = "all";

        foreach (var component in CurrentSession.components)
        {
            if (component.label != null)
                component.label.visible = true;
        }
    }

    public void HideAllLabels()
    {
        if (!HasSession) return;

        CurrentSession.labelsVisible = false;
        CurrentSession.visibilityMode = "hidden";

        foreach (var component in CurrentSession.components)
        {
            if (component.label != null && !component.label.pinned)
                component.label.visible = false;
        }
    }

    public void SelectComponent(string componentId)
    {
        if (!HasSession) return;

        CurrentSession.selectedComponentId = componentId;
        CurrentSession.visibilityMode = "focused";

        foreach (var component in CurrentSession.components)
        {
            if (component.label == null) continue;

            bool isSelected = component.component_id == componentId;
            component.label.visible = isSelected || component.label.pinned;
        }
    }

    public ComponentResult GetSelectedComponent()
    {
        if (!HasSession || string.IsNullOrEmpty(CurrentSession.selectedComponentId))
            return null;

        foreach (var component in CurrentSession.components)
        {
            if (component.component_id == CurrentSession.selectedComponentId)
                return component;
        }

        return null;
    }

    public void PinSelectedComponent()
    {
        var selected = GetSelectedComponent();
        if (selected == null || selected.label == null) return;

        selected.label.pinned = true;
        selected.label.visible = true;

        if (!CurrentSession.pinnedComponentIds.Contains(selected.component_id))
            CurrentSession.pinnedComponentIds.Add(selected.component_id);
    }

    public void UnpinAll()
    {
        if (!HasSession) return;

        CurrentSession.pinnedComponentIds.Clear();

        foreach (var component in CurrentSession.components)
        {
            if (component.label != null)
                component.label.pinned = false;
        }
    }
}