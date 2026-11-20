using UnityEngine;

// ============================================================
//  ClickRay.cs — PC·모바일 클릭/호버 레이캐스트 (2026-11-20)
//
//  문제: 첫 번째로 맞은 물체만 보았기 때문에, 멀리 있는 후드 래치(상자 콜라이더가 후드 전체)나 지금 단계에서 못 누르는 문·커넥터가
//  시선 앞쪽·뒤쪽에서 클릭을 가로채거나 눌렸다(접지 케이블을 누르려는데 뒤의 후드 래치가 눌림).
//  해결: 맞은 것을 가까운 순으로 보되, ClickableAnimator(문·후드·커넥터 등)는
//    · 지금 단계에서 누를 수 없으면(CanInteract 가 false) 건너뛰고,
//    · 너무 멀리(FarClickLimit 초과) 있으면 건너뛴다 — 멀리 있는 것은 누르지도, 시선을 막지도 않는다.
//  그 밖의 물체(볼트·부품·도구 등)는 예전과 똑같이 맞는다.
// ============================================================
public static class ClickRay
{
    /// <summary>ClickableAnimator(문·후드·커넥터·레버)를 누를 수 있는 최대 거리(m)</summary>
    public const float FarClickLimit = 3.0f;

    private static readonly RaycastHit[] s_hits = new RaycastHit[48];

    public static bool Cast(Ray ray, out RaycastHit hit, float maxDistance, int layerMask = Physics.DefaultRaycastLayers)
    {
        int n = Physics.RaycastNonAlloc(ray, s_hits, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = s_hits[i];
            if (h.collider == null || h.distance >= best) continue;
            var anim = h.collider.GetComponent<ClickableAnimator>() ?? h.collider.GetComponentInParent<ClickableAnimator>();
            if (anim != null && (h.distance > FarClickLimit || !anim.CanInteract())) continue;
            best = h.distance;
            hit = h;
            found = true;
        }
        return found;
    }
}
