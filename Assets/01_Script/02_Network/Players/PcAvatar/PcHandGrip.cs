using UnityEngine;

/// <summary>
/// 물체를 잡고 있을 때의 "손 모양" 담당 — 손가락을 쥔 모양으로 말고, 손목 방향(손바닥이 안쪽을 보게)을 계산한다.
///
/// 손가락 쥠 값은 에디터 빌더(PcAvatarBuilder)가 휴머노이드 근육 값으로 한 번 만들어 저장해 둔다:
///   curl[i] = 기본 포즈의 로컬 회전 → 쥔 포즈의 로컬 회전으로 가는 "추가 회전".
/// 런타임에는 애니메이션이 정한 로컬 회전 위에 curl 을 weight 만큼 곱한다(LateUpdate, IK 앞).
/// 본 축 방향을 가정하지 않으므로 휴머노이드 리그라면 어떤 모델에도 동작한다.
/// </summary>
public class PcHandGrip : MonoBehaviour
{
    [System.Serializable]
    public class Hand
    {
        public Transform wrist;
        [Tooltip("엄지·검지·중지·약지·소지 × (근위·중간·원위) = 15개")]
        public Transform[] bones;
        [Tooltip("bones 와 같은 순서 — 기본 포즈 → 쥔 포즈의 추가 로컬 회전")]
        public Quaternion[] curl;
        [Tooltip("손 본 로컬 기준: 손가락이 향하는 방향")]
        public Vector3 fingerDirLocal = Vector3.right;
        [Tooltip("손 본 로컬 기준: 손바닥이 향하는 방향")]
        public Vector3 palmNormalLocal = Vector3.down;
    }

    public Hand right = new Hand();
    public Hand left = new Hand();

    [Header("손 방향 (몸 기준)")]
    [Tooltip("손가락 방향을 앞에서 아래로 기울이는 각도")]
    public float fingersPitchDown = 20f;

    public bool IsBaked(bool isRight)
    {
        var h = isRight ? right : left;
        return h.wrist != null && h.bones != null && h.curl != null && h.bones.Length > 0 && h.bones.Length == h.curl.Length;
    }

    /// <summary>애니메이션이 정한 손가락 포즈 위에 쥠을 weight(0..1) 만큼 얹는다. LateUpdate 에서 매 프레임 호출.</summary>
    public void ApplyCurl(bool isRight, float weight)
    {
        if (weight <= 0.001f || !IsBaked(isRight)) return;
        var h = isRight ? right : left;
        for (int i = 0; i < h.bones.Length; i++)
        {
            if (h.bones[i] == null) continue;
            h.bones[i].localRotation = h.bones[i].localRotation * Quaternion.Slerp(Quaternion.identity, h.curl[i], weight);
        }
    }

    /// <summary>
    /// 손 본이 가져야 할 월드 회전: 손가락은 몸 앞쪽(약간 아래), 손바닥은 몸 안쪽(오른손은 왼쪽, 왼손은 오른쪽)을 향한다.
    /// 손잡이를 감싸 쥐는 가장 흔한 한 손 잡기 자세.
    /// </summary>
    public Quaternion HandRotation(bool isRight, Quaternion bodyYaw)
    {
        var h = isRight ? right : left;
        Vector3 f = bodyYaw * (Quaternion.Euler(fingersPitchDown, 0f, 0f) * Vector3.forward);
        Vector3 n = bodyYaw * (isRight ? Vector3.left : Vector3.right);

        Vector3 fl = h.fingerDirLocal.sqrMagnitude > 0.0001f ? h.fingerDirLocal.normalized : Vector3.right;
        Vector3 nl = h.palmNormalLocal.sqrMagnitude > 0.0001f ? h.palmNormalLocal.normalized : Vector3.down;

        // 1) 손가락 방향을 맞춘다
        Quaternion q1 = Quaternion.FromToRotation(fl, f);
        // 2) 손가락 방향을 축으로 돌려 손바닥 방향을 맞춘다
        Vector3 n1 = q1 * nl;
        Vector3 nProj = Vector3.ProjectOnPlane(n, f);
        Vector3 n1Proj = Vector3.ProjectOnPlane(n1, f);
        if (nProj.sqrMagnitude < 1e-6f || n1Proj.sqrMagnitude < 1e-6f) return q1;
        float ang = Vector3.SignedAngle(n1Proj, nProj, f);
        return Quaternion.AngleAxis(ang, f) * q1;
    }
}
