using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DoDrill.Training;

// ============================================================
//  TaskProgressUISetup.cs  —  Editor 전용
//  메뉴: DoDrill > UI > Create Task Progress UI
//
//  생성 구조:
//    [TaskProgressUI]                   ← Canvas (Screen Space - Overlay)
//    └── Panel                          ← 반투명 패널 (우측 하단 고정)
//        ├── ProgressText               ← "1 / 8"  (Bold)
//        ├── StatusText                 ← "진행 중" (Medium, 색상)
//        ├── ModuleText                 ← "HandHygiene" (Light, 소형)
//        ├── DescriptionText            ← "손위생을 실시한다" (SemiBold)
//        └── ConfirmButton              ← 완료 버튼 (Bold)
//
//  글꼴: Pretendard (Bold / SemiBold / Medium / Light)
// ============================================================

public static class TaskProgressUISetup
{
    // ── 폰트 경로 ──────────────────────────────────────────────
    private const string FontBold     = "Assets/05_Font/Pretendard-Bold SDF.asset";
    private const string FontSemiBold = "Assets/05_Font/Pretendard-SemiBold SDF.asset";
    private const string FontMedium   = "Assets/05_Font/Pretendard-Medium SDF.asset";
    private const string FontLight    = "Assets/05_Font/Pretendard-Light SDF.asset";

    // ── 색상 팔레트 ────────────────────────────────────────────
    private static readonly Color PanelBg       = new Color(0.05f, 0.07f, 0.12f, 0.92f);
    private static readonly Color AccentBlue    = new Color(0.22f, 0.60f, 1.00f, 1.00f);
    private static readonly Color AccentGreen   = new Color(0.20f, 0.85f, 0.55f, 1.00f);
    private static readonly Color TextPrimary   = new Color(0.95f, 0.95f, 0.97f, 1.00f);
    private static readonly Color TextSecondary = new Color(0.60f, 0.65f, 0.75f, 1.00f);
    private static readonly Color DividerColor  = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color BtnNormal     = new Color(0.22f, 0.60f, 1.00f, 1.00f);
    private static readonly Color BtnHighlight  = new Color(0.35f, 0.72f, 1.00f, 1.00f);
    private static readonly Color BtnPressed    = new Color(0.15f, 0.48f, 0.85f, 1.00f);

    // ── 패널 크기 ──────────────────────────────────────────────
    private const float PanelW = 360f;
    private const float PanelH = 240f;
    private const float Pad    = 20f;
    private const float Gap    = 10f;

    // ──────────────────────────────────────────────────────────
    [MenuItem("DoDrill/UI/Create Task Progress UI", priority = 200)]
    public static void CreateTaskProgressUI()
    {
        // 이미 존재하면 확인
        var existing = GameObject.Find("[TaskProgressUI]");
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "TaskProgressUI 재설정",
                "[TaskProgressUI]가 이미 씬에 있습니다.\n삭제하고 다시 만드시겠습니까?",
                "재생성", "취소");
            if (!replace) return;
            Undo.DestroyObjectImmediate(existing);
        }

        // 폰트 로드
        var fBold     = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBold);
        var fSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSemiBold);
        var fMedium   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMedium);
        var fLight    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontLight);

        if (fBold == null || fMedium == null)
        {
            EditorUtility.DisplayDialog("폰트 없음",
                $"Pretendard 폰트를 찾을 수 없습니다.\n경로: Assets/05_Font/",
                "확인");
            return;
        }

        // ── Canvas ─────────────────────────────────────────────
        var canvasGO = new GameObject("[TaskProgressUI]");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create TaskProgressUI");

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode            = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution    = new Vector2(1920f, 1080f);
        scaler.screenMatchMode        = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight     = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Panel ──────────────────────────────────────────────
        var panel = CreateGO("Panel", canvasGO.transform);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = PanelBg;

        var panelRT = panel.GetComponent<RectTransform>();
        // 우측 하단 고정
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot     = new Vector2(1f, 0f);
        panelRT.anchoredPosition = new Vector2(-24f, 24f);
        panelRT.sizeDelta        = new Vector2(PanelW, PanelH);

        // 모서리 둥글게 (Image type = Sliced → 런타임에서 처리)
        panelImg.raycastTarget = true;

        // ── 내부 레이아웃 (수직 ─────────────────────────────────
        // [Header Row]  ProgressText | StatusText
        // [ModuleText]
        // [Divider]
        // [DescriptionText]
        // [ConfirmButton]

        float curY = -Pad;

        // ── Header Row ─────────────────────────────────────────
        var headerRow = CreateGO("HeaderRow", panel.transform);
        var hrRT      = headerRow.GetComponent<RectTransform>();
        SetAnchored(hrRT, new Vector2(Pad, curY), new Vector2(PanelW - Pad * 2, 28f));
        hrRT.anchorMin = new Vector2(0f, 1f);
        hrRT.anchorMax = new Vector2(0f, 1f);
        hrRT.pivot     = new Vector2(0f, 1f);

        var progressText  = CreateText("ProgressText", headerRow.transform,
            "1 / 8", fBold ?? fMedium, 18f, TextPrimary);
        var ptRT = progressText.GetComponent<RectTransform>();
        ptRT.anchorMin = new Vector2(0f, 0f); ptRT.anchorMax = new Vector2(0f, 1f);
        ptRT.pivot     = new Vector2(0f, 0.5f);
        ptRT.offsetMin = Vector2.zero; ptRT.offsetMax = new Vector2(80f, 0f);
        progressText.alignment = TextAlignmentOptions.MidlineLeft;

        var statusText = CreateText("StatusText", headerRow.transform,
            "진행 중", fMedium, 15f, AccentGreen);
        var stRT = statusText.GetComponent<RectTransform>();
        stRT.anchorMin = new Vector2(1f, 0f); stRT.anchorMax = new Vector2(1f, 1f);
        stRT.pivot     = new Vector2(1f, 0.5f);
        stRT.offsetMin = new Vector2(-120f, 0f); stRT.offsetMax = Vector2.zero;
        statusText.alignment = TextAlignmentOptions.MidlineRight;

        curY -= 28f + Gap;

        // ── ModuleText ─────────────────────────────────────────
        var moduleText = CreateText("ModuleText", panel.transform,
            "HandHygiene", fLight ?? fMedium, 13f, TextSecondary);
        PlaceText(moduleText.GetComponent<RectTransform>(), panel, Pad, curY, PanelW - Pad * 2, 20f);
        moduleText.alignment = TextAlignmentOptions.MidlineLeft;
        curY -= 20f + Gap * 0.5f;

        // ── Divider ────────────────────────────────────────────
        var divider = CreateGO("Divider", panel.transform);
        var divImg  = divider.AddComponent<Image>();
        divImg.color = DividerColor;
        var divRT   = divider.GetComponent<RectTransform>();
        divRT.anchorMin = new Vector2(0f, 1f);
        divRT.anchorMax = new Vector2(0f, 1f);
        divRT.pivot     = new Vector2(0f, 1f);
        divRT.anchoredPosition = new Vector2(Pad, curY);
        divRT.sizeDelta        = new Vector2(PanelW - Pad * 2, 1f);
        curY -= 1f + Gap;

        // ── DescriptionText ────────────────────────────────────
        var descText = CreateText("DescriptionText", panel.transform,
            "현재 수행할 Task 설명이 표시됩니다", fSemiBold ?? fMedium, 16f, TextPrimary);
        var dtRT = descText.GetComponent<RectTransform>();
        dtRT.anchorMin = new Vector2(0f, 1f);
        dtRT.anchorMax = new Vector2(0f, 1f);
        dtRT.pivot     = new Vector2(0f, 1f);
        dtRT.anchoredPosition = new Vector2(Pad, curY);
        dtRT.sizeDelta        = new Vector2(PanelW - Pad * 2, 52f);
        descText.alignment    = TextAlignmentOptions.TopLeft;
        descText.enableWordWrapping = true;
        curY -= 52f + Gap;

        // ── ConfirmButton ──────────────────────────────────────
        var btnGO  = CreateGO("ConfirmButton", panel.transform);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = BtnNormal;

        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0f, 1f);
        btnRT.anchorMax = new Vector2(0f, 1f);
        btnRT.pivot     = new Vector2(0f, 1f);
        btnRT.anchoredPosition = new Vector2(Pad, curY);
        btnRT.sizeDelta        = new Vector2(PanelW - Pad * 2, 44f);

        var btn = btnGO.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = BtnNormal;
        colors.highlightedColor = BtnHighlight;
        colors.pressedColor     = BtnPressed;
        colors.disabledColor    = new Color(0.3f, 0.3f, 0.35f, 0.6f);
        colors.fadeDuration     = 0.1f;
        btn.colors = colors;

        var btnLabel = CreateText("Label", btnGO.transform,
            "완료", fBold ?? fMedium, 17f, Color.white);
        var blRT = btnLabel.GetComponent<RectTransform>();
        blRT.anchorMin = Vector2.zero;
        blRT.anchorMax = Vector2.one;
        blRT.offsetMin = Vector2.zero;
        blRT.offsetMax = Vector2.zero;
        btnLabel.alignment = TextAlignmentOptions.Center;

        // ── TaskProgressUI 컴포넌트 연결 ───────────────────────
        var uiComp = canvasGO.AddComponent<TaskProgressUI>();

        // SerializedObject로 private [SerializeField] 필드에 값 할당
        var so = new SerializedObject(uiComp);
        so.FindProperty("_panel")           .objectReferenceValue = panel;
        so.FindProperty("_progressText")    .objectReferenceValue = progressText;
        so.FindProperty("_moduleText")      .objectReferenceValue = moduleText;
        so.FindProperty("_descriptionText") .objectReferenceValue = descText;
        so.FindProperty("_statusText")      .objectReferenceValue = statusText;
        so.FindProperty("_confirmButton")   .objectReferenceValue = btn;
        so.FindProperty("_confirmBtnLabel") .objectReferenceValue = btnLabel;
        so.ApplyModifiedProperties();

        // ── EventSystem 보장 ───────────────────────────────────
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
        }

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = canvasGO;
        Debug.Log("[TaskProgressUISetup] [TaskProgressUI] 생성 완료");
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private static GameObject CreateGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent,
        string text, TMP_FontAsset font, float size, Color color)
    {
        var go  = CreateGO(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = text;
        tmp.font     = font;
        tmp.fontSize = size;
        tmp.color    = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>anchorMin=(0,1) 기준 절대 좌표 배치 (패널 내부)</summary>
    private static void PlaceText(RectTransform rt, GameObject panel,
        float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(w, h);
    }

    private static void SetAnchored(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
    }
}
