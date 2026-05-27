using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SnapDebugHUD : MonoBehaviour
{
    public SnapSystem snapSystem;
    public float fontSize = 0.012f;
    public float lineHeight = 0.014f;
    public float panelWidth = 0.55f;
    public float maxPanelHeight = 0.42f;

    private Canvas canvas;
    private GameObject panel;
    private TMP_Text labelText;
    private TMP_Text listText;
    private Image panelBg;
    private bool visible = true;
    private bool hudReady;

    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();

    void Start()
    {
        if (snapSystem == null)
            snapSystem = FindObjectOfType<SnapSystem>();
        CreateHUD();
    }

    void Update()
    {
        if (!hudReady) return;

        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
        {
            visible = !visible;
            if (panel != null) panel.SetActive(visible);
        }

        if (!visible) return;

        try
        {
            PieceState[] pieces = FindObjectsOfType<PieceState>();
            if (pieces == null || pieces.Length == 0)
            {
                if (listText != null) listText.text = "No pieces found";
                return;
            }

            sb.Clear();
            sb.AppendFormat("Pieces: {0}  Clusters: {1}  snapRadius: {2:F3}m",
                pieces.Length, CountClusters(pieces),
                snapSystem != null ? snapSystem.snapRadius : 0.08f);
            sb.AppendLine();

            if (snapSystem != null)
            {
                sb.AppendFormat("L-Hold: {0} ({1})  R-Hold: {2} ({3})",
                    snapSystem.leftHolder != null && snapSystem.leftHolder.IsHolding,
                    snapSystem.leftHolder != null && snapSystem.leftHolder.heldPiece != null
                        ? snapSystem.leftHolder.heldPiece.PieceId.ToString() : "-",
                    snapSystem.rightHolder != null && snapSystem.rightHolder.IsHolding,
                    snapSystem.rightHolder != null && snapSystem.rightHolder.heldPiece != null
                        ? snapSystem.rightHolder.heldPiece.PieceId.ToString() : "-");
                sb.AppendLine();
            }
            sb.AppendLine();

            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                if (p == null || p.transform == null) continue;
                sb.AppendFormat("[{0}] pos:{1} cluster:{2}",
                    p.PieceId,
                    p.transform.position.ToString("F2"),
                    p.ClusterId);
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("--- Pairs ---");

            for (int i = 0; i < pieces.Length; i++)
            {
                for (int j = i + 1; j < pieces.Length; j++)
                {
                    var a = pieces[i];
                    var b = pieces[j];
                    if (a == null || b == null || a.transform == null || b.transform == null) continue;

                    int pa = a.PieceId, pb = b.PieceId;
                    float rawDist = Vector3.Distance(a.transform.position, b.transform.position);
                    float rotDelta = Quaternion.Angle(a.transform.rotation, b.transform.rotation);

                    bool adjacent = snapSystem != null && snapSystem.AreAdjacent(pa, pb);
                    float errDist = 0f;
                    if (adjacent && snapSystem != null)
                        snapSystem.GetSnapError(pa, pb, out errDist, out _);

                    if (adjacent)
                    {
                        bool nearSnap = rawDist < (snapSystem != null ? snapSystem.snapRadius : 0.08f)
                            && errDist < (snapSystem != null ? snapSystem.snapRadius : 0.08f);
                        sb.AppendFormat("{0}-{1}  adj  raw:{2:F3}m  err:{3:F3}m  rot:{4:F1}deg{5}",
                            pa, pb, rawDist, errDist, rotDelta,
                            nearSnap ? "  <<< WOULD SNAP" : "");
                    }
                    else
                    {
                        sb.AppendFormat("{0}-{1}  ---  raw:{2:F3}m  rot:{3:F1}deg",
                            pa, pb, rawDist, rotDelta);
                    }
                    sb.AppendLine();
                }
            }

            if (listText != null)
                listText.text = sb.ToString();

            if (listText != null)
            {
                int lines = 0;
                for (int k = 0; k < sb.Length; k++)
                    if (sb[k] == '\n') lines++;
                float h = Mathf.Max((lines + 2) * lineHeight, lineHeight * 3, 0.03f);
                var rt = listText.rectTransform;
                rt.sizeDelta = new Vector2(panelWidth - 0.01f, h);
                rt.anchoredPosition = new Vector2(0, -h * 0.5f);
            }
        }
        catch (System.Exception e)
        {
            if (listText != null)
                listText.text = "Error: " + e.Message;
        }
    }

    private int CountClusters(PieceState[] pieces)
    {
        var ids = new System.Collections.Generic.HashSet<int>();
        foreach (var p in pieces)
            if (p != null) ids.Add(p.ClusterId);
        return ids.Count;
    }

    void CreateHUD()
    {
        var canvasGO = new GameObject("SnapDebugHUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(null);
        canvasGO.transform.position = Vector3.zero;
        canvasGO.transform.rotation = Quaternion.identity;

        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        var canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(0.6f, 0.5f);

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.sizeDelta = new Vector2(panelWidth, maxPanelHeight);
        panelRT.anchoredPosition = Vector2.zero;
        panelBg = panel.GetComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.55f);

        labelText = CreateTMP("LabelText", panel.transform, new Vector2(0, maxPanelHeight * 0.5f - 0.01f),
            panelWidth - 0.01f, 0.018f, 0.015f,
            "Snap Debug HUD (F1 to toggle)", TMPro.TextAlignmentOptions.Top);
        labelText.color = new Color(0.5f, 1f, 0.5f);

        listText = CreateTMP("ListText", panel.transform, new Vector2(0, -0.005f),
            panelWidth - 0.01f, 0.02f, fontSize,
            "Waiting for pieces...", TMPro.TextAlignmentOptions.TopLeft);
        listText.color = Color.white;

        hudReady = true;
    }

    void LateUpdate()
    {
        if (!hudReady || canvas == null) return;

        var cam = Camera.main;
        if (cam != null)
        {
            canvas.transform.position = cam.transform.position + cam.transform.forward * 0.7f
                + cam.transform.up * 0.25f;
            canvas.transform.rotation = cam.transform.rotation;
        }
    }

    private TMP_Text CreateTMP(string name, Transform parent, Vector2 anchoredPos, float width, float height,
        float size, string text, TMPro.TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, height);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.fontSize = size;
        tmp.isTextObjectScaleStatic = true;
        tmp.alignment = alignment;
        tmp.text = text;
        if (tmp.font == null)
            tmp.font = TMPro.TMP_Settings.defaultFontAsset;
        return tmp;
    }
}
