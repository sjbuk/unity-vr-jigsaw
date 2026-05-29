using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor window with menu commands for setting up Jigsaw VR scenes, configuring project settings,
/// and testing puzzles directly from the Editor. Provides one-click scene creation for Bootstrap,
/// MainMenu, and PuzzleScene with all required components and references.
/// </summary>
public class JigsawSceneSetup : EditorWindow
{
    [MenuItem("Jigsaw/Setup Puzzle Scene")]
    static void SetupPuzzleScene()
    {
        string sceneDir = "Assets/_Project/Scenes";
        Directory.CreateDirectory(sceneDir);

        string scenePath = Path.Combine(sceneDir, "PuzzleScene.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);

        CreatePuzzleScene(scene);
        Debug.Log($"PuzzleScene created at {scenePath}");
    }

    [MenuItem("Jigsaw/Setup Main Menu Scene")]
    static void SetupMainMenuScene()
    {
        string sceneDir = "Assets/_Project/Scenes";
        Directory.CreateDirectory(sceneDir);

        string scenePath = Path.Combine(sceneDir, "MainMenu.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);

        CreateMainMenuScene(scene);
        Debug.Log($"MainMenu created at {scenePath}");
    }

    [MenuItem("Jigsaw/Setup All Scenes & Project Settings")]
    static void SetupAll()
    {
        SetupPuzzleScene();
        SetupMainMenuScene();
        SetupBootstrapScene();
        ConfigureProjectSettings();
        ConfigureBuildSettings();

        Debug.Log("Jigsaw VR project fully configured!");
    }

    static void SetupBootstrapScene()
    {
        string sceneDir = "Assets/_Project/Scenes";
        string scenePath = Path.Combine(sceneDir, "Bootstrap.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"Bootstrap created at {scenePath}");
    }

    static void CreatePuzzleScene(Scene scene)
    {
        CreateDirectionalLight();
        CreateEventSystem();
        GameObject xrOrigin = CreateXROrigin();
        GameObject puzzleManager = new GameObject("Puzzle Manager");
        puzzleManager.AddComponent<PuzzleManager>();

        GameObject wallGrid = new GameObject("Wall Grid");
        wallGrid.AddComponent<WallGrid>();

        GameObject piecesContainer = new GameObject("Pieces Container");

        GameObject snapSystem = new GameObject("Snap System");
        snapSystem.AddComponent<SnapSystem>();

        GameObject saveSystem = new GameObject("Save System");
        saveSystem.AddComponent<SaveManager>();

        GameObject completionFX = new GameObject("Completion FX");
        completionFX.AddComponent<CompletionFX>();

        GameObject audioManager = new GameObject("Audio Manager");
        audioManager.AddComponent<AudioManager>();

        GameObject uiCanvas = new GameObject("UI Canvas");
        var canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = xrOrigin.GetComponentInChildren<Camera>();
        var canvasRT = canvas.GetComponent<RectTransform>();
        canvasRT.SetParent(xrOrigin.GetComponentInChildren<XROrigin>().CameraFloorOffsetObject.transform);
        canvasRT.localPosition = new Vector3(0, 0.4f, 1.5f);
        canvasRT.sizeDelta = new Vector2(0.6f, 0.3f);
        uiCanvas.AddComponent<CanvasScaler>();
        uiCanvas.AddComponent<GraphicRaycaster>();
        AddTrackedDeviceRaycaster(uiCanvas);

        GameObject returnButton = CreateReturnButton(uiCanvas, completionFX);

        LinkPuzzleSceneReferences(puzzleManager, wallGrid, snapSystem, saveSystem, completionFX, audioManager, xrOrigin);

        var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (es != null)
        {
            var inputModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (inputModule != null)
                inputModule.xrTrackingOrigin = xrOrigin.GetComponent<XROrigin>().transform;
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    static GameObject CreateReturnButton(GameObject parentCanvas, GameObject completionFX)
    {
        var btnGO = new GameObject("Return To Menu Button", typeof(RectTransform));
        btnGO.transform.SetParent(parentCanvas.transform, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0.25f, 0.07f);
        rt.anchoredPosition = Vector2.zero;

        var img = btnGO.AddComponent<Image>();
        img.color = new Color(0.129f, 0.498f, 0.824f);
        img.raycastTarget = true;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = new ColorBlock
        {
            normalColor = new Color(0.129f, 0.498f, 0.824f),
            highlightedColor = new Color(0.259f, 0.647f, 0.961f),
            pressedColor = new Color(0.078f, 0.376f, 0.624f),
            selectedColor = new Color(0.129f, 0.498f, 0.824f),
            disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;
        var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = "Return to Menu";
        tmp.fontSize = 0.035f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.raycastTarget = false;

        btnGO.SetActive(false);

        var fx = completionFX.GetComponent<CompletionFX>();
        if (fx != null)
            fx.returnToMenuButton = btnGO;

        return btnGO;
    }

    static void CreateMainMenuScene(Scene scene)
    {
        CreateDirectionalLight();
        CreateEventSystem();
        GameObject xrOrigin = CreateXROrigin();

        GameObject menuManager = new GameObject("Menu Manager");
        var mm = menuManager.AddComponent<MenuManager>();

        GameObject menuPanels = new GameObject("Menu Panels Container");
        menuPanels.transform.SetParent(xrOrigin.transform);
        menuPanels.transform.localPosition = new Vector3(0, mm.menuHeight, mm.menuForwardDistance);
        mm.cardsContainer = menuPanels.transform;

        GameObject uiCanvas = new GameObject("UI Canvas");
        var canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRT = canvas.GetComponent<RectTransform>();
        canvasRT.SetParent(xrOrigin.transform);
        canvasRT.localPosition = new Vector3(0, mm.menuHeight, mm.menuForwardDistance);
        canvasRT.sizeDelta = new Vector2(2, 1);
        uiCanvas.AddComponent<CanvasScaler>();
        uiCanvas.AddComponent<GraphicRaycaster>();
        AddTrackedDeviceRaycaster(uiCanvas);

        var textGO = new GameObject("Placeholder Text");
        textGO.transform.SetParent(canvasRT, false);
        var text = textGO.AddComponent<TextMeshPro>();
        text.text = "Jigsaw VR — No puzzles found\nAdd puzzle folders to Assets/_Project/Puzzels/";
        text.fontSize = 0.08f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        var textRT = text.GetComponent<RectTransform>();
        textRT.sizeDelta = new Vector2(2, 1);
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        mm.puzzleCardPrefab = CreatePuzzleCardPrefab();

        var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (es != null)
        {
            var inputModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (inputModule != null)
                inputModule.xrTrackingOrigin = xrOrigin.GetComponent<XROrigin>().transform;
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    static GameObject CreatePuzzleCardPrefab()
    {
        string prefabPath = "Assets/_Project/Prefabs/PuzzleCard.prefab";

        GameObject card = new GameObject("PuzzleCard", typeof(RectTransform));
        var cardRT = card.GetComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(0.4f, 0.5f);

        var puzzleCard = card.AddComponent<PuzzleCard>();

        CreateCardBackground(card, cardRT);

        CreateThumbnailArea(card, puzzleCard);

        CreateNameText(card, puzzleCard);
        CreatePieceCountText(card, puzzleCard);
        CreateProgressArea(card, puzzleCard);
        CreateButtonRow(card, puzzleCard);

        Directory.CreateDirectory("Assets/_Project/Prefabs");

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(prefabPath);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(card, prefabPath);
        Object.DestroyImmediate(card);
        return prefab;
    }

    static void CreateCardBackground(GameObject card, RectTransform cardRT)
    {
        var borderGO = new GameObject("PanelBorder", typeof(RectTransform));
        borderGO.transform.SetParent(card.transform, false);
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color(0.18f, 0.18f, 0.28f, 1f);
        borderImg.raycastTarget = false;

        var bgGO = new GameObject("PanelBackground", typeof(RectTransform));
        bgGO.transform.SetParent(card.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = new Vector2(0.005f, 0.005f);
        bgRT.offsetMax = new Vector2(-0.005f, -0.005f);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.078f, 0.078f, 0.106f, 0.92f);
        bgImg.raycastTarget = true;
    }

    static void CreateThumbnailArea(GameObject card, PuzzleCard puzzleCard)
    {
        var frameGO = new GameObject("ThumbnailFrame", typeof(RectTransform));
        frameGO.transform.SetParent(card.transform, false);
        var frameRT = frameGO.GetComponent<RectTransform>();
        frameRT.anchorMin = new Vector2(0.025f, 0.55f);
        frameRT.anchorMax = new Vector2(0.975f, 0.95f);
        frameRT.offsetMin = Vector2.zero;
        frameRT.offsetMax = Vector2.zero;
        var frameImg = frameGO.AddComponent<Image>();
        frameImg.color = new Color(0.22f, 0.22f, 0.32f, 1f);
        frameImg.raycastTarget = false;

        var thumbGO = new GameObject("ThumbnailImage", typeof(RectTransform));
        thumbGO.transform.SetParent(frameGO.transform, false);
        var thumbRT = thumbGO.GetComponent<RectTransform>();
        thumbRT.anchorMin = new Vector2(0.04f, 0.06f);
        thumbRT.anchorMax = new Vector2(0.96f, 0.94f);
        thumbRT.offsetMin = Vector2.zero;
        thumbRT.offsetMax = Vector2.zero;
        var rawImg = thumbGO.AddComponent<RawImage>();
        rawImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        rawImg.raycastTarget = false;
        puzzleCard.thumbnailImage = rawImg;
    }

    static void CreateNameText(GameObject card, PuzzleCard puzzleCard)
    {
        var nameGO = new GameObject("NameText", typeof(RectTransform));
        nameGO.transform.SetParent(card.transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0.04f, 0.49f);
        nameRT.anchorMax = new Vector2(0.96f, 0.55f);
        nameRT.offsetMin = Vector2.zero;
        nameRT.offsetMax = Vector2.zero;
        var nameText = nameGO.AddComponent<TextMeshProUGUI>();
        nameText.text = "Puzzle Name";
        nameText.fontSize = 0.035f;
        nameText.alignment = TextAlignmentOptions.Left;
        nameText.color = Color.white;
        nameText.fontStyle = FontStyles.Bold;
        nameText.raycastTarget = false;
        puzzleCard.nameText = nameText;
    }

    static void CreatePieceCountText(GameObject card, PuzzleCard puzzleCard)
    {
        var countGO = new GameObject("PieceCountText", typeof(RectTransform));
        countGO.transform.SetParent(card.transform, false);
        var countRT = countGO.GetComponent<RectTransform>();
        countRT.anchorMin = new Vector2(0.04f, 0.45f);
        countRT.anchorMax = new Vector2(0.96f, 0.49f);
        countRT.offsetMin = Vector2.zero;
        countRT.offsetMax = Vector2.zero;
        var countText = countGO.AddComponent<TextMeshProUGUI>();
        countText.text = "0 pieces";
        countText.fontSize = 0.025f;
        countText.alignment = TextAlignmentOptions.Left;
        countText.color = new Color(0.6f, 0.6f, 0.65f);
        countText.raycastTarget = false;
        puzzleCard.pieceCountText = countText;
    }

    static void CreateProgressArea(GameObject card, PuzzleCard puzzleCard)
    {
        var progressTextGO = new GameObject("ProgressText", typeof(RectTransform));
        progressTextGO.transform.SetParent(card.transform, false);
        var ptRT = progressTextGO.GetComponent<RectTransform>();
        ptRT.anchorMin = new Vector2(0.04f, 0.41f);
        ptRT.anchorMax = new Vector2(0.96f, 0.45f);
        ptRT.offsetMin = Vector2.zero;
        ptRT.offsetMax = Vector2.zero;
        var pt = progressTextGO.AddComponent<TextMeshProUGUI>();
        pt.text = "0%";
        pt.fontSize = 0.02f;
        pt.alignment = TextAlignmentOptions.Right;
        pt.color = new Color(0.5f, 0.5f, 0.55f);
        pt.raycastTarget = false;
        puzzleCard.progressText = pt;

        var sliderGO = new GameObject("ProgressSlider", typeof(RectTransform));
        sliderGO.transform.SetParent(card.transform, false);
        var sliderRT = sliderGO.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.04f, 0.39f);
        sliderRT.anchorMax = new Vector2(0.96f, 0.41f);
        sliderRT.offsetMin = Vector2.zero;
        sliderRT.offsetMax = Vector2.zero;
        var slider = sliderGO.AddComponent<Slider>();
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        CreateSliderBackground(sliderGO, slider);
        CreateSliderFillArea(sliderGO, slider);

        var handleGO = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleGO.transform.SetParent(sliderGO.transform, false);
        var handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.anchorMin = Vector2.zero;
        handleRT.anchorMax = Vector2.zero;
        handleRT.offsetMin = Vector2.zero;
        handleRT.offsetMax = Vector2.zero;
        handleGO.SetActive(false);

        puzzleCard.progressSlider = slider;
    }

    static void CreateSliderBackground(GameObject sliderGO, Slider slider)
    {
        var bgGO = new GameObject("Background", typeof(RectTransform));
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        bgImg.raycastTarget = false;
    }

    static void CreateSliderFillArea(GameObject sliderGO, Slider slider)
    {
        var fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1, 0.75f);
        fillAreaRT.offsetMin = Vector2.zero;
        fillAreaRT.offsetMax = Vector2.zero;

        var fillGO = new GameObject("Fill", typeof(RectTransform));
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.157f, 0.569f, 0.275f, 1f);
        fillImg.raycastTarget = false;

        slider.fillRect = fillRT;
        slider.targetGraphic = fillImg;
    }

    static void CreateButtonRow(GameObject card, PuzzleCard puzzleCard)
    {
        var rowGO = new GameObject("ButtonRow", typeof(RectTransform));
        rowGO.transform.SetParent(card.transform, false);
        var rowRT = rowGO.GetComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0.04f, 0.03f);
        rowRT.anchorMax = new Vector2(0.96f, 0.35f);
        rowRT.offsetMin = Vector2.zero;
        rowRT.offsetMax = Vector2.zero;

        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 0.01f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        puzzleCard.resumeButton = CreateButton(rowGO, "Resume", false);
        puzzleCard.newGameButton = CreateButton(rowGO, "New Game", true);
        puzzleCard.resetButton = CreateButton(rowGO, "Reset", false);
    }

    static Button CreateButton(GameObject parent, string label, bool startActive)
    {
        var btnGO = new GameObject(label.Replace(" ", "") + "Button", typeof(RectTransform));
        btnGO.transform.SetParent(parent.transform, false);
        btnGO.SetActive(startActive);

        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(0f, 0.06f);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.raycastTarget = true;
        btnImg.type = Image.Type.Sliced;

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(btnGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 0.028f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;

        return btn;
    }

    static GameObject CreateXROrigin()
    {
        var xrOrigin = new GameObject("XR Origin");
        var originComponent = xrOrigin.AddComponent<XROrigin>();

        var camOffset = new GameObject("Camera Floor Offset");
        camOffset.transform.SetParent(xrOrigin.transform);
        originComponent.CameraFloorOffsetObject = camOffset;

        var mainCam = new GameObject("Main Camera");
        mainCam.transform.SetParent(camOffset.transform);
        var cam = mainCam.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.01f;
        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        cam.tag = "MainCamera";
        mainCam.AddComponent<AudioListener>();
        mainCam.AddComponent<TrackedPoseDriver>();

        originComponent.Camera = cam;

        var leftController = new GameObject("Left Controller");
        leftController.transform.SetParent(camOffset.transform);
        SetupController(leftController);

        var rightController = new GameObject("Right Controller");
        rightController.transform.SetParent(camOffset.transform);
        SetupController(rightController);

        SetupLaserPointer(leftController, "LeftHand");
        SetupLaserPointer(rightController, "RightHand");

        return xrOrigin;
    }

    static void SetupController(GameObject controller)
    {
        var interactor = controller.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
        var rayInteractor = controller.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>();

        var line = controller.AddComponent<LineRenderer>();
        line.widthMultiplier = 0.005f;
        line.positionCount = 2;

        var attachPoint = new GameObject("AttachPoint");
        attachPoint.transform.SetParent(controller.transform);
    }

    static void SetupLaserPointer(GameObject controller, string hand)
    {
        var laser = controller.AddComponent<LaserPointer>();
        laser.controllerTransform = controller.transform;
        laser.lineRenderer = controller.GetComponent<LineRenderer>();

        var pieceHolder = controller.GetComponent<PieceHolder>();
        if (pieceHolder == null)
        {
            pieceHolder = controller.AddComponent<PieceHolder>();
        }
        pieceHolder.attachPoint = controller.transform.Find("AttachPoint");
        pieceHolder.laserPointer = laser;

        laser.pieceHolder = pieceHolder;
    }

    static void LinkPuzzleSceneReferences(GameObject puzzleManager, GameObject wallGrid, GameObject snapSystem,
        GameObject saveSystem, GameObject completionFX, GameObject audioManager, GameObject xrOrigin)
    {
        var pm = puzzleManager.GetComponent<PuzzleManager>();
        pm.wallGrid = wallGrid.GetComponent<WallGrid>();
        pm.snapSystem = snapSystem.GetComponent<SnapSystem>();
        pm.saveManager = saveSystem.GetComponent<SaveManager>();
        pm.completionFX = completionFX.GetComponent<CompletionFX>();

        var snap = snapSystem.GetComponent<SnapSystem>();
        var leftController = xrOrigin.transform.Find("Camera Floor Offset/Left Controller");
        var rightController = xrOrigin.transform.Find("Camera Floor Offset/Right Controller");

        if (leftController != null)
        {
            snap.leftHolder = leftController.GetComponent<PieceHolder>();
            var holder = leftController.GetComponent<PieceHolder>();
            if (holder != null) holder.wallGrid = wallGrid.GetComponent<WallGrid>();
        }

        if (rightController != null)
        {
            snap.rightHolder = rightController.GetComponent<PieceHolder>();
            var holder = rightController.GetComponent<PieceHolder>();
            if (holder != null) holder.wallGrid = wallGrid.GetComponent<WallGrid>();
        }

        snap.audioManager = audioManager.GetComponent<AudioManager>();
    }

    [MenuItem("Jigsaw/Configure Project Settings")]
    static void ConfigureProjectSettings()
    {
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Unity_4_8);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetArchitecture(BuildTargetGroup.Android, 1);

        QualitySettings.vSyncCount = 0;
        QualitySettings.shadowDistance = 15f;
        QualitySettings.pixelLightCount = 1;

        string[] urpAssets = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        if (urpAssets.Length > 0)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                AssetDatabase.GUIDToAssetPath(urpAssets[0]));
            if (pipeline != null)
            {
                var serialized = new SerializedObject(pipeline);
                serialized.FindProperty("m_MainLightRenderingMode").enumValueIndex = 1;
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_MainLightShadowmapResolution").enumValueIndex = (int)UnityEngine.Rendering.Universal.ShadowResolution._1024;
                serialized.ApplyModifiedProperties();
            }
        }

        Debug.Log("Project settings configured for Jigsaw VR");
    }

    static void ConfigureBuildSettings()
    {
        var scenes = new[]
        {
            "Assets/_Project/Scenes/Bootstrap.unity",
            "Assets/_Project/Scenes/MainMenu.unity",
            "Assets/_Project/Scenes/PuzzleScene.unity"
        };

        foreach (var scene in scenes)
        {
            if (File.Exists(scene))
            {
                var existing = EditorBuildSettings.scenes;
                bool found = false;

                foreach (var es in existing)
                {
                    if (es.path == scene)
                    {
                        found = true;
                        es.enabled = true;
                        break;
                    }
                }

                if (!found)
                {
                    System.Array.Resize(ref existing, existing.Length + 1);
                    existing[^1] = new EditorBuildSettingsScene(scene, true);
                    EditorBuildSettings.scenes = existing;
                }
            }
        }

        Debug.Log("Build settings configured");
    }

    [MenuItem("Jigsaw/Add XR Device Simulator")]
    static void AddXRDeviceSimulator()
    {
        var existing = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>();
        if (existing != null)
        {
            Debug.Log("XR Device Simulator already exists in the scene. Selecting it.");
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            return;
        }

        string prefabPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/XR Device Simulator/XR Device Simulator.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"XR Device Simulator prefab not found at {prefabPath}. Import the sample first via Window > Package Manager > XR Interaction Toolkit > Samples.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Add XR Device Simulator");
        instance.name = "XR Device Simulator";
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("XR Device Simulator added to scene. Configure controls via the XR Device Simulator component.");
    }

    [MenuItem("Jigsaw/Test Puzzle Scene")]
    static void TestPuzzleScene()
    {
        string defaultPath = System.IO.Path.Combine(Application.dataPath, "_Project", "Puzzels");
        string folder = EditorUtility.OpenFolderPanel("Select Puzzle Folder", defaultPath, "");
        if (string.IsNullOrEmpty(folder)) return;

        PuzzleManager.PuzzleFolderPath = folder;
        PuzzleManager.LoadOnStart = PuzzleManager.LoadMode.NewGame;

        string puzzleScenePath = "Assets/_Project/Scenes/PuzzleScene.unity";
        if (System.IO.File.Exists(puzzleScenePath))
        {
            EditorSceneManager.OpenScene(puzzleScenePath);
            EditorApplication.EnterPlaymode();
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "PuzzleScene not found. Run Jigsaw/Setup Puzzle Scene first.", "OK");
        }
    }

    static void CreateDirectionalLight()
    {
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
    }

    static void CreateEventSystem()
    {
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    static void AddTrackedDeviceRaycaster(GameObject canvas)
    {
        var tdrType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceRaycaster, Unity.XR.Interaction.Toolkit");
        if (tdrType != null)
            canvas.AddComponent(tdrType);
    }
}
