using UnityEngine;

/// <summary>
/// PC·모바일 플레이어의 이동 충돌.
///
/// 두 컨트롤러(FreeLookController / FreeLockControllerMobile)는 transform.position 을 직접 옮겨서 CharacterController·Rigidbody 충돌이 없었고,
/// 작업장 씬에는 벽·바닥·가구 콜라이더도 없어서 벽을 지나 밖으로 나가고 선반·카트를 통과했다.
///
/// 환경 충돌체는 'Ignore Raycast' 레이어(2)에 둔다 (Tools ▸ DoDrill ▸ 플레이어 충돌 만들기, PlayerCollisionBuilder):
///   · 기본 Raycast(레이어 마스크를 안 준 것)에 안 잡혀서 클릭·호버가 가구 콜라이더에 막히지 않고,
///   · 물리 충돌 행렬에서 'Player' 레이어(VR AutoHandPlayer 캡슐)하고만 충돌하게 해서 도구·손·부품이 가구 안에서 튕기지 않는다.
/// PC·모바일은 물리 엔진 충돌이 아니라 이 클래스의 캡슐 스윕으로 막는다(벽을 따라 미끄러짐).
/// </summary>
public static class PlayerCollision
{
    /// <summary>환경 충돌체가 놓이는 레이어('Ignore Raycast').</summary>
    public const int BlockLayer = 2;
    public static readonly int BlockMask = 1 << BlockLayer;

    private const float Skin = 0.02f;           // 벽에서 띄우는 간격(m)
    private const float FootClearance = 0.12f;  // 발 밑 이만큼보다 낮은 턱·바닥 모서리는 무시(m)

    private static readonly Collider[] s_overlaps = new Collider[8];
    private static CapsuleCollider s_probe;

    /// <summary>
    /// position(발 밑 기준)에서 수평 이동량 delta 를 충돌을 고려해 적용한 새 위치를 돌려준다. y 는 그대로.
    /// radius/height 는 이미 아바타 스케일을 곱한 값.
    /// </summary>
    public static Vector3 Move(Vector3 position, Vector3 delta, float radius, float height)
    {
        delta.y = 0f;

        position = Depenetrate(position, radius, height);

        for (int i = 0; i < 3 && delta.sqrMagnitude > 1e-10f; i++)
        {
            float dist = delta.magnitude;
            Vector3 dir = delta / dist;
            GetCapsule(position, radius, height, out Vector3 p1, out Vector3 p2);

            if (Physics.CapsuleCast(p1, p2, radius, dir, out RaycastHit hit, dist + Skin, BlockMask, QueryTriggerInteraction.Ignore))
            {
                float travel = Mathf.Max(0f, hit.distance - Skin);
                position += dir * travel;

                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 1e-6f) break;
                n.Normalize();

                // 남은 이동량에서 벽 방향 성분을 빼서 벽을 따라 미끄러지게
                Vector3 rest = dir * Mathf.Max(0f, dist - travel);
                delta = rest - Vector3.Dot(rest, n) * n;
            }
            else
            {
                position += delta;
                break;
            }
        }

        return Depenetrate(position, radius, height);
    }

    private static void GetCapsule(Vector3 feet, float radius, float height, out Vector3 p1, out Vector3 p2)
    {
        float bottom = Mathf.Max(radius, FootClearance + radius);
        float top = Mathf.Max(bottom + 0.01f, height - radius);
        p1 = feet + Vector3.up * bottom;
        p2 = feet + Vector3.up * top;
    }

    // 이미 겹쳐 있는 경우(스폰 위치가 가구 안, 가구가 나중에 켜짐 등) 수평으로 밀어낸다.
    // 한 번 호출(=한 프레임)에서 겹침 해소로 밀어내는 총 거리 상한. 상한이 없으면 가구 안에서 시작했을 때 반대편 벽을 뚫고 밖으로 밀려났다.
    private const float MaxDepenetrationPerCall = 0.2f;

    private static Vector3 Depenetrate(Vector3 position, float radius, float height)
    {
        float budget = MaxDepenetrationPerCall;
        for (int iter = 0; iter < 3 && budget > 1e-4f; iter++)
        {
            GetCapsule(position, radius, height, out Vector3 p1, out Vector3 p2);
            int n = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, s_overlaps, BlockMask, QueryTriggerInteraction.Ignore);
            if (n <= 0) break;

            var probe = GetProbe(radius, height);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var other = s_overlaps[i];
                if (other == null) continue;
                if (Physics.ComputePenetration(probe, position, Quaternion.identity,
                                               other, other.transform.position, other.transform.rotation,
                                               out Vector3 dir, out float d))
                {
                    dir.y = 0f;
                    push += dir * (d + Skin);
                }
            }
            if (push.sqrMagnitude < 1e-8f) break;
            float len = push.magnitude;
            if (len > budget) push *= budget / len;
            budget -= push.magnitude;
            position += push;
        }
        return position;
    }

    private static CapsuleCollider GetProbe(float radius, float height)
    {
        if (s_probe == null)
        {
            var go = new GameObject("[PlayerCollisionProbe]") { hideFlags = HideFlags.HideAndDontSave };
            go.layer = 31;                       // 마스크에 없는 레이어
            s_probe = go.AddComponent<CapsuleCollider>();
            s_probe.isTrigger = true;
            s_probe.direction = 1;               // Y
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);   // 에디터(비플레이)에서는 호출 불가
        }
        float bottom = Mathf.Max(radius, FootClearance + radius);
        float top = Mathf.Max(bottom + 0.01f, height - radius);
        s_probe.radius = radius;
        s_probe.height = (top - bottom) + radius * 2f;
        s_probe.center = new Vector3(0f, (bottom + top) * 0.5f, 0f);
        return s_probe;
    }
}
