using UnityEngine;

// ============================================================
//  WarningPopup.cs
//
//  시선 앞에 잠깐 뜨는 경고 카드(빨강·주황). 남은 시간 경고(TaskTimeWarning)와 고전압 접촉 경고(HvTouchWarning)가 쓴다.
//  이름표와 같은 라벨 프리팹(Resources/ItemNameTagLabel)을 쓰고, 차체·손에 가리지 않게 맨 앞에 그린다.
//  머리를 돌려도 시선 앞에 부드럽게 따라온다. 같은 종류(key)는 하나만 띄우고 새 내용으로 바꾼다.
// ============================================================
public class WarningPopup : MonoBehaviour
{
    private const float Distance = 0.9f;
    private const float DropBelowEye = 0.12f;

    private static GameObject s_prefab;
    private static readonly System.Collections.Generic.Dictionary<string, WarningPopup> s_active = new System.Collections.Generic.Dictionary<string, WarningPopup>();

    private string _key;
    private float _hideAt;
    private Vector3 _offsetUp;
    private int _slot;
    private int _order;
    private static int s_order;
    private const int MaxCards = 3;
    private const float Gap = 0.012f;

    /// <summary>지금 화면에 경고 카드가 하나라도 떠 있는가(장비 이름표 등이 겹치지 않게 잠시 숨기는 데 쓴다).</summary>
    public static bool AnyActive
    {
        get { foreach (var kv in s_active) if (kv.Value != null) return true; return false; }
    }

    private static bool IsSlotUsed(int slot)
    {
        foreach (var kv in s_active) if (kv.Value != null && kv.Value._slot == slot) return true;
        return false;
    }

    /// <summary>시선 앞에 경고 카드를 띄운다. tint = 카드 배경색, slot = 같은 시간에 둘 이상 뜰 때 위아래 칸(0=기본, 1=한 칸 위).</summary>
    public static void Show(string key, string title, string message, Color tint, float seconds, int slot = -1)
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (!s_active.TryGetValue(key, out var p) || p == null)
        {
            // slot 을 안 정하면 비어 있는 가장 낮은 칸에 띄운다 — 남은 시간 경고·엑스레이 안내 등이 같은 자리에 겹쳐 글자가 뒤섞이던 문제
            if (slot < 0)
            {
                slot = 0;
                while (IsSlotUsed(slot)) slot++;
            }
            if (s_prefab == null) s_prefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (s_prefab == null) return;
            var go = Instantiate(s_prefab);
            go.name = "[WarningPopup] " + key;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
            p = go.AddComponent<WarningPopup>();
            p._key = key;
            p._slot = slot;
            p._order = ++s_order;
            s_active[key] = p;
            TrimOldest(key);
            p._offsetUp = Vector3.up * (0.11f * slot);
            p.transform.position = p.Target(cam.transform);
        }
        else if (slot >= 0) { p._slot = slot; }
        p._offsetUp = Vector3.up * (0.11f * Mathf.Max(0, p._slot));
        p._hideAt = Time.unscaledTime + seconds;

        var label = p.GetComponent<WorldHoverLabel>();
        if (label != null)
        {
            label.SetText(title, message);   // 카드 크기·모양·맨 앞 그리기
            if (label.labelText != null)
                label.labelText.text = $"<b>{title}</b>\n<size=72%><color=#FFE2A8>{message}</color></size>";
            var img = label.background != null ? label.background : p.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (img != null) img.color = tint;
        }
    }

    /// <summary>
    /// (11/13) 카드가 한꺼번에 여러 장(남은 시간·장비 안내·측정 기록…) 뜨면 0.11m 고정 간격으로 쌓여 두 줄 카드끼리 겹치고,
    /// 아래 카드가 먼저 사라져도 빈칸이 남았다 → 매 프레임 실제 카드 높이로 위에서부터 차례로 쌓는다(빈칸 없이).
    /// 동시에 MaxCards 장까지만 — 넘치면 가장 오래된 일반 카드부터 닫는다(고전압·남은 시간 경고는 남긴다).
    /// </summary>
    private static void TrimOldest(string keep)
    {
        while (true)
        {
            int n = 0; WarningPopup oldest = null;
            foreach (var kv in s_active)
            {
                if (kv.Value == null) continue;
                n++;
                if (kv.Key == keep || kv.Key == "hv" || kv.Key == "time") continue;
                if (oldest == null || kv.Value._order < oldest._order) oldest = kv.Value;
            }
            if (n <= MaxCards || oldest == null) return;
            Hide(oldest._key);
        }
    }

    private static int s_layoutFrame = -1;
    private static readonly System.Collections.Generic.List<WarningPopup> s_sorted = new System.Collections.Generic.List<WarningPopup>();

    private static void Layout()
    {
        if (s_layoutFrame == Time.frameCount) return;
        s_layoutFrame = Time.frameCount;
        s_sorted.Clear();
        foreach (var kv in s_active) if (kv.Value != null) s_sorted.Add(kv.Value);
        // 고전압(slot 1 로 부르던 것)이 맨 위, 그다음 남은 시간, 나머지는 먼저 뜬 순서
        s_sorted.Sort((a, b) =>
        {
            int pa = a.Priority, pb = b.Priority;
            return pa != pb ? pa.CompareTo(pb) : a._order.CompareTo(b._order);
        });
        float y = 0f;
        for (int i = 0; i < s_sorted.Count; i++)
        {
            var p = s_sorted[i];
            float h = p.Height();
            if (i > 0) y -= h * 0.5f;
            p._offsetUp = Vector3.up * y;
            y -= h * 0.5f + Gap;
        }
    }

    private int Priority => _key == "hv" ? 0 : _key == "time" ? 1 : 2;

    private float Height()
    {
        var c = GetComponentInChildren<Canvas>(true);
        if (c == null) return 0.1f;
        var rt = (RectTransform)c.transform;
        return Mathf.Clamp(rt.rect.height * rt.lossyScale.y, 0.03f, 0.25f);
    }

    public static void Hide(string key)
    {
        if (s_active.TryGetValue(key, out var p) && p != null) Destroy(p.gameObject);
        s_active.Remove(key);
    }

    private Vector3 Target(Transform cam)
    {
        Vector3 fwd = cam.forward;
        return cam.position + fwd * Distance - Vector3.up * DropBelowEye + _offsetUp;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= _hideAt) { Hide(_key); return; }
        var cam = Camera.main;
        if (cam == null) return;
        Layout();
        transform.position = Vector3.Lerp(transform.position, Target(cam.transform), Time.unscaledDeltaTime * 6f);
    }

    private void OnDestroy()
    {
        if (_key != null && s_active.TryGetValue(_key, out var p) && p == this) s_active.Remove(_key);
    }
}
