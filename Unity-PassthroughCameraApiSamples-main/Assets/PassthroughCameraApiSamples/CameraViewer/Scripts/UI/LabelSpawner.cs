using System.Collections;
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

    [Header("World Spawn")]
    [SerializeField] private float sessionSpawnDistance = 0.45f;
    [SerializeField] private float sessionSpawnVerticalOffset = -0.08f;
    [SerializeField] private bool faceUserOnSpawn = true;

    [Header("Board Placement (relative to session root)")]
    [SerializeField] private Vector3 boardOverlayLocalOffset = new Vector3(0f, -0.02f, 0.35f);
    [SerializeField] private Vector3 boardOverlayEulerOffset = Vector3.zero;

    [Header("Label Placement (relative to session root / board)")]
    [SerializeField] private float labelPlaneForwardOffset = -0.03f;
    [SerializeField] private float boardPlaneWidth = 0.55f;
    [SerializeField] private float boardPlaneHeight = 0.36f;
    [SerializeField] private float labelHorizontalSpread = 1.05f;
    [SerializeField] private float labelVerticalSpread = 0.90f;
    [SerializeField] private float labelVerticalBias = -0.03f;

    [Header("Runtime")]
    [SerializeField] private bool updateAnchorsEveryFrame = false;
    [SerializeField] private bool forceVisibleForDebug = false;
    [SerializeField] private bool showAllBoxesByDefault = true;

    private readonly Dictionary<string, ComponentLabelCard> spawnedLabels = new();
    private readonly Dictionary<string, Vector3> labelLocalAnchors = new();

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

    public void RecenterCurrentSession()
    {
        if (currentSessionRoot == null)
            return;

        Pose pose = GetInitialSessionPose();
        currentSessionRoot.transform.position = pose.position;
        currentSessionRoot.transform.rotation = pose.rotation;

        RefreshAnchorPositions();

        if (currentBoardOverlayCard != null)
            currentBoardOverlayCard.SnapFacingUser();
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

        if (labelParent != null)
            currentSessionRoot.transform.SetParent(labelParent, worldPositionStays: true);

        Pose pose = GetInitialSessionPose();
        currentSessionRoot.transform.position = pose.position;
        currentSessionRoot.transform.rotation = pose.rotation;
        currentSessionRoot.transform.localScale = Vector3.one;
    }

    private Pose GetInitialSessionPose()
    {
        Transform cam = targetCamera != null ? targetCamera.transform : null;

        if (cam == null)
            return new Pose(transform.position, Quaternion.identity);

        Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = cam.forward;

        flatForward.Normalize();

        Vector3 spawnPos =
            cam.position +
            flatForward * sessionSpawnDistance +
            Vector3.up * sessionSpawnVerticalOffset;

        Quaternion spawnRot = faceUserOnSpawn
            ? Quaternion.LookRotation(flatForward, Vector3.up)
            : Quaternion.identity;

        return new Pose(spawnPos, spawnRot);
    }

    private void SpawnBoardOverlayForSession(BoardSession session)
    {
        if (boardOverlayCardPrefab == null || currentSessionRoot == null)
            return;

        Texture2D boardTexture = BuildTextureFromSession(session);

        if (boardTexture == null)
        {
            Debug.Log("[LabelSpawner] Skipping BoardOverlayCard spawn (no image)");
            return;
        }

        currentBoardOverlayCard = Instantiate(
            boardOverlayCardPrefab,
            currentSessionRoot.transform
        );

        currentBoardOverlayCard.name = "BoardOverlayCard";
        currentBoardOverlayCard.transform.localPosition = boardOverlayLocalOffset;
        currentBoardOverlayCard.transform.localRotation = Quaternion.Euler(boardOverlayEulerOffset);
        currentBoardOverlayCard.transform.localScale = Vector3.one;

        currentBoardOverlayCard.Initialize(
            boardTexture,
            targetCamera != null ? targetCamera.transform : null
        );

        float imageWidth = session.imageWidth > 0 ? session.imageWidth : 1f;
        float imageHeight = session.imageHeight > 0 ? session.imageHeight : 1f;

        StartCoroutine(BuildBoardBoxesNextFrame(session, imageWidth, imageHeight));
    }

    private IEnumerator BuildBoardBoxesNextFrame(
        BoardSession session,
        float imageWidth,
        float imageHeight
    )
    {
        yield return null;

        if (currentBoardOverlayCard == null || session == null || session.components == null)
            yield break;

        currentBoardOverlayCard.BuildBoxes(session.components, imageWidth, imageHeight);
        currentBoardOverlayCard.ShowAllBoxes(showAllBoxesByDefault);

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
        if (currentSessionRoot == null)
            return null;

        Vector3 localAnchor = ComputeLocalLabelPosition(component, session);
        Vector3 worldAnchor = currentSessionRoot.transform.TransformPoint(localAnchor);

        Debug.Log($"[LabelSpawner] Creating {component.component_id} at local={localAnchor} world={worldAnchor}");

        ComponentLabelCard card = Instantiate(
            labelCardPrefab,
            worldAnchor,
            Quaternion.identity,
            currentSessionRoot.transform
        );

        card.name = $"Label_{component.component_id}";
        labelLocalAnchors[component.component_id] = localAnchor;

        card.Initialize(
            component: component,
            anchorPos: worldAnchor,
            cameraTarget: targetCamera != null ? targetCamera.transform : null,
            detailsMode: detailsMode
        );

        return card;
    }

    private Vector3 ComputeLocalLabelPosition(ComponentResult component, BoardSession session)
    {
        if (component == null || component.bbox == null || component.bbox.Length < 4)
            return boardOverlayLocalOffset + new Vector3(0f, 0f, labelPlaneForwardOffset);

        float sourceWidth = 1f;
        float sourceHeight = 1f;

        if (session != null)
        {
            if (session.imageWidth > 0)
                sourceWidth = session.imageWidth;

            if (session.imageHeight > 0)
                sourceHeight = session.imageHeight;
        }

        float left = component.bbox[0];
        float top = component.bbox[1];
        float width = component.bbox[2];
        float height = component.bbox[3];

        float anchorX = left + (width * 0.5f);
        float anchorY = top + (height * 0.70f);

        float normalizedX = Mathf.Clamp01(anchorX / sourceWidth);
        float normalizedY = Mathf.Clamp01(anchorY / sourceHeight);

        float centeredX = (normalizedX - 0.5f) * 2f;
        float centeredY = (0.5f - normalizedY) * 2f;

        float localX = centeredX * (boardPlaneWidth * 0.5f) * labelHorizontalSpread;
        float localY = centeredY * (boardPlaneHeight * 0.5f) * labelVerticalSpread + labelVerticalBias;
        float localZ = labelPlaneForwardOffset;

        Vector3 local = boardOverlayLocalOffset + new Vector3(localX, localY, localZ);

        Debug.Log(
            $"[LabelSpawner] local anchor for {component.component_id} " +
            $"bbox=({left:F1},{top:F1},{width:F1},{height:F1}) " +
            $"norm=({normalizedX:F3},{normalizedY:F3}) " +
            $"centered=({centeredX:F3},{centeredY:F3}) " +
            $"local={local}"
        );

        return local;
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

        LabelFilterCategory componentCategory = MapTypeToCategory(component.resolved_type);
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

        if (currentSessionRoot == null)
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

            if (!labelLocalAnchors.TryGetValue(component.component_id, out Vector3 localAnchor))
                localAnchor = ComputeLocalLabelPosition(component, session);

            Vector3 worldAnchor = currentSessionRoot.transform.TransformPoint(localAnchor);
            card.SetAnchorWorldPosition(worldAnchor);
        }
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
        labelLocalAnchors.Clear();

        hoveredComponentId = null;
        selectedComponentId = null;

        currentBoardOverlayCard = null;

        if (currentSessionRoot != null)
            Destroy(currentSessionRoot);

        currentSessionRoot = null;
    }
}