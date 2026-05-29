using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
/// and handles puzzle start/reset actions. Also sets up XR UI interaction at runtime.
/// </summary>
public class MenuManager : MonoBehaviour
{
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;
    public float cardSpacing = 0.55f;
    public float cardWorldScale = 1f;
    public float menuHeight = 0f;
    public float menuForwardDistance = 1.5f;

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;
    private Transform workingContainer;
    private TMP_Text titleText;

    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;

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

        SetupContainer();
        SetupXRTrackingOrigin();
        SetupMenuUIControllers();
        SetupCanvasWorldCamera();
        CreateTitle();
        DiscoverPuzzles();
        ArrangePanels();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
            && discoveredPuzzles != null && discoveredPuzzles.Count > 0)
        {
            OnStartPuzzle(discoveredPuzzles[0], false);
        }

        HandleDirectUIClicks();
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

        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t.name != "Left Controller" && t.name != "Right Controller")
                continue;

            foreach (var lp in t.GetComponentsInChildren<LaserPointer>(true))
                lp.enabled = false;

            foreach (var ph in t.GetComponentsInChildren<PieceHolder>(true))
                ph.enabled = false;

            var laserVisual = t.Find("LaserVisual");
            if (laserVisual != null) laserVisual.gameObject.SetActive(false);

            foreach (var nf in t.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nf.enableNearCasting = false;
                nf.enableFarCasting = true;
                nf.enableUIInteraction = true;
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
                visual.extendLineToEmptyHit = true;
                visual.restingVisualLineLength = 3f;
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

    void HandleDirectUIClicks()
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

        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t.name != "Left Controller" && t.name != "Right Controller")
                continue;

            var triggerAction = t.name == "Left Controller" ? leftTriggerAction : rightTriggerAction;
            if (triggerAction == null) continue;
            if (!triggerAction.WasPressedThisFrame()) continue;

            var origin = t.position;
            var forward = t.forward;
            var hitButton = FindClosestButtonUnderRay(origin, forward);
            if (hitButton != null)
                hitButton.onClick.Invoke();
        }
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

        var cam = Camera.main;
        if (cam == null) return;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(workingContainer, false);

        var rt = titleGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2f, 0.15f);

        Vector3 forward = cam.transform.forward;
        titleGO.transform.position = cam.transform.position
            + forward * menuForwardDistance
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

    void DiscoverPuzzles()
    {
        discoveredPuzzles = new List<PuzzleInfo>();

        if (!Directory.Exists(puzzlesPath))
        {
            Debug.LogWarning($"[MenuManager] Puzzle path does not exist: {puzzlesPath}");
            return;
        }

        var dirs = Directory.GetDirectories(puzzlesPath);

        foreach (var dir in dirs)
        {
            string checkpoint = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(checkpoint)) continue;

            var json = File.ReadAllText(checkpoint);
            var data = JsonUtility.FromJson<CheckpointData>(json);
            if (data == null) continue;

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
                name = Path.GetFileName(dir),
                pieceCount = data.piece_count,
                thumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                progress = progress,
                hasSave = hasSave
            });
        }
    }

    void ArrangePanels()
    {
        int count = discoveredPuzzles.Count;

        if (count == 0)
        {
            if (titleText != null)
                titleText.text = "No Puzzles Found";
            return;
        }

        if (puzzleCardPrefab == null)
        {
            Debug.LogError("[MenuManager] puzzleCardPrefab not assigned!");
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, workingContainer);

            float totalWidth = (count - 1) * cardSpacing;
            float offsetX = -totalWidth * 0.5f + i * cardSpacing;

            Vector3 worldCenter = cam.transform.position
                + forward * menuForwardDistance
                + Vector3.up * menuHeight
                + right * offsetX;

            card.transform.position = worldCenter;
            card.transform.localScale = new Vector3(cardWorldScale, cardWorldScale, cardWorldScale);
            card.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var puzzleCard = card.GetComponent<PuzzleCard>();
            if (puzzleCard != null)
                puzzleCard.Initialize(discoveredPuzzles[i], this);
        }
    }

    public void OnStartPuzzle(PuzzleInfo puzzle, bool resume)
    {
        PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
        PuzzleManager.LoadOnStart = resume ? PuzzleManager.LoadMode.Resume : PuzzleManager.LoadMode.NewGame;
        SceneManager.LoadScene("PuzzleScene");
    }

    public void OnResetPuzzle(PuzzleInfo puzzle)
    {
        File.Delete(Path.Combine(puzzle.folderPath, "save.json"));
        foreach (Transform child in workingContainer)
            Destroy(child.gameObject);
        titleText = null;
        CreateTitle();
        DiscoverPuzzles();
        ArrangePanels();
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
}
