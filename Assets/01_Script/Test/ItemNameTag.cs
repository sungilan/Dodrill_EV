using UnityEngine;

/// <summary>
/// 조준한 물건 위에 이름표를 띄우는 공용 도우미.
///  · PC·모바일: 호버 라벨을 맞은 지점이 아니라 물건 머리 위에 두고, 아래에서 위로 스르륵 올라오게 한다(<see cref="LabelAnchor"/>).
///  · VR: 손 레이저(AutoHand 거리 잡기)가 물건에 닿으면 <see cref="Show"/>, 벗어나면 <see cref="Hide"/>.
/// 라벨 모양은 PC 호버 라벨(WorldHoverLabel 프리팹)과 같다. VR 에서는 Resources/ItemNameTagLabel 로 불러온다.
/// </summary>
public static class ItemNameTag
{
    /// <summary>물건 머리 위 여유(m).</summary>
    public const float TopMargin = 0.08f;
    /// <summary>나타날 때 이만큼 아래에서 시작해 올라온다(m).</summary>
    public const float RiseFrom = 0.08f;

    /// <summary>
    /// 라벨을 둘 자리. 잡을 수 있는 물건(TaskItem/SyncGrab)이면 물건 머리 위, 그 밖에는 맞은 지점 위.
    /// </summary>
    public static Vector3 LabelAnchor(Collider col, Vector3 hitPoint, float fallbackOffset)
    {
        if (col != null)
        {
            Component item = col.GetComponentInParent<SyncGrab>();
            if (item == null) item = col.GetComponentInParent<TaskItem>();
            if (item != null && TryTop(item.transform, out var top)) return top;
        }
        return hitPoint + Vector3.up * fallbackOffset;
    }

    /// <summary>물건(렌더러 기준) 머리 위 중앙.</summary>
    public static bool TryTop(Transform root, out Vector3 top)
    {
        top = default;
        if (root == null) return false;
        bool any = false;
        Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (r.GetComponentInParent<Canvas>() != null) continue;   // 도구에 붙은 LCD 등 UI 는 제외
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return false;
        top = new Vector3(b.center.x, b.max.y + TopMargin, b.center.z);
        return true;
    }

    // ── VR 용: 한 번에 하나(손 레이저가 가리키는 물건) ─────────────────
    private static GameObject s_prefab;
    private static GameObject s_label;
    private static Transform s_target;

    public static void Show(Transform target, string prefabIdOrName)
    {
        if (target == null) return;
        if (s_target == target && s_label != null) return;
        Hide();

        if (s_prefab == null) s_prefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (s_prefab == null) return;
        if (!TryTop(target, out var top)) top = target.position + Vector3.up * 0.2f;

        s_label = Object.Instantiate(s_prefab, top - Vector3.up * RiseFrom, Quaternion.identity);
        SetLayer(s_label, 2);   // Ignore Raycast — 라벨이 손 레이저를 막지 않게
        var label = s_label.GetComponent<WorldHoverLabel>();
        if (label != null) label.SetText(prefabIdOrName, null);
        s_label.AddComponent<UIHoverFader>();
        s_label.AddComponent<Follow>().target = target;
        s_target = target;
    }

    public static void Hide(Transform target = null)
    {
        if (target != null && target != s_target) return;
        if (s_label != null)
        {
            var f = s_label.GetComponent<UIHoverFader>();
            if (f != null) f.FadeOut(); else Object.Destroy(s_label);
        }
        s_label = null;
        s_target = null;
    }

    private static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    /// <summary>물건 머리 위를 따라가며, 처음에는 아래에서 위로 올라온다.</summary>
    private class Follow : MonoBehaviour
    {
        public Transform target;
        private void LateUpdate()
        {
            if (target == null) return;
            if (!TryTop(target, out var top)) return;
            transform.position = Vector3.Lerp(transform.position, top, Time.deltaTime * 10f);
        }
    }
}
