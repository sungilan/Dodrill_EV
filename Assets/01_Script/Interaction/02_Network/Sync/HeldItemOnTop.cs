using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  HeldItemOnTop.cs — 든 도구를 화면 맨 앞에 그리는 표시(집기가 쓰는 정적 도우미). 모듈화 2단계(2026-11-20)에 HeldItemOnTopFeature.cs 에서 분리.
//  렌더러 기능(HeldItemOnTopFeature·XRayPass)은 엑스레이(EV)를 알기 때문에 EV 쪽으로 옮기고, 이 표시 도우미만 인터랙션 층에 둔다.
// ============================================================
/// <summary>렌더러에 '맨 앞에 그리기' 표시를 켜고 끈다. 같은 물체를 여러 번 켜도 한 번으로 센다.</summary>
public static class HeldItemOnTop
{
    /// <summary>
    /// renderingLayerMask 에서 쓰는 비트. ProjectSettings(Tags and Layers → Rendering Layers) 1번에 'HeldItemOnTop' 으로 등록해 둔 칸이다.
    /// 예전에는 등록하지 않은 29번 비트를 썼는데, 정의되지 않은 렌더링 레이어는 걸러질 수 있어 메거가 앞에 그려지지 않았다.
    /// 조명은 Default 비트로 맞으므로 이 비트를 더해도 밝기는 그대로다.
    /// </summary>
    public const uint Bit = 1u << 1;

    /// <summary>
    /// 든 도구의 월드 UI(LCD 등)를 잠시 옮겨 두는 레이어. 이름 없는 빈 레이어를 쓴다(ProjectSettings 에 'HeldItemUI' 로 이름만 붙임).
    /// UI 에는 물리 충돌체가 없어서 레이어를 바꿔도 판정에 영향이 없다.
    /// </summary>
    public const int UILayer = 29;

    private static readonly HashSet<int> s_active = new HashSet<int>();
    private static readonly Dictionary<GameObject, int> s_uiOriginalLayer = new Dictionary<GameObject, int>();

    public static bool Any => s_active.Count > 0;

    public static void Set(GameObject root, bool on)
    {
        if (root == null) return;
        int id = root.GetInstanceID();
        if (on) s_active.Add(id); else s_active.Remove(id);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) Set(r, on);

        // 월드 공간 Canvas 와 그 아래 UI 를 전용 레이어로 옮기거나 되돌린다
        foreach (var c in root.GetComponentsInChildren<Canvas>(true))
        {
            if (c.renderMode != RenderMode.WorldSpace) continue;
            foreach (var t in c.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (on)
                {
                    if (!s_uiOriginalLayer.ContainsKey(go)) s_uiOriginalLayer[go] = go.layer;
                    go.layer = UILayer;
                }
                else if (s_uiOriginalLayer.TryGetValue(go, out int layer))
                {
                    go.layer = layer;
                    s_uiOriginalLayer.Remove(go);
                }
            }
        }
    }

    public static void Set(Renderer r, bool on)
    {
        if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return;
        r.renderingLayerMask = on ? (r.renderingLayerMask | Bit) : (r.renderingLayerMask & ~Bit);
    }

    public static void Register(Object key, bool on)
    {
        if (key == null) return;
        if (on) s_active.Add(key.GetInstanceID()); else s_active.Remove(key.GetInstanceID());
    }
}
