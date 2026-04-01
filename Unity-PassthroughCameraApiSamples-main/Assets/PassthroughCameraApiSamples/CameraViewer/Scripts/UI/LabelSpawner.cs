using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LabelSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BoardSessionManager boardSessionManager;
    [SerializeField] private Transform labelParent;
    [SerializeField] private Camera targetCamera;

    [Header("Label Style")]
    [SerializeField] private Vector3 labelScale = new Vector3(0.0025f, 0.0025f, 0.0025f);
    [SerializeField] private float zOffset = 2.0f;
    [SerializeField] private float yOffset = 0.05f;
    [SerializeField] private int fontSize = 4;
    [SerializeField] private bool faceCameraEveryFrame = true;

    private readonly Dictionary<string, GameObject> spawnedLabels = new();

    private void Awake()
    {
        if (labelParent == null)
        {
            labelParent = transform;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void OnEnable()
    {
        if (boardSessionManager != null)
        {
            boardSessionManager.OnSessionCreated += HandleSessionCreated;
        }
    }

    private void OnDisable()
    {
        if (boardSessionManager != null)
        {
            boardSessionManager.OnSessionCreated -= HandleSessionCreated;
        }
    }

    private void LateUpdate()
    {
        RefreshVisibility();

        if (faceCameraEveryFrame)
        {
            FaceLabelsTowardCamera();
        }
    }

    private void HandleSessionCreated(BoardSession session)
    {
        ClearSpawnedLabels();

        if (session == null || session.components == null)
        {
            return;
        }

        SpawnLabelsForSession(session);
        RefreshVisibility();
    }

    private void SpawnLabelsForSession(BoardSession session)
    {
        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.component_id))
            {
                continue;
            }

            GameObject labelObject = CreateLabelObject(component);
            spawnedLabels[component.component_id] = labelObject;
        }
    }

    private GameObject CreateLabelObject(ComponentResult component)
    {
        GameObject go = new GameObject($"Label_{component.component_id}");
        go.transform.SetParent(labelParent, false);
        go.transform.localScale = labelScale;
        go.transform.position = ComputeWorldPosition(component);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = BuildLabelText(component);
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;

        return go;
    }

    private string BuildLabelText(ComponentResult component)
    {
        if (component == null)
        {
            return "Unknown Component";
        }

        string title = component.GetResolvedLabelTitle();
        string subtitle = component.GetResolvedLabelSubtitle();

        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return title;
        }

        return $"{title}\n{subtitle}";
    }

    private Vector3 ComputeWorldPosition(ComponentResult component)
    {
        if (targetCamera == null || component == null || component.bbox == null || component.bbox.Length < 4)
        {
            return transform.position;
        }

        float x = component.bbox[0];
        float y = component.bbox[1];
        float w = component.bbox[2];

        float centerX = x + (w * 0.5f);
        float topY = y;

        float cameraPixelWidth = Mathf.Max(1f, targetCamera.pixelWidth);
        float cameraPixelHeight = Mathf.Max(1f, targetCamera.pixelHeight);

        float normalizedX = Mathf.Clamp01(centerX / cameraPixelWidth);
        float normalizedY = Mathf.Clamp01(1f - (topY / cameraPixelHeight));

        Vector3 viewportPoint = new Vector3(normalizedX, normalizedY, zOffset);
        Vector3 worldPoint = targetCamera.ViewportToWorldPoint(viewportPoint);
        worldPoint.y += yOffset;

        return worldPoint;
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
            {
                continue;
            }

            if (!spawnedLabels.TryGetValue(component.component_id, out GameObject labelObject) || labelObject == null)
            {
                continue;
            }

            bool visible = component.IsLabelVisible();
            labelObject.SetActive(visible);

            if (!visible)
            {
                continue;
            }

            labelObject.transform.position = ComputeWorldPosition(component);

            TextMeshPro tmp = labelObject.GetComponent<TextMeshPro>();
            if (tmp != null)
            {
                tmp.text = BuildLabelText(component);
            }
        }
    }

    private void FaceLabelsTowardCamera()
    {
        if (targetCamera == null)
        {
            return;
        }

        foreach (var kvp in spawnedLabels)
        {
            GameObject labelObject = kvp.Value;
            if (labelObject == null || !labelObject.activeSelf)
            {
                continue;
            }

            Vector3 direction = labelObject.transform.position - targetCamera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                labelObject.transform.forward = direction.normalized;
            }
        }
    }

    private void SetAllLabelsActive(bool active)
    {
        foreach (var kvp in spawnedLabels)
        {
            if (kvp.Value != null)
            {
                kvp.Value.SetActive(active);
            }
        }
    }

    private void ClearSpawnedLabels()
    {
        foreach (var kvp in spawnedLabels)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }

        spawnedLabels.Clear();
    }
}