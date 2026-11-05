using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모바일용 '작업' 버튼 (누르고 있는 동안 IsHeld). PC 의 F 키에 해당한다.
/// 임팩트 렌치처럼 키를 계속 눌러야 하는 도구를 모바일에서도 쓰기 위해, 렌치가 볼트에 닿았을 때만 화면에 나타난다.
/// 씬·프리팹 수정 없이 실행 중에 오버레이 캔버스를 만든다.
/// </summary>
public static class MobileWorkButton
{
    public static bool IsHeld { get; private set; }

    private static GameObject _root;

    public static void SetVisible(bool visible)
    {
        if (visible) Ensure();
        if (_root != null) _root.SetActive(visible);
        if (!visible) IsHeld = false;
    }

    private static void Ensure()
    {
        if (_root != null) return;

        _root = new GameObject("[MobileWorkButton]");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _root.AddComponent<GraphicRaycaster>();

        var btn = new GameObject("Button", typeof(RectTransform), typeof(Image));
        btn.transform.SetParent(_root.transform, false);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-240f, 200f);
        rt.sizeDelta = new Vector2(230f, 230f);

        var img = btn.GetComponent<Image>();
        img.color = new Color(0.10f, 0.45f, 0.85f, 0.88f);
        img.sprite = MakeCircle();

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(btn.transform, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
        var kf = KoreanFont(); if (kf != null) label.font = kf;
        label.text = "작업\n<size=60%>누르고 있기</size>";
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.fontSize = 56;
        label.fontStyle = TMPro.FontStyles.Bold;
        label.color = Color.white;
        label.raycastTarget = false;

        var trigger = btn.AddComponent<EventTrigger>();
        AddEntry(trigger, EventTriggerType.PointerDown, () => { IsHeld = true; img.color = new Color(0.95f, 0.55f, 0.10f, 0.95f); });
        AddEntry(trigger, EventTriggerType.PointerUp, () => { IsHeld = false; img.color = new Color(0.10f, 0.45f, 0.85f, 0.88f); });
        AddEntry(trigger, EventTriggerType.PointerExit, () => { IsHeld = false; img.color = new Color(0.10f, 0.45f, 0.85f, 0.88f); });
    }

    private static TMPro.TMP_FontAsset _font;

    /// <summary>
    /// 한글이 있는 TMP 글꼴. 코드로 만든 글자는 기본 글꼴(한글 없음)이라 ‘서기/앉기/작업’ 이 □□ 로 나왔다
    /// → 프로젝트 UI 가 쓰는 Pretendard 를 찾아 쓴다.
    /// </summary>
    internal static TMPro.TMP_FontAsset KoreanFont()
    {
        if (_font != null) return _font;
        foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
            if (f != null && f.name.IndexOf("Pretendard", System.StringComparison.OrdinalIgnoreCase) >= 0) { _font = f; break; }
        if (_font == null)
            foreach (var t in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.font != null && t.text != null && System.Text.RegularExpressions.Regex.IsMatch(t.text, "[가-힣]")) { _font = t.font; break; }
        if (_font == null) _font = TMPro.TMP_Settings.defaultFontAsset;
        return _font;
    }

    internal static void AddEntry(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    internal static Sprite MakeCircle()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01((r - 1.5f - d) * 1.0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}
