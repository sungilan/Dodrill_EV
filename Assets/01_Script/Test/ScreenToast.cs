using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  ScreenToast.cs
//
//  PC·모바일 화면용 알림 — WarningPopup(시선 앞 월드 카드)을 대신한다.
//  예전에는 PC 에서도 화면 한가운데에 큰 카드(가로 1000px 넘게)가 떠서 작업 대상·미니맵을 가렸고,
//  선임 정비사 대화창과 같은 말을 두 번 하는 느낌이었다(11/19 의견).
//  → 위쪽 가운데(고전압 상태 아래)에 작은 알림을 위에서 아래로 쌓는다. 최대 3장, 고전압·남은 시간 경고가 맨 위.
//  VR 은 그대로 WarningPopup 의 월드 카드를 쓴다.
// ============================================================
public class ScreenToast : MonoBehaviour
{
    private const int MaxCards = 3;
    private const float Width = 700f;
    private const float TopY = -186f;          // 고전압 상태(제목·아이콘·안내 한 줄) 바로 아래
    private const float CenterX = 0.5f;        // 화면 정가운데(고전압 상태와 같은 기준)
    private const float FadeTime = 0.25f;

    private class Card
    {
        public string key;
        public RectTransform rt;
        public CanvasGroup group;
        public Image back;
        public TMP_Text text;
        public float hideAt;
        public int order;
        public int Priority => key == "hv" ? 0 : key == "time" ? 1 : 2;
    }

    private static ScreenToast s_inst;
    private static int s_order;
    private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
    private readonly List<Card> _sorted = new List<Card>();
    private RectTransform _stack;

    /// <summary>화면용 알림을 띄운다(같은 key 는 하나만, 새 내용으로 바꾸고 시간을 늘린다).</summary>
    public static void Show(string key, string title, string message, Color tint, float seconds)
    {
        if (s_inst == null)
        {
            var go = new GameObject("[ScreenToast]");
            DontDestroyOnLoad(go);
            s_inst = go.AddComponent<ScreenToast>();
            s_inst.Build();
        }
        s_inst.Set(key, title, message, tint, seconds);
    }

    public static void Hide(string key)
    {
        if (s_inst != null && s_inst._cards.TryGetValue(key, out var c)) { Destroy(c.rt.gameObject); s_inst._cards.Remove(key); }
    }

    private void Build()
    {
        var cv = gameObject.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 67;
        var sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;

        _stack = NewRect("Stack", transform);
        _stack.anchorMin = _stack.anchorMax = new Vector2(CenterX, 1f); _stack.pivot = new Vector2(0.5f, 1f);
        _stack.anchoredPosition = new Vector2(0f, TopY); _stack.sizeDelta = new Vector2(Width, 10f);
        var vl = _stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f; vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        // 묶음 높이를 카드 높이 합에 맞춘다 — 없으면 묶음 높이(10)에 맞춰 카드가 최소 높이로 눌려 글씨가 카드 밖으로 넘쳤다
        var fit = _stack.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void Set(string key, string title, string message, Color tint, float seconds)
    {
        if (!_cards.TryGetValue(key, out var c))
        {
            c = new Card { key = key, order = ++s_order };
            c.rt = NewRect("Toast_" + key, _stack);
            c.group = c.rt.gameObject.AddComponent<CanvasGroup>();
            c.group.blocksRaycasts = false; c.group.interactable = false;
            c.back = c.rt.gameObject.AddComponent<Image>(); c.back.raycastTarget = false;
            c.back.sprite = WorldHoverLabel.RoundedSprite(); c.back.type = Image.Type.Sliced;
            var hl = c.rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(22, 22, 10, 12); hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = false;

            var trt = NewRect("Text", c.rt);
            c.text = trt.gameObject.AddComponent<TextMeshProUGUI>();
            var kf = MobileWorkButton.KoreanFont();
            if (kf != null) c.text.font = kf;
            c.text.fontSize = 22f; c.text.alignment = TextAlignmentOptions.Center; c.text.color = Color.white;
            c.text.richText = true; c.text.outlineWidth = 0.15f; c.text.outlineColor = new Color32(0, 0, 0, 200); c.text.textWrappingMode = TextWrappingModes.Normal; c.text.raycastTarget = false;
            _cards[key] = c;
            Trim(key);
        }
        // 기존 경고 색(빨강·주황·파랑·보라)을 어둡게 눌러 흰 글씨가 잘 읽히게, 거의 불투명하게
        c.back.color = new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f, 0.94f);
        c.text.text = string.IsNullOrEmpty(message)
            ? $"<b>{title}</b>"
            : $"<b>{title}</b>\n<size=80%><color=#FFE9C2>{message}</color></size>";
        c.hideAt = Time.unscaledTime + seconds;
        c.group.alpha = 1f;
        Reorder();
    }

    /// <summary>최대 장수를 넘으면 가장 오래된 일반 알림부터 닫는다(고전압·남은 시간은 남긴다).</summary>
    private void Trim(string keep)
    {
        while (_cards.Count > MaxCards)
        {
            Card oldest = null;
            foreach (var kv in _cards)
            {
                if (kv.Key == keep || kv.Key == "hv" || kv.Key == "time") continue;
                if (oldest == null || kv.Value.order < oldest.order) oldest = kv.Value;
            }
            if (oldest == null) return;
            Hide(oldest.key);
        }
    }

    private void Reorder()
    {
        _sorted.Clear();
        _sorted.AddRange(_cards.Values);
        _sorted.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.order.CompareTo(b.order));
        for (int i = 0; i < _sorted.Count; i++) _sorted[i].rt.SetSiblingIndex(i);
    }

    private readonly List<string> _expired = new List<string>();

    private void Update()
    {
        float now = Time.unscaledTime;
        _expired.Clear();
        foreach (var kv in _cards)
        {
            float left = kv.Value.hideAt - now;
            if (left <= 0f) _expired.Add(kv.Key);
            else if (left < FadeTime) kv.Value.group.alpha = left / FadeTime;
        }
        foreach (var k in _expired) Hide(k);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }
}
