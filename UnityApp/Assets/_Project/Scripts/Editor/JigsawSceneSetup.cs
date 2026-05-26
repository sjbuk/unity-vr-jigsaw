using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.UI;

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

        GameObject returnButton = new GameObject("Return To Menu Button");
        returnButton.transform.SetParent(uiCanvas.transform);
        returnButton.SetActive(false);

        LinkPuzzleSceneReferences(puzzleManager, wallGrid, snapSystem, saveSystem, completionFX, audioManager, xrOrigin);

        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void CreateMainMenuScene(Scene scene)
    {
        CreateDirectionalLight();
        CreateEventSystem();
        GameObject xrOrigin = CreateXROrigin();

        GameObject menuManager = new GameObject("Menu Manager");
        menuManager.AddComponent<MenuManager>();

        GameObject menuPanels = new GameObject("Menu Panels Container");

        GameObject uiCanvas = new GameObject("UI Canvas");
        var canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        EditorSceneManager.MarkSceneDirty(scene);
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

    [MenuItem("Jigsaw/Test Puzzle Scene")]
    static void TestPuzzleScene()
    {
        string folder = EditorUtility.OpenFolderPanel("Select Puzzle Folder", Application.persistentDataPath, "");
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
}
