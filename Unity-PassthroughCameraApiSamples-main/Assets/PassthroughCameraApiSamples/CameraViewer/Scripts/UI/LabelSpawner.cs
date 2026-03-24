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

    private void Update()
    {
        RefreshVisibility();

        // Optional: keep labels facing the camera
        FaceLabelsTowardCamera();
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
            if (component == null || string.IsNullOrEmpty(component.component_id))
                continue;

            GameObject labelObject = CreateLabelObject(component);
            spawnedLabels[component.component_id] = labelObject;
        }
    }

    private GameObject CreateLabelObject(ComponentResult component)
    {
        GameObject go = new GameObject($"Label_{component.component_id}");
        go.transform.SetParent(labelParent, false);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = GetLabelText(component);
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;

        go.transform.localScale = labelScale;
        go.transform.position = ComputeWorldPosition(component);

        return go;
    }

    private string GetLabelText(ComponentResult component)
    {
        if (component.label != null && !string.IsNullOrEmpty(component.label.title))
        {
            return component.label.title;
        }

        return component.type;
    }

    private Vector3 ComputeWorldPosition(ComponentResult component)
    {
        if (targetCamera == null || component.bbox == null || component.bbox.Length < 4)
        {
            return transform.position;
        }

        int x = component.bbox[0];
        int y = component.bbox[1];
        int w = component.bbox[2];
        int h = component.bbox[3];

        float centerX = x + (w * 0.5f);
        float topY = y;

        float screenX = centerX;
        float screenY = Screen.height - topY;

        Vector3 screenPoint = new Vector3(screenX, screenY, zOffset);
        Vector3 worldPoint = targetCamera.ScreenToWorldPoint(screenPoint);
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
        if (session.components == null)
            return;

        foreach (var component in session.components)
        {
            if (component == null || string.IsNullOrEmpty(component.component_id))
                continue;

            if (!spawnedLabels.TryGetValue(component.component_id, out GameObject labelObject) || labelObject == null)
                continue;

            bool visible = component.label != null && component.label.visible;
            labelObject.SetActive(visible);

            if (visible)
            {
                labelObject.transform.position = ComputeWorldPosition(component);

                TextMeshPro tmp = labelObject.GetComponent<TextMeshPro>();
                if (tmp != null)
                {
                    tmp.text = GetLabelText(component);
                }
            }
        }
    }

    private void FaceLabelsTowardCamera()
    {
        if (targetCamera == null)
            return;

        foreach (var kvp in spawnedLabels)
        {
            GameObject labelObject = kvp.Value;
            if (labelObject == null || !labelObject.activeSelf)
                continue;

            labelObject.transform.forward = labelObject.transform.position - targetCamera.transform.position;
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