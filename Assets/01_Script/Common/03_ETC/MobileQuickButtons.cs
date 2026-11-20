using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모바일 상황 버튼(내려놓기, 소켓 바꾸기 등) — 오른쪽 아래 '작업' 버튼 주변에 필요할 때만 띄운다.
/// 씬·프리팹 수정 없이 실행 중에 오버레이 캔버스를 만든다. 매 프레임 Set(…, visible) 로 켜고 끄면 된다.
/// </summary>
public static class MobileQuickButtons
{
    private static GameObject _root;
    private static readonly Dictionary<string, Entry> _btns = new Dictionary<string, Entry>();

    private class Entry
    {
        public GameObject go;
        public TMPro.TextMeshProUGUI label;
        public Action onClick;
    }

    // 자리(오른쪽 아래 기준, 1920×1080): 작업 버튼(-240,200 / 230×230) 아래·왼쪽
    public static readonly Vector2 SlotDrop = new Vector2(-240f, 60f);
    public static readonly Vector2 SlotLeft = new Vector2(-490f, 260f);
    public static readonly Vector2 SlotUpper = new Vector2(-240f, 450f);

    public static void Set(string id, bool visible, string text = null, Vector2 slot = default, Action onClick = null)
    {
        if (!visible)
        {
            if (_btns.TryGetValue(id, out var e0) && e0.go != null && e0.go.activeSelf) e0.go.SetActive(false);
            return;
        }
        Ensure();
        if (!_btns.TryGetValue(id, out var e) || e.go == null)
        {
            e = Make(id, slot);
            _btns[id] = e;
        }
        e.onClick = onClick;
        if (text != null && e.label.text != text) e.label.text = text;
        if (!e.go.activeSelf) e.go.SetActive(true);
    }

    public static void HideAll()
    {
        foreach (var e in _btns.Values) if (e.go != null) e.go.SetActive(false);
    }

    private static void Ensure()
    {
        if (_root != null) return;
        _btns.Clear();
        _root = new GameObject("[MobileQuickButtons]");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 58;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _root.AddComponent<GraphicRaycaster>();
    }

    private static Entry Make(string id, Vector2 slot)
    {
        var e = new Entry();
        var go = new GameObject(id, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_root.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = slot;
        rt.sizeDelta = new Vector2(230f, 110f);
        var img = go.GetComponent<Image>();
        var normal = new Color(0.12f, 0.16f, 0.24f, 0.88f);
        var pressed = new Color(0.10f, 0.45f, 0.85f, 0.95f);
        img.color = normal;

        var lg = new GameObject("Label", typeof(RectTransform));
        lg.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)lg.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8f, 4f); lrt.offsetMax = new Vector2(-8f, -4f);
        var t = lg.AddComponent<TMPro.TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont(); if (kf != null) t.font = kf;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontSize = 34;
        t.enableAutoSizing = true; t.fontSizeMin = 20f; t.fontSizeMax = 34f;
        t.fontStyle = TMPro.FontStyles.Bold;
        t.color = Color.white;
        t.raycastTarget = false;

        var trigger = go.AddComponent<EventTrigger>();
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerDown, () => img.color = pressed);
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerUp, () => img.color = normal);
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerClick, () => e.onClick?.Invoke());

        e.go = go; e.label = t;
        return e;
    }
}
