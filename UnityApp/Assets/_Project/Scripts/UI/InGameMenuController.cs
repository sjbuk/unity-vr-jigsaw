using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Handles the in-game pause menu triggered by the left controller's menu button.
/// Shows a world-space canvas with Return to Main Menu and Cancel options.
/// Uses the same direct raycast click pattern as MenuManager.
/// </summary>
public class InGameMenuController : MonoBehaviour
{
    public static bool IsMenuActive { get; private set; }

    [Header("Layout")]
    public float menuForwardDistance = 1.5f;
    public float menuHeight = 0f;

    [Header("Hover")]
    public Color hoverHighlightColor = new Color(0.2f, 0.7f, 1f, 1f);
    public float hoverFadeSpeed = 8f;

    private bool menuVisible;
    private InputActionAsset inputActions;
    private InputActionMap jigsawMap;
    private InputAction menuButtonAction;
    private InputAction menuClickAction;

    private Transform menuPanel;
    private Transform leftController;
    private Camera mainCamera;
    private Coroutine menuLoop;

    private Button hoveredButton;
    private Image hoveredButtonImage;
    private Color hoveredOriginalColor;
    private Color hoverCurrentColor;

    void Start()
    {
        FindCamera();
        FindLeftController();
        LoadInputActions();
        CreateMenuClickAction();
        CreateMenuPanel();
    }

    void FindCamera()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
                mainCamera = xrOrigin.Camera;
        }
    }

    void LoadInputActions()
    {
        var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
        if (jsonAsset == null)
        {
            Debug.LogError("[InGameMenu] XRI_Jigsaw.json not found in Resources!");
            return;
        }
        inputActions = InputActionAsset.FromJson(jsonAsset.text);
        jigsawMap = inputActions.FindActionMap("Jigsaw");
        if (jigsawMap == null)
        {
            Debug.LogError("[InGameMenu] Jigsaw action map not found!");
            return;
        }

        menuButtonAction = jigsawMap.FindAction("LeftMenuButton");
        if (menuButtonAction != null)
        {
            Debug.Log($"[InGameMenu] LeftMenuButton action found, bindings: {menuButtonAction.bindings.Count}");
            menuButtonAction.performed += OnMenuButtonPressed;
            jigsawMap.Enable();
        }
        else
        {
            Debug.LogError("[InGameMenu] LeftMenuButton action NOT found in Jigsaw map! Available actions: " + string.Join(", ", System.Linq.Enumerable.Select(jigsawMap.actions, a => a.name)));
        }
    }

    void CreateMenuClickAction()
    {
        menuClickAction = new InputAction("MenuClick", InputActionType.Button);
        menuClickAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
        menuClickAction.Enable();
    }

    void FindLeftController()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t.name == "Left Controller")
            {
                leftController = t;
                return;
            }
        }
    }

    void CreateMenuPanel()
    {
        var go = new GameObject("InGameMenuPanel");
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (mainCamera != null)
            canvas.worldCamera = mainCamera;

        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        var bgGo = new GameObject("Background", typeof(Image));
        bgGo.transform.SetParent(go.transform, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.sizeDelta = new Vector2(1.8f, 1.0f);
        var bgImg = bgGo.GetComponent<Image>();
        bgImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

        var titleGo = new GameObject("Title", typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(go.transform, false);
        var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
        titleTmp.text = "Paused";
        titleTmp.fontSize = 0.07f;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;
        titleTmp.fontStyle = FontStyles.Bold;
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0f, 0.15f);
        titleRt.sizeDelta = new Vector2(0.6f, 0.08f);

        CreateButton(go.transform, "Return to Main Menu", new Vector2(0f, 0.03f), () =>
        {
            Time.timeScale = 1f;
            IsMenuActive = false;
            ShowPuzzlePieces();
            SceneManager.LoadScene("MainMenu");
        });

        CreateButton(go.transform, "Cancel", new Vector2(0f, -0.13f), HideMenu);

        menuPanel = go.transform;
    }

    Button CreateButton(Transform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(0.5f, 0.09f);

        var img = btnGo.GetComponent<Image>();
        img.color = new Color(0.25f, 0.35f, 0.75f, 0.85f);

        var btn = btnGo.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(btnGo.transform, false);
        var txtRt = txtGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        var txtTmp = txtGo.GetComponent<TextMeshProUGUI>();
        txtTmp.text = label;
        txtTmp.fontSize = 0.035f;
        txtTmp.alignment = TextAlignmentOptions.Center;
        txtTmp.color = Color.white;

        return btn;
    }

    void OnMenuButtonPressed(InputAction.CallbackContext ctx)
    {
        ToggleMenu();
    }

    void ToggleMenu()
    {
        if (menuVisible)
            HideMenu();
        else
            ShowMenu();
    }

    void ShowMenu()
    {
        menuVisible = true;
        IsMenuActive = true;
        Time.timeScale = 0f;

        if (leftController == null) FindLeftController();
        if (mainCamera == null) FindCamera();

        HidePuzzlePieces();
        PositionPanel();

        if (menuPanel != null)
            menuPanel.gameObject.SetActive(true);

        if (menuLoop != null) StopCoroutine(menuLoop);
        menuLoop = StartCoroutine(MenuUpdateLoop());
    }

    void HideMenu()
    {
        menuVisible = false;
        IsMenuActive = false;
        Time.timeScale = 1f;

        if (menuLoop != null) { StopCoroutine(menuLoop); menuLoop = null; }

        if (menuPanel != null)
            menuPanel.gameObject.SetActive(false);

        ShowPuzzlePieces();
        ClearHover();
    }

    void HidePuzzlePieces()
    {
        var puzzleRoot = GameObject.Find("PuzzleRoot");
        if (puzzleRoot != null)
        {
            foreach (var mr in puzzleRoot.GetComponentsInChildren<MeshRenderer>())
                mr.enabled = false;
        }
    }

    void ShowPuzzlePieces()
    {
        var puzzleRoot = GameObject.Find("PuzzleRoot");
        if (puzzleRoot != null)
        {
            foreach (var mr in puzzleRoot.GetComponentsInChildren<MeshRenderer>())
                mr.enabled = true;
        }
    }

    void PositionPanel()
    {
        if (menuPanel == null) return;

        if (mainCamera != null)
        {
            var pos = mainCamera.transform.position;
            var fwd = mainCamera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            menuPanel.position = pos + fwd * menuForwardDistance + Vector3.up * menuHeight;
            menuPanel.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
        else if (leftController != null)
        {
            menuPanel.position = leftController.position + leftController.forward * menuForwardDistance;
            menuPanel.rotation = Quaternion.LookRotation(leftController.forward, Vector3.up);
        }
    }

    void ClearHover()
    {
        if (hoveredButtonImage != null)
        {
            hoveredButtonImage.color = hoveredOriginalColor;
            hoveredButtonImage = null;
        }
        hoveredButton = null;
    }

    IEnumerator MenuUpdateLoop()
    {
        while (menuVisible)
        {
            if (leftController == null) FindLeftController();
            if (mainCamera == null) FindCamera();
            UpdateHover();
            HandleMenuClick();
            yield return new WaitForSecondsRealtime(0.02f);
        }
    }

    void UpdateHover()
    {
        if (leftController == null || menuPanel == null) return;

        var origin = leftController.position;
        var forward = leftController.forward;
        var newHover = FindClosestButtonUnderRay(origin, forward);

        if (newHover != hoveredButton)
        {
            if (hoveredButton != null && hoveredButtonImage != null)
            {
                hoveredButtonImage.color = hoveredOriginalColor;
                hoveredButtonImage = null;
            }
            hoveredButton = newHover;
            hoverCurrentColor = hoveredOriginalColor;

            if (hoveredButton != null)
            {
                hoveredButtonImage = hoveredButton.GetComponent<Image>();
                if (hoveredButtonImage != null)
                    hoveredOriginalColor = hoveredButtonImage.color;
            }
        }

        if (hoveredButton != null && hoveredButtonImage != null)
        {
            float fade = hoverFadeSpeed * Time.unscaledDeltaTime;
            hoverCurrentColor = Color.Lerp(hoverCurrentColor, hoverHighlightColor, Mathf.Clamp01(fade));
            hoveredButtonImage.color = hoverCurrentColor;
        }
    }

    void HandleMenuClick()
    {
        if (leftController == null || menuClickAction == null) return;
        if (!menuClickAction.WasPressedThisFrame()) return;

        var origin = leftController.position;
        var forward = leftController.forward;
        var hitButton = FindClosestButtonUnderRay(origin, forward);
        if (hitButton != null)
        {
            ClearHover();
            hitButton.onClick.Invoke();
        }
    }

    Button FindClosestButtonUnderRay(Vector3 origin, Vector3 direction)
    {
        if (menuPanel == null) return null;

        Button closest = null;
        float closestDist = float.MaxValue;

        foreach (var btn in menuPanel.GetComponentsInChildren<Button>(true))
        {
            var btnPos = btn.transform.position;
            var toBtn = btnPos - origin;
            var projLength = Vector3.Dot(toBtn, direction);
            if (projLength <= 0f) continue;

            var projPoint = origin + direction * projLength;
            var dist = Vector3.Distance(btnPos, projPoint);

            var rect = btn.GetComponent<RectTransform>();
            var halfSize = (rect != null) ? Mathf.Max(rect.rect.width, rect.rect.height) * rect.lossyScale.x * 0.5f : 0.15f;

            if (dist < halfSize && projLength < closestDist)
            {
                closestDist = projLength;
                closest = btn;
            }
        }

        return closest;
    }

    void OnEnable()
    {
        jigsawMap?.Enable();
        menuClickAction?.Enable();
    }

    void OnDisable()
    {
        jigsawMap?.Disable();
        menuClickAction?.Disable();
        if (menuVisible) HideMenu();
    }

    void OnDestroy()
    {
        if (menuButtonAction != null)
            menuButtonAction.performed -= OnMenuButtonPressed;
        if (menuClickAction != null)
        {
            menuClickAction.Disable();
            menuClickAction.Dispose();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && menuVisible)
            HideMenu();
    }
}
