using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

public class SetupControllerInteractors : EditorWindow
{
    [MenuItem("Jigsaw/Setup Controller Interactors")]
    static void SetupInteractors()
    {
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("No XR Origin found in the scene.");
            return;
        }

        var offset = xrOrigin.CameraFloorOffsetObject;
        if (offset == null)
        {
            Debug.LogError("XR Origin has no Camera Floor Offset.");
            return;
        }

        var leftCtrl = offset.transform.Find("Left Controller");
        var rightCtrl = offset.transform.Find("Right Controller");

        if (leftCtrl == null)
        {
            Debug.LogError("Left Controller not found under Camera Floor Offset.");
            return;
        }
        if (rightCtrl == null)
        {
            Debug.LogError("Right Controller not found under Camera Floor Offset.");
            return;
        }

        SetupController(leftCtrl.gameObject, true);
        SetupController(rightCtrl.gameObject, false);

        EnsureHighlightMaterial();

        LinkSceneReferences(xrOrigin.gameObject);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Controller interactors set up successfully!");
    }

    static void SetupController(GameObject controller, bool isLeft)
    {
        var laser = controller.GetComponent<LaserPointer>();
        if (laser == null)
            laser = controller.AddComponent<LaserPointer>();

        var holder = controller.GetComponent<PieceHolder>();
        if (holder == null)
            holder = controller.AddComponent<PieceHolder>();

        var xrController = controller.GetComponent<XRBaseController>();
        if (xrController == null)
            Debug.LogWarning($"No XRBaseController on {controller.name}");

        laser.controller = xrController;
        laser.controllerTransform = controller.transform;
        laser.pieceHolder = holder;
        laser.Hand = isLeft ? LaserPointer.HandSide.Left : LaserPointer.HandSide.Right;

        Transform attachPoint = controller.transform.Find("AttachPoint");
        if (attachPoint == null)
        {
            var ap = new GameObject("AttachPoint");
            ap.transform.SetParent(controller.transform, false);
            attachPoint = ap.transform;
        }

        holder.attachPoint = attachPoint;
        holder.controller = xrController;
        holder.laserPointer = laser;

        Transform laserVis = controller.transform.Find("LaserVisual");
        if (laserVis == null)
        {
            var lv = new GameObject("LaserVisual");
            lv.transform.SetParent(controller.transform, false);
            var lr = lv.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.005f;
            lr.positionCount = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.red;
            lr.endColor = new Color(1, 0, 0, 0.2f);
            laserVis = lv.transform;
        }

        laser.lineRenderer = laserVis.GetComponent<LineRenderer>();
        if (laser.lineRenderer == null)
        {
            laser.lineRenderer = laserVis.gameObject.AddComponent<LineRenderer>();
            laser.lineRenderer.widthMultiplier = 0.005f;
            laser.lineRenderer.positionCount = 2;
            laser.lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            laser.lineRenderer.startColor = Color.red;
            laser.lineRenderer.endColor = new Color(1, 0, 0, 0.2f);
        }

        Debug.Log($"Setup {controller.name}: LaserPointer + PieceHolder added.");
    }

    static void EnsureHighlightMaterial()
    {
        const string resourcePath = "Assets/_Project/Resources/PieceHighlight.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(resourcePath);
        if (existing != null) return;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(1f, 0.84f, 0f, 0.5f);

        AssetDatabase.CreateAsset(mat, resourcePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created PieceHighlight material at {resourcePath}");
    }

    static void LinkSceneReferences(GameObject xrOrigin)
    {
        var snapSystem = FindObjectOfType<SnapSystem>();
        var wallGrid = FindObjectOfType<WallGrid>();

        if (snapSystem != null)
        {
            var offset = xrOrigin.GetComponent<Unity.XR.CoreUtils.XROrigin>().CameraFloorOffsetObject;
            var leftCtrl = offset.transform.Find("Left Controller");
            var rightCtrl = offset.transform.Find("Right Controller");

            if (leftCtrl != null)
                snapSystem.leftHolder = leftCtrl.GetComponent<PieceHolder>();
            if (rightCtrl != null)
                snapSystem.rightHolder = rightCtrl.GetComponent<PieceHolder>();

            Debug.Log("SnapSystem references linked.");
        }
        else
        {
            Debug.LogWarning("SnapSystem not found in scene.");
        }

        if (wallGrid != null)
        {
            var offset = xrOrigin.GetComponent<Unity.XR.CoreUtils.XROrigin>().CameraFloorOffsetObject;
            var leftCtrl = offset.transform.Find("Left Controller");
            var rightCtrl = offset.transform.Find("Right Controller");

            if (leftCtrl != null)
            {
                var h = leftCtrl.GetComponent<PieceHolder>();
                if (h != null) h.wallGrid = wallGrid;
            }
            if (rightCtrl != null)
            {
                var h = rightCtrl.GetComponent<PieceHolder>();
                if (h != null) h.wallGrid = wallGrid;
            }

            Debug.Log("WallGrid references linked.");
        }
        else
        {
            Debug.LogWarning("WallGrid not found in scene.");
        }
    }
}
