using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  StepPopupToggle.cs
//
//  PC·모바일 화면 왼쪽 위(단계 안내 패널 아래)에 '단계 팝업 켜짐/꺼짐' 작은 버튼을 둔다.
//  단계가 시작될 때마다 뜨는 가운데 설명 창(GuideSystem)을 끄고 켠다. 설정은 기기에 저장된다(GuideSystem.CenterPopupEnabled).
//  VR 은 손바닥 메뉴(VrPalmMenu)의 '단계 팝업' 버튼을 쓴다. 게임 씬에서만(GuideSystem 이 있을 때) 나타난다.
//  씬에 따로 둘 필요 없이 시작할 때 스스로 붙는다.
// ============================================================
public class StepPopupToggle : MonoBehaviour
{
    private GameObject _canvas;
    private TMP_Text _label;
    private float _nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<StepPopupToggle>() != null) return;
        var go = new GameObject("[StepPopupToggle]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<StepPopupToggle>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck) { RefreshLabel(); return; }
        _nextCheck = Time.unscaledTime + 1f;
        bool want = GuideSystem.Instance != null && Utils.GetPlatformType() != PlatformType.VR;
        if (want && _canvas == null) Build();
        else if (!want && _canvas != null) { Destroy(_canvas); _canvas = null; }
        RefreshLabel();
    }

    private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }

    private void Build()
    {
        _canvas = new GameObject("[StepPopupToggle_Canvas]");
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 40;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        var btnGo = new GameObject("Button");
        btnGo.transform.SetParent(_canvas.transform, false);
        var rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);   // 왼쪽 위
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(14f, -250f);        // 단계 안내 패널과 미니맵 사이
        rt.sizeDelta = new Vector2(250f, 40f);
        var img = btnGo.AddComponent<Image>();
        img.color = new Color(0.1f, 0.14f, 0.2f, 0.85f);
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => GuideSystem.SetCenterPopupEnabled(!GuideSystem.CenterPopupEnabled));

        var tGo = new GameObject("Text");
        tGo.transform.SetParent(btnGo.transform, false);
        var trt = tGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        _label = tGo.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) _label.font = kf;
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 22f;
        _label.color = Color.white;
        _label.raycastTarget = false;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (_label == null) return;
        string s = GuideSystem.CenterPopupEnabled ? "단계 설명 팝업: 켜짐" : "단계 설명 팝업: 꺼짐";
        if (_label.text != s) _label.text = s;
    }
}
