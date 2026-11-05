using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모바일 '높낮이' 버튼(서기/앉기) — 차 아래 볼트처럼 낮은 곳을 볼 때 PC 의 Q/E 에 해당 (공군 앱의 높낮이 버튼과 같은 쓰임).
/// 누르고 있는 동안 높이가 바뀐다. 씬·프리팹 수정 없이 실행 중에 오버레이 캔버스로 만든다.
/// </summary>
public static class MobileHeightButtons
{
    private static GameObject _root;

    public static void Show(FreeLockControllerMobile controller)
    {
        Hide();
        if (controller == null) return;

        _root = new GameObject("[MobileHeightButtons]");
        Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _root.AddComponent<GraphicRaycaster>();

        MakeButton("Up", "서기", new Vector2(-70f, 190f), controller.MoveUp, controller.StopMove);
        MakeButton("Down", "앉기", new Vector2(-70f, 60f), controller.MoveDown, controller.StopMove);
    }

    public static void Hide()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
    }

    private static void MakeButton(string name, string label, Vector2 pos, System.Action down, System.Action up)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_root.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(110f, 110f);

        var img = go.GetComponent<Image>();
        var normal = new Color(0.08f, 0.12f, 0.2f, 0.78f);
        var pressed = new Color(0.10f, 0.45f, 0.85f, 0.95f);
        img.sprite = MobileWorkButton.MakeCircle();
        img.color = normal;

        var lg = new GameObject("Label", typeof(RectTransform));
        lg.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)lg.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var t = lg.AddComponent<TMPro.TextMeshProUGUI>();
        t.text = label;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontSize = 34;
        t.fontStyle = TMPro.FontStyles.Bold;
        t.color = Color.white;
        t.raycastTarget = false;

        var trigger = go.AddComponent<EventTrigger>();
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerDown, () => { img.color = pressed; down?.Invoke(); });
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerUp, () => { img.color = normal; up?.Invoke(); });
        MobileWorkButton.AddEntry(trigger, EventTriggerType.PointerExit, () => { img.color = normal; up?.Invoke(); });
    }
}
