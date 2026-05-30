using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using TMPro;

/// <summary>
/// Manages the main menu scene: discovers puzzle folders, creates interactive cards for each,
/// and handles puzzle start/reset actions. Also performs runtime XR UI interaction setup.
///
/// === XR UI INTERACTION ARCHITECTURE ===
///
/// The standard XRI Toolkit pipeline (NearFarInteractor → TrackedDeviceGraphicRaycaster →
/// InputSystemUIInputModule) was NOT working for the MainMenu scene due to multiple issues:
///
///   1. NO UI Canvas in the saved scene. MenuManager creates one dynamically ("Menu Panels Container")
///      but originally only added a bare Canvas component — missing CanvasScaler, GraphicRaycaster,
///      and TrackedDeviceGraphicRaycaster.
///
///   2. WRONG TYPE NAME in the original TrackedDeviceRaycaster reflection code. The actual XRI class
///      is called "TrackedDeviceGraphicRaycaster" (not "TrackedDeviceRaycaster"). The typo in both
///      JigsawSceneSetup.cs:718 and the original MenuManager fallback code meant the raycaster was
///      never added to the canvas.
///
///   3. BROKEN GUID on InputSystemUIInputModule actionsAsset. The MainMenu scene references an
///      InputActionAsset with GUID ca9f5fa95ffab41fb9a615ab714db018 that does not match any
///      existing .inputactions file. Individual action references (PointAction, TrackedDevicePosition,
///      etc.) all point to this missing asset, resolving to null at runtime.
///
///   4. XRI UI MAP lacks tracked-device actions. The "XRI UI" map in XRI Default Input Actions
///      has Point/Click/Navigate etc. but NOT TrackedDevicePosition/TrackedDeviceOrientation.
///      The standard InputSystem_Actions.inputactions "UI" map DOES have these.
///
///   5. WorldSpace Canvas GraphicRaycaster.Raycast() uses screen-space hit testing which does not
///      work for 3D VR WorldSpace canvases. The TrackedDeviceGraphicRaycaster is the correct
///      raycaster but requires a working InputSystemUIInputModule pipeline.
///
///   6. Canvas positioned at y=0 but XR camera at y≈1.36. Cards parented to canvas but positioned
///      at camera height → outside canvas RectTransform bounds → unreachable.
///
/// === FIXES APPLIED (all in MenuManager.Start) ===
///
/// SetupContainer():
///   - Creates "Menu Panels Container" Canvas in WorldSpace mode
///   - Adds CanvasScaler, GraphicRaycaster, TrackedDeviceGraphicRaycaster (via assembly search)
///   - Sets canvas.worldCamera to the XR camera
///
/// SetupXRTrackingOrigin():
///   - Sets InputSystemUIInputModule.xrTrackingOrigin to XR Origin transform
///   - FixInputSystemActions: loads InputSystem_Actions asset, repairs all broken action references
///     (PointAction, TrackedDevicePositionAction, etc.)
///
/// SetupCanvasWorldCamera():
///   - Positions the canvas at the XR camera's y-height so cards fall within canvas bounds
///
/// SetupMenuUIControllers():
///   - Disables LaserPointer, PieceHolder, PokeInteractor, TeleportInteractor (not needed in menu)
///   - Hides LaserVisual child (stale puzzle laser)
///   - Configures NearFarInteractor: enableNearCasting=false, enableFarCasting=true, enableUIInteraction=true
///   - Configures CurveVisualController: extendLineToEmptyHit=true, restingVisualLineLength=3m
///     → provides the visible white laser from each controller
///   - Enables "XRI Left Interaction" and "XRI Right Interaction" action maps via reflection
///     on ControllerInputActionManager
///
/// HandleDirectUIClick() [called every frame from Update]:
///   - UpdateHover: finds button under active controller ray, lerps its Image.color
///     to hoverHighlightColor. Restores original on exit.
///   - HandleDirectUIClick: reads UI Press action from the active controller only
///   - On trigger press, projects a 3D ray from the controller transform
///   - FindClosestButtonUnderRay: iterates all Button children of the canvas, computes
///     perpendicular distance from each button to the ray, selects the closest within
///     the button's half-size threshold
///   - Invokes button.onClick directly — bypasses the XRI UI event pipeline entirely
///     because the standard pipeline relies on working TrackedDevicePosition/Orientation
///     actions and the NearFarInteractor ↔ InputSystemUIInputModule bridge, which had
///     too many failure points
///
/// === PUBLIC PROPERTIES ===
///   - activeController: which controller (Left/Right) drives the menu ray and clicks
///   - hoverHighlightColor: color applied when ray points at a button
///   - hoverFadeSpeed: how fast the hover color fades in/out
///
/// === KNOWN LIMITATIONS ===
///   - The direct click handler does not support UI scroll
///   - It finds buttons by 3D proximity, not by Unity UI raycast ordering
/// </summary>
public class MenuManager : MonoBehaviour
{
    public enum ActiveController { Left, Right }

    [Header("Puzzle Setup")]
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;
    public float cardSpacing = 0.55f;
    public float cardWorldScale = 1f;
    public float menuHeight = 0f;
    public float menuForwardDistance = 1.5f;

    [Header("Input")]
    [Tooltip("Which controller drives the menu ray and clicks.")]
    public ActiveController activeController = ActiveController.Left;

    [Header("Hover")]
    [Tooltip("Color applied when the ray points at a button.")]
    public Color hoverHighlightColor = new Color(0.2f, 0.7f, 1f, 1f);
    [Tooltip("How fast the highlight color fades in (seconds).")]
    public float hoverFadeSpeed = 6f;

    [Header("Network")]
    [Tooltip("Base URL for the puzzle API server.")]
    public string apiUrl = "http://10.111.1.20:8000";

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;
    private Transform workingContainer;
    private TMP_Text titleText;
    private TMP_InputField apiUrlInput;
    private bool isLoadingRemote;

    private Vector3 cachedCameraPos;
    private Vector3 cachedCameraForward;
    private Vector3 cachedCameraRight;

    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;

    private Button hoveredButton;
    private Image hoveredButtonImage;
    private Color hoveredOriginalColor;
    private Color hoverCurrentColor;

    void Start()
    {
        var placeholder = GameObject.Find("Placeholder Text");
        if (placeholder != null) placeholder.SetActive(false);

#if UNITY_ANDROID && !UNITY_EDITOR
        puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
#else
        puzzlesPath = Path.Combine(Application.dataPath, "_Project", "Puzzels");
#endif
        Directory.CreateDirectory(puzzlesPath);

        string persistedUrl = PlayerPrefs.GetString("PuzzleApiUrl", "");
        if (!string.IsNullOrEmpty(persistedUrl))
            apiUrl = persistedUrl;
        PuzzleApiClient.BaseUrl = apiUrl;

        var cam = Camera.main;
        if (cam != null)
        {
            cachedCameraPos = cam.transform.position;
            cachedCameraForward = cam.transform.forward;
            cachedCameraRight = cam.transform.right;
        }

        SetupContainer();
        SetupXRTrackingOrigin();
        SetupMenuUIControllers();
        SetupCanvasWorldCamera();
        CreateTitle();
        CreateApiUrlPanel();
        _ = DiscoverPuzzlesAsync();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
            && discoveredPuzzles != null && discoveredPuzzles.Count > 0)
        {
            OnStartPuzzle(discoveredPuzzles[0], false);
        }

        UpdateHover();
        HandleDirectUIClick();
    }

    void SetupContainer()
    {
        var existingCanvas = GameObject.Find("UI Canvas");
        if (existingCanvas != null)
        {
            workingContainer = existingCanvas.transform;
            return;
        }

        var containerGO = new GameObject("Menu Panels Container");
        containerGO.transform.SetParent(transform, false);
        containerGO.transform.localPosition = new Vector3(0, menuHeight, menuForwardDistance);
        containerGO.transform.localRotation = Quaternion.identity;

        var canvas = containerGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var xrCamera = Camera.main;
        if (xrCamera == null)
        {
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
                xrCamera = xrOrigin.Camera;
        }
        if (xrCamera != null)
            canvas.worldCamera = xrCamera;

        containerGO.AddComponent<CanvasScaler>();
        containerGO.AddComponent<GraphicRaycaster>();

        var tdrType = FindTrackedDeviceRaycasterType();
        if (tdrType != null)
            containerGO.AddComponent(tdrType);

        workingContainer = containerGO.transform;
    }

    void SetupXRTrackingOrigin()
    {
        var eventSystem = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null) return;
        var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null) return;
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
            inputModule.xrTrackingOrigin = xrOrigin.transform;

        FixInputSystemActions(inputModule);
    }

    void FixInputSystemActions(InputSystemUIInputModule inputModule)
    {
        InputActionAsset standardAsset = null;

        foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
        {
            if (asset.name == "InputSystem_Actions")
            {
                standardAsset = asset;
                break;
            }
        }

        if (standardAsset != null)
        {
            inputModule.actionsAsset = standardAsset;
            var uiMap = standardAsset.FindActionMap("UI");
            if (uiMap != null)
            {
                SetActionRef(inputModule, "m_PointAction", uiMap.FindAction("Point"));
                SetActionRef(inputModule, "m_MoveAction", uiMap.FindAction("Navigate"));
                SetActionRef(inputModule, "m_SubmitAction", uiMap.FindAction("Submit"));
                SetActionRef(inputModule, "m_CancelAction", uiMap.FindAction("Cancel"));
                SetActionRef(inputModule, "m_LeftClickAction", uiMap.FindAction("Click"));
                SetActionRef(inputModule, "m_ScrollWheelAction", uiMap.FindAction("ScrollWheel"));
                SetActionRef(inputModule, "m_MiddleClickAction", uiMap.FindAction("MiddleClick"));
                SetActionRef(inputModule, "m_RightClickAction", uiMap.FindAction("RightClick"));
                SetActionRef(inputModule, "m_TrackedDevicePositionAction", uiMap.FindAction("TrackedDevicePosition"));
                SetActionRef(inputModule, "m_TrackedDeviceOrientationAction", uiMap.FindAction("TrackedDeviceOrientation"));
            }
        }
    }

    void SetActionRef(InputSystemUIInputModule module, string fieldName, InputAction action)
    {
        if (action == null) return;
        var field = typeof(InputSystemUIInputModule).GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (field == null)
        {
            field = typeof(InputSystemUIInputModule).BaseType?.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        }
        if (field != null)
            field.SetValue(module, InputActionReference.Create(action));
    }

    void SetupMenuUIControllers()
    {
        EnableXRInteractionMaps();

        string activeName = activeController == ActiveController.Left ? "Left Controller" : "Right Controller";
        string inactiveName = activeController == ActiveController.Left ? "Right Controller" : "Left Controller";

        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t.name != "Left Controller" && t.name != "Right Controller")
                continue;

            bool isActive = t.name == activeName;

            foreach (var lp in t.GetComponentsInChildren<LaserPointer>(true))
                lp.enabled = false;

            foreach (var ph in t.GetComponentsInChildren<PieceHolder>(true))
                ph.enabled = false;

            var laserVisual = t.Find("LaserVisual");
            if (laserVisual != null) laserVisual.gameObject.SetActive(false);

            foreach (var nf in t.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nf.enableNearCasting = false;
                nf.enableFarCasting = isActive;
                nf.enableUIInteraction = isActive;
            }

            foreach (var poke in t.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>(true))
                poke.enabled = false;

            var teleport = t.Find("Teleport Interactor");
            if (teleport != null)
            {
                foreach (var rr in teleport.GetComponents<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>())
                    rr.enabled = false;
            }

            foreach (var visual in t.GetComponentsInChildren<CurveVisualController>(true))
            {
                visual.extendLineToEmptyHit = isActive;
                visual.restingVisualLineLength = isActive ? 3f : 0f;
            }
        }
    }

    void EnableXRInteractionMaps()
    {
        foreach (var manager in FindObjectsOfType<ControllerInputActionManager>())
        {
            var field = manager.GetType().GetField("m_UIScroll",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) continue;

            var uiScrollRef = field.GetValue(manager) as InputActionReference;
            if (uiScrollRef == null) continue;

            var action = uiScrollRef.action;
            var asset = action?.actionMap?.asset;
            if (asset == null) continue;

            asset.FindActionMap("XRI Left Interaction")?.Enable();
            asset.FindActionMap("XRI Right Interaction")?.Enable();
            return;
        }
    }

    void SetupCanvasWorldCamera()
    {
        if (workingContainer == null) return;
        var canvas = workingContainer.GetComponent<Canvas>();
        if (canvas == null) return;

        var xrCamera = Camera.main;
        if (xrCamera == null)
        {
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
                xrCamera = xrOrigin.Camera;
        }
        if (xrCamera != null)
        {
            canvas.worldCamera = xrCamera;
            var pos = workingContainer.position;
            pos.y = xrCamera.transform.position.y;
            workingContainer.position = pos;
        }
    }

    void UpdateHover()
    {
        if (workingContainer == null) return;
        var activeTransform = GetActiveControllerTransform();
        if (activeTransform == null) return;

        var origin = activeTransform.position;
        var forward = activeTransform.forward;
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
            hoverCurrentColor = Color.Lerp(hoverCurrentColor, hoverHighlightColor, Time.deltaTime * hoverFadeSpeed);
            hoveredButtonImage.color = hoverCurrentColor;
        }
    }

    void HandleDirectUIClick()
    {
        if (workingContainer == null) return;

        if (leftTriggerAction == null)
        {
            var xriAsset = FindXRIInputAsset();
            if (xriAsset != null)
            {
                var leftMap = xriAsset.FindActionMap("XRI Left Interaction");
                var rightMap = xriAsset.FindActionMap("XRI Right Interaction");
                leftTriggerAction = leftMap?.FindAction("UI Press");
                rightTriggerAction = rightMap?.FindAction("UI Press");
                if (leftTriggerAction != null) leftTriggerAction.Enable();
                if (rightTriggerAction != null) rightTriggerAction.Enable();
            }
        }

        var activeTransform = GetActiveControllerTransform();
        if (activeTransform == null) return;

        var triggerAction = activeTransform.name == "Left Controller" ? leftTriggerAction : rightTriggerAction;
        if (triggerAction == null) return;
        if (!triggerAction.WasPressedThisFrame()) return;

        var origin = activeTransform.position;
        var forward = activeTransform.forward;
        var hitButton = FindClosestButtonUnderRay(origin, forward);
        if (hitButton != null)
        {
            if (hoveredButtonImage != null)
            {
                hoveredButtonImage.color = hoveredOriginalColor;
                hoveredButtonImage = null;
            }
            hitButton.onClick.Invoke();
        }
    }

    Transform GetActiveControllerTransform()
    {
        string targetName = activeController == ActiveController.Left ? "Left Controller" : "Right Controller";
        foreach (var t in FindObjectsOfType<Transform>())
            if (t.name == targetName)
                return t;
        return null;
    }

    Button FindClosestButtonUnderRay(Vector3 origin, Vector3 direction)
    {
        Button closest = null;
        float closestDist = float.MaxValue;

        foreach (var btn in workingContainer.GetComponentsInChildren<Button>(true))
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

    InputActionAsset FindXRIInputAsset()
    {
        foreach (var manager in FindObjectsOfType<ControllerInputActionManager>())
        {
            var field = manager.GetType().GetField("m_UIScroll",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) continue;
            var uiScrollRef = field.GetValue(manager) as InputActionReference;
            return uiScrollRef?.action?.actionMap?.asset;
        }
        return null;
    }

    System.Type FindTrackedDeviceRaycasterType()
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster");
            if (t != null) return t;
        }
        return null;
    }

    void CreateTitle()
    {
        if (workingContainer == null) return;

        var existing = workingContainer.Find("Title");
        if (existing != null)
        {
            titleText = existing.GetComponent<TMP_Text>();
            return;
        }

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(workingContainer, false);

        var rt = titleGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2f, 0.15f);

        titleGO.transform.position = cachedCameraPos
            + cachedCameraForward * menuForwardDistance
            + Vector3.up * (menuHeight + 0.45f);

        titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = "Jigsaw VR";
        titleText.fontSize = 0.12f;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;

        if (titleText.font == null)
            titleText.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    void CreateApiUrlPanel()
    {
        if (workingContainer == null) return;

        var existing = workingContainer.Find("ApiUrlPanel");
        if (existing != null)
        {
            apiUrlInput = existing.GetComponentInChildren<TMP_InputField>();
            if (apiUrlInput != null)
                apiUrlInput.text = apiUrl;
            return;
        }

        var panelGO = new GameObject("ApiUrlPanel", typeof(RectTransform));
        panelGO.transform.SetParent(workingContainer, false);

        var rt = panelGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2.5f, 0.12f);

        panelGO.transform.position = cachedCameraPos
            + cachedCameraForward * menuForwardDistance
            + Vector3.up * (menuHeight + 0.28f);

        var inputGO = new GameObject("ApiUrlInput", typeof(RectTransform));
        inputGO.transform.SetParent(panelGO.transform, false);
        var inputRt = inputGO.GetComponent<RectTransform>();
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(0.7f, 1);
        inputRt.sizeDelta = Vector2.zero;
        inputRt.anchoredPosition = Vector2.zero;

        var bg = inputGO.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

        apiUrlInput = inputGO.AddComponent<TMP_InputField>();
        apiUrlInput.text = apiUrl;
        apiUrlInput.targetGraphic = bg;

        var placeholder = new GameObject("Placeholder", typeof(RectTransform));
        placeholder.transform.SetParent(inputGO.transform, false);
        var phRt = placeholder.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.sizeDelta = Vector2.zero;
        var phText = placeholder.AddComponent<TextMeshProUGUI>();
        phText.text = "API URL...";
        phText.fontSize = 0.025f;
        phText.color = new Color(0.5f, 0.5f, 0.6f, 1f);
        phText.alignment = TextAlignmentOptions.Left;
        phText.fontStyle = FontStyles.Italic;
        if (phText.font == null)
            phText.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        apiUrlInput.placeholder = phText;

        var textArea = inputGO.transform.Find("Text Area");
        if (textArea != null)
        {
            var textComp = textArea.GetComponentInChildren<TMP_Text>();
            if (textComp != null)
            {
                textComp.fontSize = 0.025f;
                textComp.alignment = TextAlignmentOptions.Left;
                textComp.color = Color.white;
                if (textComp.font == null)
                    textComp.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            }
        }

        var refreshGO = new GameObject("RefreshButton", typeof(RectTransform));
        refreshGO.transform.SetParent(panelGO.transform, false);
        var refreshRt = refreshGO.GetComponent<RectTransform>();
        refreshRt.anchorMin = new Vector2(0.72f, 0.1f);
        refreshRt.anchorMax = new Vector2(0.98f, 0.9f);
        refreshRt.sizeDelta = Vector2.zero;

        var refreshImg = refreshGO.AddComponent<Image>();
        refreshImg.color = new Color(0.2f, 0.5f, 0.8f, 1f);

        var refreshBtn = refreshGO.AddComponent<Button>();
        refreshBtn.targetGraphic = refreshImg;

        var refreshLabelGO = new GameObject("Text", typeof(RectTransform));
        refreshLabelGO.transform.SetParent(refreshGO.transform, false);
        var refreshLabelRt = refreshLabelGO.GetComponent<RectTransform>();
        refreshLabelRt.anchorMin = Vector2.zero;
        refreshLabelRt.anchorMax = Vector2.one;
        refreshLabelRt.sizeDelta = Vector2.zero;
        var refreshLabel = refreshLabelGO.AddComponent<TextMeshProUGUI>();
        refreshLabel.text = "Refresh";
        refreshLabel.fontSize = 0.025f;
        refreshLabel.alignment = TextAlignmentOptions.Center;
        refreshLabel.color = Color.white;
        refreshLabel.fontStyle = FontStyles.Bold;
        if (refreshLabel.font == null)
            refreshLabel.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        refreshBtn.onClick.AddListener(() =>
        {
            apiUrl = apiUrlInput.text.Trim();
            PuzzleApiClient.BaseUrl = apiUrl;
            _ = RefreshPuzzleList();
        });
    }

    async Task DiscoverPuzzlesAsync()
    {
        discoveredPuzzles = new List<PuzzleInfo>();

        HashSet<string> localSourceModels = new HashSet<string>();
        HashSet<string> localFolderNames = new HashSet<string>();

        if (Directory.Exists(puzzlesPath))
        {
            var dirs = Directory.GetDirectories(puzzlesPath);

            foreach (var dir in dirs)
            {
                string folderName = Path.GetFileName(dir);
                localFolderNames.Add(folderName);

                string checkpoint = Path.Combine(dir, "checkpoint.json");
                if (!File.Exists(checkpoint)) continue;

                var json = File.ReadAllText(checkpoint);
                var data = JsonUtility.FromJson<CheckpointData>(json);
                if (data == null) continue;

                if (!string.IsNullOrEmpty(data.source))
                    localSourceModels.Add(data.source);

                string thumbnail = Path.Combine(dir, "preview.png");
                string save = Path.Combine(dir, "save.json");
                float progress = 0f;
                bool hasSave = false;

                if (File.Exists(save))
                {
                    try
                    {
                        var saveData = JsonUtility.FromJson<SaveManager.SaveData>(File.ReadAllText(save));
                        if (saveData != null)
                        {
                            progress = saveData.completionPercent;
                            hasSave = true;
                        }
                    }
                    catch { }
                }

                discoveredPuzzles.Add(new PuzzleInfo
                {
                    folderPath = dir,
                    name = folderName,
                    displayName = string.IsNullOrEmpty(data.name) ? folderName : data.name,
                    pieceCount = data.piece_count,
                    thumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                    progress = progress,
                    hasSave = hasSave,
                    isRemote = false,
                    isDownloaded = true,
                    sourceModel = data.source
                });
            }
        }

        isLoadingRemote = true;
        ArrangePanels();

        var jobs = await PuzzleApiClient.ListJobs();
        if (this == null) return;

        foreach (var job in jobs)
        {
            if (string.IsNullOrEmpty(job.job_id)) continue;

            bool alreadyLocal = localFolderNames.Contains(job.job_id)
                || localSourceModels.Contains(job.source_model);

            if (alreadyLocal)
            {
                foreach (var existing in discoveredPuzzles)
                {
                    if (existing.sourceModel == job.source_model
                        || existing.name == job.job_id)
                    {
                        if (string.IsNullOrEmpty(existing.displayName)
                            || existing.displayName == existing.name)
                        {
                            existing.displayName = job.name;
                        }
                        break;
                    }
                }
                continue;
            }

            discoveredPuzzles.Add(new PuzzleInfo
            {
                folderPath = Path.Combine(puzzlesPath, job.job_id),
                name = job.job_id,
                displayName = string.IsNullOrEmpty(job.name) ? job.job_id : job.name,
                pieceCount = job.piece_count,
                thumbnailPath = null,
                progress = 0f,
                hasSave = false,
                isRemote = true,
                isDownloaded = false,
                remoteJobId = job.job_id,
                sourceModel = job.source_model
            });
        }

        isLoadingRemote = false;
        ArrangePanels();
    }

    void ArrangePanels()
    {
        foreach (Transform child in workingContainer)
        {
            if (child.GetComponent<PuzzleCard>() != null)
                Destroy(child.gameObject);
        }

        int count = discoveredPuzzles.Count;

        if (count == 0)
        {
            if (titleText != null)
            {
                titleText.text = isLoadingRemote ? "Loading puzzles..." : "No Puzzles Found";
            }
            return;
        }

        if (puzzleCardPrefab == null)
        {
            Debug.LogError("[MenuManager] puzzleCardPrefab not assigned!");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, workingContainer);

            float totalWidth = (count - 1) * cardSpacing;
            float offsetX = -totalWidth * 0.5f + i * cardSpacing;

            Vector3 worldCenter = cachedCameraPos
                + cachedCameraForward * menuForwardDistance
                + Vector3.up * menuHeight
                + cachedCameraRight * offsetX;

            card.transform.position = worldCenter;
            card.transform.localScale = new Vector3(cardWorldScale, cardWorldScale, cardWorldScale);
            card.transform.rotation = Quaternion.LookRotation(cachedCameraForward, Vector3.up);

            var puzzleCard = card.GetComponent<PuzzleCard>();
            if (puzzleCard != null)
                puzzleCard.Initialize(discoveredPuzzles[i], this);
        }
    }

    public void OnStartPuzzle(PuzzleInfo puzzle, bool resume)
    {
        if (puzzle.isRemote && !puzzle.isDownloaded)
        {
            _ = DownloadAndStartPuzzle(puzzle);
            return;
        }

        PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
        PuzzleManager.LoadOnStart = resume ? PuzzleManager.LoadMode.Resume : PuzzleManager.LoadMode.NewGame;
        SceneManager.LoadScene("PuzzleScene");
    }

    public async Task DownloadAndStartPuzzle(PuzzleInfo puzzle)
    {
        if (!puzzle.isRemote || puzzle.isDownloaded)
        {
            OnStartPuzzle(puzzle, false);
            return;
        }

        puzzle.folderPath = Path.Combine(puzzlesPath, puzzle.remoteJobId);
        Directory.CreateDirectory(puzzle.folderPath);

        PuzzleCard targetCard = null;
        foreach (var card in workingContainer.GetComponentsInChildren<PuzzleCard>())
        {
            if (card.PuzzleInfo == puzzle)
            {
                targetCard = card;
                break;
            }
        }

        if (targetCard != null)
            targetCard.UpdateDownloadProgress(0.01f);

        bool success = await PuzzleApiClient.DownloadPuzzle(
            puzzle.remoteJobId, puzzle.folderPath,
            progress =>
            {
                if (targetCard != null)
                    targetCard.UpdateDownloadProgress(progress);
            });

        if (success)
        {
            puzzle.isDownloaded = true;
            string checkpointPath = Path.Combine(puzzle.folderPath, "checkpoint.json");
            if (File.Exists(checkpointPath))
            {
                var data = JsonUtility.FromJson<CheckpointData>(File.ReadAllText(checkpointPath));
                if (data != null && !string.IsNullOrEmpty(data.name))
                    puzzle.displayName = data.name;
            }

            string thumbnailPath = Path.Combine(puzzle.folderPath, "preview.png");
            if (File.Exists(thumbnailPath))
                puzzle.thumbnailPath = thumbnailPath;

            if (targetCard != null)
                targetCard.UpdateDownloadProgress(1f);

            OnStartPuzzle(puzzle, false);
        }
        else
        {
            if (targetCard != null)
                targetCard.UpdateDownloadProgress(-1f);

            Debug.LogError($"[MenuManager] Failed to download puzzle: {puzzle.remoteJobId}");
        }
    }

    public void OnResetPuzzle(PuzzleInfo puzzle)
    {
        File.Delete(Path.Combine(puzzle.folderPath, "save.json"));

        foreach (Transform child in workingContainer)
        {
            if (child.GetComponent<PuzzleCard>() != null)
                Destroy(child.gameObject);
        }

        _ = DiscoverPuzzlesAsync();
    }

    async Task RefreshPuzzleList()
    {
        foreach (Transform child in workingContainer)
        {
            if (child.GetComponent<PuzzleCard>() != null)
                Destroy(child.gameObject);
        }

        await DiscoverPuzzlesAsync();
    }
}

[System.Serializable]
public class PuzzleInfo
{
    public string folderPath;
    public string name;
    public int pieceCount;
    public string thumbnailPath;
    public float progress;
    public bool hasSave;
    public bool isRemote;
    public bool isDownloaded;
    public string remoteJobId;
    public string displayName;
    public string sourceModel;
}
