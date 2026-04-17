using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

public class BoardOverlayCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RawImage boardImage;
    [SerializeField] private RectTransform boxOverlayContainer;
    [SerializeField] private GameObject bboxOverlayPrefab;

    [Header("Visual Root")]
    [SerializeField] private GameObject boardVisualRoot;

    [Header("User Facing")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private bool faceUserOnRelease = true;
    [SerializeField] private bool smoothFaceUser = true;
    [SerializeField] private float faceUserSmoothSpeed = 10f;
    [SerializeField] private bool snapFacingOnStart = true;

    [Header("Grab")]
    [SerializeField] private GrabInteractable grabInteractable;

    [Header("BBox Visuals")]
    [SerializeField] private Color defaultBoxColor = Color.red;
    [SerializeField] private Color hoveredBoxColor = Color.yellow;
    [SerializeField] private Color selectedBoxColor = new Color(1f, 0.55f, 0f, 1f);
    [SerializeField] private float edgeThickness = 2f;

    private readonly Dictionary<string, BBoxVisual> boxesById = new();

    private bool isGrabbed = false;
    private bool shouldSmoothRotateToUser = false;
    private bool wasGrabbedLastFrame = false;

    private string hoveredComponentId;
    private string selectedComponentId;

    private sealed class BBoxVisual
    {
        public RectTransform root;
        public Image top;
        public Image bottom;
        public Image left;
        public Image right;

        public void SetColor(Color color)
        {
            if (top != null) top.color = color;
            if (bottom != null) bottom.color = color;
            if (left != null) left.color = color;
            if (right != null) right.color = color;
        }

        public void SetVisible(bool visible)
        {
            if (root != null)
                root.gameObject.SetActive(visible);
        }
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.2f);

        if (headTransform == null && Camera.main != null)
            headTransform = Camera.main.transform;

        if (boardImage != null)
        {
            Vector3 s = boardImage.rectTransform.localScale;
            boardImage.rectTransform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
        }

        if (boxOverlayContainer != null)
        {
            Vector3 s = boxOverlayContainer.localScale;
            boxOverlayContainer.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
        }

        if (snapFacingOnStart)
            SnapFacingUser();
    }

    private void Update()
    {
        UpdateGrabState();
    }

    private void LateUpdate()
    {
        if (!faceUserOnRelease || !smoothFaceUser || !shouldSmoothRotateToUser || isGrabbed)
            return;

        if (headTransform == null)
            return;

        Quaternion targetRotation = GetYawFacingRotation();

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            faceUserSmoothSpeed * Time.deltaTime
        );

        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        if (angle < 0.5f)
        {
            transform.rotation = targetRotation;
            shouldSmoothRotateToUser = false;
        }
    }

    private void UpdateGrabState()
    {
        if (grabInteractable == null)
            return;

        bool isCurrentlyGrabbed = grabInteractable.State == InteractableState.Select;

        if (isCurrentlyGrabbed && !wasGrabbedLastFrame)
        {
            BeginGrab();
        }
        else if (!isCurrentlyGrabbed && wasGrabbedLastFrame)
        {
            EndGrab();
        }

        wasGrabbedLastFrame = isCurrentlyGrabbed;
    }

    private Quaternion GetYawFacingRotation()
    {
        if (headTransform == null)
            return transform.rotation;

        Vector3 lookDir = headTransform.position - transform.position;
        lookDir = Vector3.ProjectOnPlane(lookDir, Vector3.up);

        if (lookDir.sqrMagnitude < 0.001f)
            return transform.rotation;

        return Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }

    public void SnapFacingUser()
    {
        transform.rotation = GetYawFacingRotation();
        shouldSmoothRotateToUser = false;
    }

    public void BeginGrab()
    {
        isGrabbed = true;
        shouldSmoothRotateToUser = false;
    }

    public void EndGrab()
    {
        isGrabbed = false;

        if (!faceUserOnRelease)
            return;

        if (smoothFaceUser)
        {
            shouldSmoothRotateToUser = true;
        }
        else
        {
            SnapFacingUser();
        }
    }

    public void SetHeadTransform(Transform head)
    {
        headTransform = head;
    }

    public void SetBoardTexture(Texture2D texture)
    {
        if (boardImage != null)
            boardImage.texture = texture;
    }

    public void ClearBoxes()
    {
        if (boxOverlayContainer == null)
            return;

        foreach (Transform child in boxOverlayContainer)
            Destroy(child.gameObject);

        boxesById.Clear();
        hoveredComponentId = null;
        selectedComponentId = null;
    }

    private Image CreateEdge(string name, RectTransform parent, Vector2 anchor, Vector2 size)
    {
        GameObject edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(parent, false);

        RectTransform rt = edge.GetComponent<RectTransform>();
        Image img = edge.GetComponent<Image>();

        img.color = defaultBoxColor;

        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        return img;
    }

    public void BuildBoxes(
        List<ComponentResult> components,
        float imageWidth,
        float imageHeight
    )
    {
        ClearBoxes();

        if (boxOverlayContainer == null)
        {
            Debug.LogError("BuildBoxes aborted: boxOverlayContainer is NULL");
            return;
        }

        if (components == null)
        {
            Debug.LogError("BuildBoxes aborted: components is NULL");
            return;
        }

        float panelWidth = boxOverlayContainer.rect.width;
        float panelHeight = boxOverlayContainer.rect.height;
        Debug.Log($"[BuildBoxes] panelWidth={panelWidth}, panelHeight={panelHeight}, imageWidth={imageWidth}, imageHeight={imageHeight}");

        foreach (var component in components)
        {
            if (component == null)
                continue;

            if (component.bbox == null || component.bbox.Length < 4)
                continue;

            float x = component.bbox[0];
            float y = component.bbox[1];
            float w = component.bbox[2];
            float h = component.bbox[3];

            Debug.Log($"[BuildBoxes] raw bbox for {component.component_id}: x={x}, y={y}, w={w}, h={h}");

            GameObject go = new GameObject($"BBox_{component.component_id}", typeof(RectTransform));
            go.transform.SetParent(boxOverlayContainer, false);
            go.transform.SetAsLastSibling();

            RectTransform rt = go.GetComponent<RectTransform>();

            float uiX = (x / imageWidth) * panelWidth;
            float uiY = (1f - ((y + h) / imageHeight)) * panelHeight;
            float uiW = (w / imageWidth) * panelWidth;
            float uiH = (h / imageHeight) * panelHeight;

            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(uiX, uiY);
            rt.sizeDelta = new Vector2(uiW, uiH);

            BBoxVisual visual = new BBoxVisual
            {
                root = rt,
                top = CreateEdge("Top", rt, new Vector2(0.5f, 1f), new Vector2(uiW, edgeThickness)),
                bottom = CreateEdge("Bottom", rt, new Vector2(0.5f, 0f), new Vector2(uiW, edgeThickness)),
                left = CreateEdge("Left", rt, new Vector2(0f, 0.5f), new Vector2(edgeThickness, uiH)),
                right = CreateEdge("Right", rt, new Vector2(1f, 0.5f), new Vector2(edgeThickness, uiH)),
            };

            visual.SetColor(defaultBoxColor);
            visual.SetVisible(true);

            boxesById[component.component_id] = visual;
        }

        RefreshAllBoxColors();
    }

    public void SetHoveredComponent(string componentId)
    {
        hoveredComponentId = string.IsNullOrWhiteSpace(componentId) ? null : componentId;
        RefreshAllBoxColors();
    }

    public void ClearHoveredComponent(string componentId)
    {
        if (hoveredComponentId == componentId)
        {
            hoveredComponentId = null;
            RefreshAllBoxColors();
        }
    }

    public void SetSelectedComponent(string componentId)
    {
        selectedComponentId = string.IsNullOrWhiteSpace(componentId) ? null : componentId;
        RefreshAllBoxColors();
    }

    public void ClearSelectedComponent(string componentId)
    {
        if (selectedComponentId == componentId)
        {
            selectedComponentId = null;
            RefreshAllBoxColors();
        }
    }

    private void RefreshAllBoxColors()
    {
        foreach (var kvp in boxesById)
        {
            string id = kvp.Key;
            BBoxVisual visual = kvp.Value;

            if (visual == null)
                continue;

            if (id == selectedComponentId)
            {
                visual.SetColor(selectedBoxColor);
            }
            else if (id == hoveredComponentId)
            {
                visual.SetColor(hoveredBoxColor);
            }
            else
            {
                visual.SetColor(defaultBoxColor);
            }
        }
    }

    public void HighlightOnly(string componentId)
    {
        SetSelectedComponent(componentId);
    }

    public void ShowAllBoxes(bool show)
    {
        foreach (var kvp in boxesById)
            kvp.Value?.SetVisible(show);
    }

    public void ClearHighlight()
    {
        hoveredComponentId = null;
        selectedComponentId = null;
        RefreshAllBoxColors();
    }
}