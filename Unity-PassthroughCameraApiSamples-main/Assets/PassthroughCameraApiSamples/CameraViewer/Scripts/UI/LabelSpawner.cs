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

    [Header("Prefabs")]
    [SerializeField] private ComponentLabelCard labelCardPrefab;
    [SerializeField] private BoardOverlayCard boardOverlayCardPrefab;

    [Header("Placement")]
    [SerializeField] private float zOffset = 0.45f;
    [SerializeField] private Vector3 boardOverlayLocalOffset = new Vector3(0f, -0.02f, 0.55f);
    [SerializeField] private Vector3 boardOverlayEulerOffset = Vector3.zero;

    [Header("Runtime")]
    [SerializeField] private bool updateAnchorsEveryFrame = false;
    [SerializeField] private bool forceVisibleForDebug = false;
    [SerializeField] private bool showAllBoxesByDefault = false;

    private readonly Dictionary<string, ComponentLabelCard> spawnedLabels = new();

    private GameObject currentSessionRoot;
    private BoardOverlayCard currentBoardOverlayCard;

    private bool detailsMode = false;
    private LabelFilterCategory activeFilter = LabelFilterCategory.All;

    private string hoveredComponentId;
    private string selectedComponentId;

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

        ClearCurrentSessionVisuals();

        if (session == null || session.components == null || labelCardPrefab == null)
            return;

        CreateSessionRoot();
        SpawnBoardOverlayForSession(session);
        SpawnLabelsForSession(session);

        Debug.Log($"[LabelSpawner] Spawned labels: {spawnedLabels.Count}");
        RefreshVisibility();
        RefreshOverlayInteractionState();
    }

    private void CreateSessionRoot()
    {
        currentSessionRoot = new GameObject("SpawnedBoardSessionRoot");
        currentSessionRoot.transform.SetParent(labelParent, worldPositionStays: false);
        currentSessionRoot.transform.localPosition = Vector3.zero;
        currentSessionRoot.transform.localRotation = Quaternion.identity;
        currentSessionRoot.transform.localScale = Vector3.one;
    }

    private void SpawnBoardOverlayForSession(BoardSession session)
    {
        if (boardOverlayCardPrefab == null || currentSessionRoot == null)
            return;

        Vector3 worldPos = GetBoardOverlayWorldPosition();
        Quaternion worldRot = GetBoardOverlayWorldRotation();

        currentBoardOverlayCard = Instantiate(
            boardOverlayCardPrefab,
            worldPos,
            worldRot,
            currentSessionRoot.transform
        );

        currentBoardOverlayCard.name = "BoardOverlayCard";

        Texture2D boardTexture = BuildTextureFromSession(session);
        if (boardTexture != null)
            currentBoardOverlayCard.SetBoardTexture(boardTexture);

        float imageWidth = session != null && session.imageWidth > 0 ? session.imageWidth : 1f;
        float imageHeight = session != null && session.imageHeight > 0 ? session.imageHeight : 1f;

        StartCoroutine(BuildBoardBoxesNextFrame(session, imageWidth, imageHeight));
    }

    private System.Collections.IEnumerator BuildBoardBoxesNextFrame(
        BoardSession session,
        float imageWidth,
        float imageHeight
    )
    {
        yield return null;

        if (currentBoardOverlayCard == null || session == null || session.components == null)
            yield break;

        currentBoardOverlayCard.BuildBoxes(session.components, imageWidth, imageHeight);

        if (showAllBoxesByDefault)
            currentBoardOverlayCard.ShowAllBoxes(true);
        else
            currentBoardOverlayCard.ShowAllBoxes(true);

        RefreshOverlayInteractionState();
    }

    private void SpawnLabelsForSession(BoardSession session)
    {
        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
                continue;

            ComponentLabelCard card = CreateLabelCard(component, session);
            if (card != null)
            {
                spawnedLabels[component.component_id] = card;
                RegisterCardEvents(card);
            }
        }
    }

    private ComponentLabelCard CreateLabelCard(ComponentResult component, BoardSession session)
    {
        Vector3 anchorPosition = ComputeWorldPosition(component, session);

        Debug.Log($"[LabelSpawner] Creating {component.component_id} at {anchorPosition}");

        Transform parent = currentSessionRoot != null ? currentSessionRoot.transform : labelParent;

        ComponentLabelCard card = Instantiate(
            labelCardPrefab,
            anchorPosition,
            Quaternion.identity,
            parent
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

    private void RegisterCardEvents(ComponentLabelCard card)
    {
        if (card == null)
            return;

        card.HoverStarted += HandleCardHoverStarted;
        card.HoverEnded += HandleCardHoverEnded;
        card.SelectedStarted += HandleCardSelectedStarted;
        card.SelectedEnded += HandleCardSelectedEnded;
    }

    private void UnregisterCardEvents(ComponentLabelCard card)
    {
        if (card == null)
            return;

        card.HoverStarted -= HandleCardHoverStarted;
        card.HoverEnded -= HandleCardHoverEnded;
        card.SelectedStarted -= HandleCardSelectedStarted;
        card.SelectedEnded -= HandleCardSelectedEnded;
    }

    private void HandleCardHoverStarted(string componentId)
    {
        hoveredComponentId = componentId;
        RefreshOverlayInteractionState();
    }

    private void HandleCardHoverEnded(string componentId)
    {
        if (hoveredComponentId == componentId)
            hoveredComponentId = null;

        RefreshOverlayInteractionState();
    }

    private void HandleCardSelectedStarted(string componentId)
    {
        selectedComponentId = componentId;
        RefreshOverlayInteractionState();
    }

    private void HandleCardSelectedEnded(string componentId)
    {
        if (selectedComponentId == componentId)
            selectedComponentId = null;

        RefreshOverlayInteractionState();
    }

    private void RefreshOverlayInteractionState()
    {
        if (currentBoardOverlayCard == null)
            return;

        currentBoardOverlayCard.SetHoveredComponent(hoveredComponentId);
        currentBoardOverlayCard.SetSelectedComponent(selectedComponentId);
    }

    private void RefreshVisibility()
    {
        if (boardSessionManager == null || !boardSessionManager.HasSession)
        {
            SetCurrentSessionVisible(false);
            return;
        }

        BoardSession session = boardSessionManager.CurrentSession;
        if (session == null || session.components == null)
        {
            SetCurrentSessionVisible(false);
            return;
        }

        bool sessionVisible = session.labelsVisible || forceVisibleForDebug;
        SetCurrentSessionVisible(sessionVisible);

        HashSet<string> visibleIds = new HashSet<string>();

        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
                continue;

            if (!spawnedLabels.TryGetValue(component.component_id, out ComponentLabelCard card) || card == null)
                continue;

            bool hasEnrichment = component.enrichment != null;
            bool visible = sessionVisible && component.IsLabelVisible();

            if (detailsMode && !hasEnrichment)
                visible = false;

            if (!MatchesFilter(component))
                visible = false;

            if (forceVisibleForDebug)
                visible = true;

            card.SetVisible(visible);

            if (!visible)
                continue;

            visibleIds.Add(component.component_id);

            card.RefreshText(component, detailsMode);

            if (targetCamera != null)
                card.SetCameraTarget(targetCamera.transform);
        }

        if (currentBoardOverlayCard != null)
        {
            currentBoardOverlayCard.ShowAllBoxes(sessionVisible || forceVisibleForDebug);

            if (!sessionVisible && !forceVisibleForDebug)
            {
                currentBoardOverlayCard.ClearHighlight();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(hoveredComponentId) && !visibleIds.Contains(hoveredComponentId))
                    hoveredComponentId = null;

                if (!string.IsNullOrWhiteSpace(selectedComponentId) && !visibleIds.Contains(selectedComponentId))
                    selectedComponentId = null;

                RefreshOverlayInteractionState();
            }
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

        float left = component.bbox[0];
        float top = component.bbox[1];
        float width = component.bbox[2];
        float height = component.bbox[3];

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

        float normalizedX = Mathf.Clamp01(anchorX / sourceWidth);
        float normalizedY = Mathf.Clamp01(1f - (anchorY / sourceHeight));

        float centeredX = (normalizedX - 0.5f) * 2f;
        float centeredY = (normalizedY - 0.5f) * 2f;

        float horizontalShapeStrength = 0.85f;
        float verticalShapeStrength = 0.65f;

        float shapedX = centeredX * horizontalShapeStrength;
        float shapedY = centeredY * verticalShapeStrength;

        shapedY -= 0.18f;

        float finalViewportX = Mathf.Clamp01(0.5f + (shapedX * 0.5f));
        float finalViewportY = Mathf.Clamp01(0.5f + (shapedY * 0.5f));

        Ray ray = targetCamera.ViewportPointToRay(new Vector3(finalViewportX, finalViewportY, 0f));

        float distanceFromCamera = zOffset;
        Vector3 worldPoint = ray.origin + ray.direction * distanceFromCamera;

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

    private Vector3 GetBoardOverlayWorldPosition()
    {
        if (targetCamera == null)
            return transform.position;

        Transform cam = targetCamera.transform;

        return cam.position
             + cam.forward * boardOverlayLocalOffset.z
             + cam.right * boardOverlayLocalOffset.x
             + cam.up * boardOverlayLocalOffset.y;
    }

    private Quaternion GetBoardOverlayWorldRotation()
    {
        if (targetCamera == null)
            return Quaternion.identity;

        Quaternion lookRot = Quaternion.LookRotation(targetCamera.transform.forward, targetCamera.transform.up);
        return lookRot * Quaternion.Euler(boardOverlayEulerOffset);
    }

    private Texture2D BuildTextureFromSession(BoardSession session)
    {
        if (session == null || session.capturedImageJpg == null || session.capturedImageJpg.Length == 0)
            return null;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loaded = texture.LoadImage(session.capturedImageJpg, markNonReadable: false);

        if (!loaded)
        {
            Debug.LogWarning("[LabelSpawner] Failed to load board image texture from session JPG bytes.");
            Destroy(texture);
            return null;
        }

        texture.name = "BoardCaptureTexture";
        return texture;
    }

    private void SetCurrentSessionVisible(bool visible)
    {
        if (currentSessionRoot != null)
            currentSessionRoot.SetActive(visible);
    }

    private void ClearCurrentSessionVisuals()
    {
        foreach (var kvp in spawnedLabels)
        {
            if (kvp.Value != null)
            {
                UnregisterCardEvents(kvp.Value);
                Destroy(kvp.Value.gameObject);
            }
        }

        spawnedLabels.Clear();

        hoveredComponentId = null;
        selectedComponentId = null;

        currentBoardOverlayCard = null;

        if (currentSessionRoot != null)
            Destroy(currentSessionRoot);

        currentSessionRoot = null;
    }
}