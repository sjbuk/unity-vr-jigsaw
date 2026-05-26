using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Editor window with menu commands for setting up Jigsaw VR scenes, configuring project settings,
/// and testing puzzles directly from the Editor. Provides one-click scene creation for Bootstrap,
/// MainMenu, and PuzzleScene with all required components and references.
/// </summary>
public class JigsawSceneSetup : EditorWindow
{
    /// <summary>Creates a new PuzzleScene with all required GameObjects and component references.</summary>
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

    /// <summary>Creates a new MainMenu scene with XR Origin, MenuManager, UI canvas, and puzzle card prefab.</summary>
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

    /// <summary>Runs all setup steps: creates all three scenes and configures project/build settings.</summary>
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

    /// <summary>Creates an empty Bootstrap scene.</summary>
    static void SetupBootstrapScene()
    {
        string sceneDir = "Assets/_Project/Scenes";
        string scenePath = Path.Combine(sceneDir, "Bootstrap.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"Bootstrap created at {scenePath}");
    }

    /// <summary>Populates the PuzzleScene with all required GameObjects (PuzzleManager, WallGrid, SnapSystem, etc.) and links references.</summary>
    /// <param name="scene">The scene to populate.</param>
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

        GameObject returnButton = new GameObject("Return To Menu Button");
        returnButton.transform.SetParent(uiCanvas.transform);
        returnButton.SetActive(false);

        LinkPuzzleSceneReferences(puzzleManager, wallGrid, snapSystem, saveSystem, completionFX, audioManager, xrOrigin);

        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>Populates the MainMenu scene with XR Origin, MenuManager, UI canvas, and a PuzzleCard prefab.</summary>
    /// <param name="scene">The scene to populate.</param>
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
        uiCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        uiCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var textGO = new GameObject("Placeholder Text");
        textGO.transform.SetParent(canvasRT, false);
        var text = textGO.AddComponent<TMPro.TextMeshPro>();
        text.text = "Jigsaw VR — No puzzles found\nAdd puzzle folders to Assets/_Project/Puzzels/";
        text.fontSize = 0.08f;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        var textRT = text.GetComponent<RectTransform>();
        textRT.sizeDelta = new Vector2(2, 1);
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        mm.puzzleCardPrefab = CreatePuzzleCardPrefab();

        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>Creates or loads the PuzzleCard prefab with UI sub-objects (background, name text, count text).</summary>
    /// <returns>The existing or newly created PuzzleCard prefab.</returns>
    static GameObject CreatePuzzleCardPrefab()
    {
        string prefabPath = "Assets/_Project/Prefabs/PuzzleCard.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing;

        var card = new GameObject("PuzzleCard", typeof(RectTransform));
        var puzzleCard = card.AddComponent<PuzzleCard>();

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(card.transform, false);
        var img = bg.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        var nameGO = new GameObject("NameText", typeof(RectTransform));
        nameGO.transform.SetParent(card.transform, false);
        var nameText = nameGO.AddComponent<TMPro.TextMeshPro>();
        nameText.text = "Puzzle Name";
        nameText.fontSize = 18f;
        nameText.alignment = TMPro.TextAlignmentOptions.TopLeft;
        nameText.color = Color.white;
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0.5f);
        nameRT.anchorMax = new Vector2(1, 1);
        nameRT.offsetMin = new Vector2(0.02f, 0);
        nameRT.offsetMax = new Vector2(-0.02f, -0.02f);
        puzzleCard.nameText = nameText;

        var countGO = new GameObject("CountText", typeof(RectTransform));
        countGO.transform.SetParent(card.transform, false);
        var countText = countGO.AddComponent<TMPro.TextMeshPro>();
        countText.text = "0 pieces";
        countText.fontSize = 12f;
        countText.alignment = TMPro.TextAlignmentOptions.TopLeft;
        countText.color = Color.gray;
        var countRT = countGO.GetComponent<RectTransform>();
        countRT.anchorMin = new Vector2(0, 0);
        countRT.anchorMax = new Vector2(1, 0.5f);
        countRT.offsetMin = new Vector2(0.02f, 0.02f);
        countRT.offsetMax = new Vector2(-0.02f, -0.02f);
        puzzleCard.pieceCountText = countText;

        Directory.CreateDirectory("Assets/_Project/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(card, prefabPath);
        Object.DestroyImmediate(card);
        return prefab;
    }

    /// <summary>Creates a full XR Origin hierarchy with camera floor offset, main camera, left/right controllers, and laser pointer setup.</summary>
    /// <returns>The root XR Origin GameObject.</returns>
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

    /// <summary>Adds XR interactors, LineRenderer, and attach point to a controller GameObject.</summary>
    /// <param name="controller">The controller GameObject to configure.</param>
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

    /// <summary>Adds LaserPointer and PieceHolder components to a controller and wires them together.</summary>
    /// <param name="controller">The controller GameObject to configure.</param>
    /// <param name="hand">Hand identifier ("LeftHand" or "RightHand").</param>
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

    /// <summary>Links cross-references between PuzzleScene GameObjects (e.g., SnapSystem needs PieceHolder references).</summary>
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

    /// <summary>Configures Android player settings (API level, IL2CPP, architecture) and URP quality settings.</summary>
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

    /// <summary>Adds the Bootstrap, MainMenu, and PuzzleScene to the build's scene list.</summary>
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

    /// <summary>Opens a folder picker to select a puzzle, sets PuzzleManager statics, and enters play mode.</summary>
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

    /// <summary>Creates a directional light with soft shadows for the scene.</summary>
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

    /// <summary>Creates an EventSystem with InputSystemUIInputModule for UI interactions.</summary>
    static void CreateEventSystem()
    {
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
