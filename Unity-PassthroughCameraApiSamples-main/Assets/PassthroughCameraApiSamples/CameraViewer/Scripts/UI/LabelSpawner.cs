using System.Collections.Generic;
using UnityEngine;

public enum LabelFilterCategory
{
    All,
    Resistor,
    Capacitor,
    IC,
    Diode,
    Transistor,
    Inductor,
    LED,
    Unknown
}

public class LabelSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BoardSessionManager boardSessionManager;
    [SerializeField] private Transform labelParent;
    [SerializeField] private Camera targetCamera;

    [Header("UI Set Label Prefab")]
    [SerializeField] private ComponentLabelCard labelCardPrefab;

    [Header("Placement")]
    [SerializeField] private float zOffset = 0.45f;
    //[SerializeField] private float yOffset = -0.05f;
    //[SerializeField] private float viewportYOffset = -0.08f;

    [Header("Runtime")]
    [SerializeField] private bool updateAnchorsEveryFrame = false;
    [SerializeField] private bool forceVisibleForDebug = false;

    private readonly Dictionary<string, ComponentLabelCard> spawnedLabels = new();
    private bool detailsMode = false;
    private LabelFilterCategory activeFilter = LabelFilterCategory.All;

    private void Awake()
    {
        if (labelParent == null)
            labelParent = transform;

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (boardSessionManager != null)
            boardSessionManager.OnSessionCreated += HandleSessionCreated;
    }

    private void OnDisable()
    {
        if (boardSessionManager != null)
            boardSessionManager.OnSessionCreated -= HandleSessionCreated;
    }

    private void LateUpdate()
    {
        RefreshVisibility();

        if (updateAnchorsEveryFrame)
            RefreshAnchorPositions();
    }

    public void ToggleDetailsMode()
    {
        detailsMode = !detailsMode;
        RefreshVisibility();
    }

    public bool IsDetailsMode()
    {
        return detailsMode;
    }

    public void SetDetailsMode(bool enabled)
    {
        detailsMode = enabled;
        RefreshVisibility();
    }

    public void SetFilter(LabelFilterCategory category)
    {
        activeFilter = category;
        Debug.Log($"[LabelSpawner] Active filter set to: {activeFilter}");
        RefreshVisibility();
    }

    public LabelFilterCategory GetActiveFilter()
    {
        return activeFilter;
    }

    private void HandleSessionCreated(BoardSession session)
    {
        Debug.Log($"[LabelSpawner] Session created. Components: {session?.components?.Count ?? 0}");
        ClearSpawnedLabels();

        if (session == null || session.components == null || labelCardPrefab == null)
            return;

        SpawnLabelsForSession(session);
        Debug.Log($"[LabelSpawner] Spawned labels: {spawnedLabels.Count}");
        RefreshVisibility();
    }

    private void SpawnLabelsForSession(BoardSession session)
    {
        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
                continue;

            ComponentLabelCard card = CreateLabelCard(component, session);
            if (card != null)
                spawnedLabels[component.component_id] = card;
        }
    }

    private ComponentLabelCard CreateLabelCard(ComponentResult component, BoardSession session)
    {
        Vector3 anchorPosition = ComputeWorldPosition(component, session);

        Debug.Log($"[LabelSpawner] Creating {component.component_id} at {anchorPosition}");

        ComponentLabelCard card = Instantiate(
            labelCardPrefab,
            anchorPosition,
            Quaternion.identity,
            labelParent
        );

        card.name = $"Label_{component.component_id}";

        card.Initialize(
            component: component,
            anchorPos: anchorPosition,
            cameraTarget: targetCamera != null ? targetCamera.transform : null,
            detailsMode: detailsMode
        );

        return card;
    }

    private void RefreshVisibility()
    {
        if (boardSessionManager == null || !boardSessionManager.HasSession)
        {
            SetAllLabelsActive(false);
            return;
        }

        BoardSession session = boardSessionManager.CurrentSession;
        if (session == null || session.components == null)
        {
            SetAllLabelsActive(false);
            return;
        }

        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
                continue;

            if (!spawnedLabels.TryGetValue(component.component_id, out ComponentLabelCard card) || card == null)
                continue;

            bool hasEnrichment = component.enrichment != null;
            bool visible = session.labelsVisible && component.IsLabelVisible();

            if (detailsMode && !hasEnrichment)
                visible = false;

            if (!MatchesFilter(component))
                visible = false;

            if (forceVisibleForDebug)
                visible = true;

            card.SetVisible(visible);

            if (!visible)
                continue;

            card.RefreshText(component, detailsMode);

            if (targetCamera != null)
                card.SetCameraTarget(targetCamera.transform);
        }
    }

    private bool MatchesFilter(ComponentResult component)
    {
        if (activeFilter == LabelFilterCategory.All)
            return true;

        if (component == null)
            return false;

        LabelFilterCategory componentCategory = MapTypeToCategory(component.type);
        return componentCategory == activeFilter;
    }

    private LabelFilterCategory MapTypeToCategory(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return LabelFilterCategory.Unknown;

        switch (type.Trim().ToLower())
        {
            case "resistor":
                return LabelFilterCategory.Resistor;

            case "capacitor":
                return LabelFilterCategory.Capacitor;

            case "ic":
                return LabelFilterCategory.IC;

            case "diode":
                return LabelFilterCategory.Diode;

            case "transistor":
                return LabelFilterCategory.Transistor;

            case "inductor":
                return LabelFilterCategory.Inductor;

            case "led":
                return LabelFilterCategory.LED;

            default:
                return LabelFilterCategory.Unknown;
        }
    }

    private void RefreshAnchorPositions()
    {
        if (boardSessionManager == null || !boardSessionManager.HasSession)
            return;

        BoardSession session = boardSessionManager.CurrentSession;
        if (session == null || session.components == null)
            return;

        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
                continue;

            if (!spawnedLabels.TryGetValue(component.component_id, out ComponentLabelCard card) || card == null)
                continue;

            Vector3 anchorPosition = ComputeWorldPosition(component, session);
            card.SetAnchorWorldPosition(anchorPosition);
        }
    }

    private Vector3 ComputeWorldPosition(ComponentResult component, BoardSession session)
    {
        if (targetCamera == null || component == null || component.bbox == null || component.bbox.Length < 4)
            return transform.position;

        // Backend bbox format: [left, top, width, height]
        float left = component.bbox[0];
        float top = component.bbox[1];
        float width = component.bbox[2];
        float height = component.bbox[3];

        // Use the bbox center horizontally.
        // Vertically, bias slightly below center so the card feels connected to the part
        // without sitting too high in the user's view.
        float anchorX = left + (width * 0.5f);
        float anchorY = top + (height * 0.70f);

        float sourceWidth = 0f;
        float sourceHeight = 0f;

        if (session != null)
        {
            if (session.imageWidth > 0)
                sourceWidth = session.imageWidth;

            if (session.imageHeight > 0)
                sourceHeight = session.imageHeight;
        }

        if (sourceWidth <= 0f)
            sourceWidth = Mathf.Max(1f, targetCamera.pixelWidth);

        if (sourceHeight <= 0f)
            sourceHeight = Mathf.Max(1f, targetCamera.pixelHeight);

        // Normalize into image/viewport space.
        float normalizedX = Mathf.Clamp01(anchorX / sourceWidth);
        float normalizedY = Mathf.Clamp01(1f - (anchorY / sourceHeight));

        // Re-center into [-1, 1] so we can shape the layout more deliberately.
        float centeredX = (normalizedX - 0.5f) * 2f;
        float centeredY = (normalizedY - 0.5f) * 2f;

        // Preserve PCB shape, but compress extremes so labels do not get pushed too far
        // toward the top/bottom edges of the user's FOV.
        // Smaller values = more compression near edges.
        float horizontalShapeStrength = 0.85f;
        float verticalShapeStrength = 0.65f;

        float shapedX = centeredX * horizontalShapeStrength;
        float shapedY = centeredY * verticalShapeStrength;

        // Global downward bias so labels sit a bit lower and more comfortably.
        // Negative value moves them lower in view.
        shapedY -= 0.18f;

        // Convert back into viewport coordinates.
        float finalViewportX = Mathf.Clamp01(0.5f + (shapedX * 0.5f));
        float finalViewportY = Mathf.Clamp01(0.5f + (shapedY * 0.5f));

        // Project a ray from the adjusted viewport point.
        Ray ray = targetCamera.ViewportPointToRay(new Vector3(finalViewportX, finalViewportY, 0f));

        // Place cards in a comfortable band in front of the user.
        // Since this is FOV-projected rather than board-anchored, this depth is the main
        // "interaction zone" where labels live before the user drags them.
        float distanceFromCamera = zOffset;

        Vector3 worldPoint = ray.origin + ray.direction * distanceFromCamera;

        // Small world-space bias to keep labels slightly below eye line and a touch forward.
        // This helps readability without destroying the rough board shape.
        Vector3 cameraUp = targetCamera.transform.up;
        Vector3 cameraForward = targetCamera.transform.forward;

        worldPoint += cameraUp * -0.04f;
        worldPoint += cameraForward * 0.02f;

        Debug.Log(
            $"[LabelSpawner] bbox=({left:F1},{top:F1},{width:F1},{height:F1}) " +
            $"anchor=({anchorX:F1},{anchorY:F1}) " +
            $"src=({sourceWidth:F0},{sourceHeight:F0}) " +
            $"norm=({normalizedX:F3},{normalizedY:F3}) " +
            $"centered=({centeredX:F3},{centeredY:F3}) " +
            $"viewport=({finalViewportX:F3},{finalViewportY:F3}) " +
            $"world={worldPoint}"
        );

        return worldPoint;
    }

    private void SetAllLabelsActive(bool active)
    {
        foreach (var kvp in spawnedLabels)
        {
            if (kvp.Value != null)
                kvp.Value.SetVisible(active);
        }
    }

    private void ClearSpawnedLabels()
    {
        foreach (var kvp in spawnedLabels)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value.gameObject);
        }

        spawnedLabels.Clear();
    }
}