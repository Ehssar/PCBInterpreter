using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private float yOffset = 0.05f;
    [SerializeField] private bool faceCameraEveryFrame = true;
    [SerializeField] private bool updateAnchorsEveryFrame = false;

    private readonly Dictionary<string, ComponentLabelCard> spawnedLabels = new();
    private bool detailsMode = false;

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

        if (faceCameraEveryFrame)
            FaceLabelsTowardCamera();
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

            card.SetVisible(visible);

            if (!visible)
                continue;

            card.RefreshText(component, detailsMode);
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

    private void FaceLabelsTowardCamera()
    {
        if (targetCamera == null)
            return;

        foreach (var kvp in spawnedLabels)
        {
            ComponentLabelCard card = kvp.Value;
            if (card == null || !card.gameObject.activeSelf)
                continue;

            Vector3 direction = card.transform.position - targetCamera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
                card.transform.forward = direction.normalized;
        }
    }

    private Vector3 ComputeWorldPosition(ComponentResult component, BoardSession session)
    {
        if (targetCamera == null || component == null || component.bbox == null || component.bbox.Length < 4)
            return transform.position;

        // Backend bbox format is [left, top, width, height]
        float left = component.bbox[0];
        float top = component.bbox[1];
        float width = component.bbox[2];
        float height = component.bbox[3];

        // Use the true bbox center
        float centerX = left + (width * 0.5f);
        float centerY = top + (height * 0.5f);

        // Prefer backend image dimensions
        float sourceWidth = 0f;
        float sourceHeight = 0f;

        if (session != null)
        {
            if (session.imageWidth > 0)
                sourceWidth = session.imageWidth;

            if (session.imageHeight > 0)
                sourceHeight = session.imageHeight;
        }

        // Fallback only if backend dimensions are missing
        if (sourceWidth <= 0f)
            sourceWidth = Mathf.Max(1f, targetCamera.pixelWidth);

        if (sourceHeight <= 0f)
            sourceHeight = Mathf.Max(1f, targetCamera.pixelHeight);

        // Convert image-space pixel coords to viewport-space [0,1]
        float normalizedX = Mathf.Clamp01(centerX / sourceWidth);
        float normalizedY = Mathf.Clamp01(1f - (centerY / sourceHeight));

        // Cast a ray through that viewport point
        Ray ray = targetCamera.ViewportPointToRay(new Vector3(normalizedX, normalizedY, 0f));

        // Place label along ray at configurable depth
        Vector3 worldPoint = ray.origin + ray.direction * zOffset;

        // Small upward bias if desired
        worldPoint.y += yOffset;

        Debug.Log(
            $"[LabelSpawner] bbox=({left:F1},{top:F1},{width:F1},{height:F1}) " +
            $"center=({centerX:F1},{centerY:F1}) " +
            $"src=({sourceWidth:F0},{sourceHeight:F0}) " +
            $"norm=({normalizedX:F3},{normalizedY:F3}) " +
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