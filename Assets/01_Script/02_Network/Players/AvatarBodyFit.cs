using Autohand;
using RootMotion.FinalIK;
using UnityEngine;

// ============================================================
//  AvatarBodyFit.cs  (2026-11-13)
//
//  VR 아바타 체형·자세 보정 묶음.
//  · StandingHeight      — '서 있을 때 머리 높이' 추정. 잠깐 튄 값(헤드셋 들어 올림·추적 튐)은 무시하고,
//                          0.6초 이상 유지된 높이만 받아들인다. 웅크림은 키로 보지 않는다.
//  · LocalAvatarHeightFit — 본인 시점 VRIK 아바타(정비사 모델)를 접속자 키에 맞춰 크기 조정.
//                          예전엔 고정 체형이라 아바타보다 키가 크면 머리 목표에 닿으려고 다리가 쭉 펴지며 발뒤꿈치가 들려 '까치발'이 됐다.
//  · ArmBodyClearance    — 손을 몸 쪽으로 가져오면 팔·손이 몸통을 뚫던 문제.
//                          ① 손이 몸통 가까이 오면 팔꿈치 목표(Bend Goal)를 바깥으로 벌려 팔뚝이 가슴을 가로지르지 않게 하고
//                          ② (원격 아바타만) 손목 목표가 몸통 안으로 들어오면 몸통 표면 밖으로 밀어낸다.
//                          본인 아바타는 손이 AutoHand 물리 손과 묶여 있어 손은 밀지 않고 팔꿈치만 벌린다.
// ============================================================

/// <summary>서 있을 때의 머리 높이 추정(잠깐 튄 값 무시).</summary>
public struct StandingHeight
{
    private float _max;
    private float _candidate;
    private float _above;

    public float Value => _max;

    public float Feed(float h, float dt)
    {
        if (h > _max + 0.01f)
        {
            // 더 높은 값이 0.6초 이상 이어져야 키로 인정(그 동안의 최저값)
            _candidate = _above <= 0f ? h : Mathf.Min(_candidate, h);
            _above += dt;
            if (_above >= 0.6f) { _max = _candidate; _above = 0f; }
        }
        else
        {
            _above = 0f;
            // 서 있는 범위(최대에서 12cm 이내)에 오래 있으면 아주 천천히 그쪽으로 — 한 번 잘못 잡힌 값이 영원히 남지 않게
            if (_max > 0f && h > _max - 0.12f) _max -= (_max - h) * 0.03f * dt;
        }
        return _max;
    }
}

/// <summary>본인 시점 VRIK 아바타를 접속자 키에 맞춘다(보이는 IK + AutoHand 의 숨은 IK 복사본 둘 다).</summary>
public class LocalAvatarHeightFit : MonoBehaviour
{
    [Tooltip("아바타 차렷 자세에서 발바닥~머리뼈 높이(m, 스케일 1). 정비사 모델 1.638")]
    public float restHeadHeight = 1.638f;
    public Vector2 scaleRange = new Vector2(0.85f, 1.2f);
    [Tooltip("아바타 크기 여유(1 = 키에 딱 맞춤). 1.02 로 크게 하면 서 있어도 무릎·팔꿈치가 굽었다")]
    public float kneeSlack = 1.0f;

    private AutoHandVRIK _ah;
    private VRIK _vis;
    private Transform _head;
    private Vector3 _visBase, _invBase;
    private bool _invBaseSet;
    private StandingHeight _stand;
    private float _cur = 1f;
    private float _nextLog;

    private void Update()
    {
        if (_vis == null)
        {
            if (_ah == null) _ah = GetComponentInChildren<AutoHandVRIK>(true);
            if (_ah == null || _ah.visibleIK == null) return;
            _vis = _ah.visibleIK;
            _visBase = _vis.transform.localScale;
            _head = _vis.solver.spine.headTarget;

            var clr = _vis.GetComponent<ArmBodyClearance>();
            if (clr == null)
            {
                clr = _vis.gameObject.AddComponent<ArmBodyClearance>();
                clr.pushHands = false;   // 본인 손은 AutoHand 물리 손과 묶여 있어 밀지 않는다
            }
        }
        if (!_invBaseSet && _ah.invisibleIK != null) { _invBase = _ah.invisibleIK.transform.localScale; _invBaseSet = true; }
        if (_head == null) return;

        float h = _head.position.y - _vis.transform.position.y;   // 아바타 루트 = 발(AutoHandVRIK 가 플레이어 높이로 맞춤)
        float stand = _stand.Feed(h, Time.deltaTime);
        if (stand < 1.0f) return;

        float want = Mathf.Clamp(stand / restHeadHeight * kneeSlack, scaleRange.x, scaleRange.y);
        if (Mathf.Abs(want - _cur) < 0.005f) return;
        _cur = Mathf.Lerp(_cur, want, 1f - Mathf.Exp(-3f * Time.deltaTime));
        _vis.transform.localScale = _visBase * _cur;
        if (_invBaseSet && _ah.invisibleIK != null) _ah.invisibleIK.transform.localScale = _invBase * _cur;

        if (Time.unscaledTime >= _nextLog)
        {
            _nextLog = Time.unscaledTime + 10f;
            Debug.Log($"[AvatarFit][local] standHead={stand:F2}m scale={_cur:F3} (want {want:F3})");
        }
    }
}

/// <summary>손이 몸통 가까이 올 때 팔꿈치를 벌리고, (원격만) 손목이 몸통을 뚫지 않게 밀어낸다.</summary>
[RequireComponent(typeof(VRIK))]
public class ArmBodyClearance : MonoBehaviour
{
    public bool pushHands = true;
    [Tooltip("몸통 반폭(m, 스케일 1) — 앞·옆·뒤")]
    public float torsoFront = 0.13f, torsoSide = 0.17f, torsoBack = 0.12f;
    [Tooltip("손목 두께 여유")]
    public float wristMargin = 0.045f;
    [Tooltip("손이 몸 가까이 왔을 때 팔꿈치 목표(어깨 기준, 바깥 +/아래 -/뒤 -, 스케일 1)")]
    public Vector3 flareOffset = new Vector3(0.45f, -0.18f, -0.05f);
    [Range(0f, 1f)] public float flareWeight = 0.85f;

    private VRIK _ik;
    private Side _l, _r;

    private class Side
    {
        public IKSolverVR.Arm arm;
        public Transform upper;
        public Transform goal;
        public Vector3 baseLocal;
        public float baseWeight;
        public bool ownGoal;
        public float sign;
        public Vector3 savedPos;
        public bool pushed;
    }

    private void OnEnable()
    {
        _ik = GetComponent<VRIK>();
        if (_ik == null) return;
        // 팔 늘어남 끄기: 손 목표가 팔 길이보다 멀면(끌기 장비가 손을 바닥 쪽에 붙잡고 있을 때 등) VRIK 기본값은
        // 팔뼈를 늘려서라도 닿게 해 팔 메쉬가 길게 늘어났다. 늘이지 않고 팔을 쭉 편 데서 멈춘다.
        if (_ik.solver.leftArm != null) _ik.solver.leftArm.stretchCurve = new AnimationCurve();
        if (_ik.solver.rightArm != null) _ik.solver.rightArm.stretchCurve = new AnimationCurve();
        _ik.solver.OnPreUpdate += Pre;
        _ik.solver.OnPostUpdate += Post;
    }

    private void OnDisable()
    {
        if (_ik == null) return;
        _ik.solver.OnPreUpdate -= Pre;
        _ik.solver.OnPostUpdate -= Post;
        Post();
    }

    private Side Make(IKSolverVR.Arm arm, Transform upper, string name)
    {
        var root = _ik.references.root;
        if (arm == null || upper == null || root == null) return null;
        var s = new Side { arm = arm, upper = upper };
        Vector3 ul = root.InverseTransformPoint(upper.position);
        s.sign = ul.x >= 0f ? 1f : -1f;
        if (arm.bendGoal == null)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            g.localPosition = FlareLocal(s);
            arm.bendGoal = g;
            s.ownGoal = true;
            s.baseWeight = 0f;   // 평소엔 VRIK 자동 판단 그대로
        }
        else s.baseWeight = arm.bendGoalWeight;
        s.goal = arm.bendGoal;
        s.baseLocal = s.goal.localPosition;
        return s;
    }

    private Vector3 FlareLocal(Side s)
    {
        var root = _ik.references.root;
        Vector3 ul = root.InverseTransformPoint(s.upper.position);
        return new Vector3(ul.x + s.sign * flareOffset.x, ul.y + flareOffset.y, ul.z + flareOffset.z);
    }

    private void Pre()
    {
        if (_ik == null || _ik.references.root == null || _ik.references.pelvis == null) return;
        if (_l == null) _l = Make(_ik.solver.leftArm, _ik.references.leftUpperArm, "ElbowFlare_L");
        if (_r == null) _r = Make(_ik.solver.rightArm, _ik.references.rightUpperArm, "ElbowFlare_R");
        Do(_l);
        Do(_r);
    }

    private void Do(Side s)
    {
        if (s == null || s.arm.target == null) return;
        var refs = _ik.references;
        Transform root = refs.root;
        float sc = Mathf.Max(0.01f, root.lossyScale.y);
        Transform top = refs.neck != null ? refs.neck : refs.head;
        if (top == null) return;

        Vector3 hp = s.arm.target.position;
        Vector3 pel = refs.pelvis.position, nk = top.position;
        bool inBand = hp.y > pel.y - 0.05f * sc && hp.y < nk.y + 0.02f * sc;

        Vector3 up = Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(root.forward, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);
        float k = Mathf.Clamp01(Mathf.InverseLerp(pel.y, nk.y, hp.y));
        Vector3 axis = Vector3.Lerp(pel, nk, k);
        Vector3 d = hp - axis;
        float f = Vector3.Dot(d, fwd), x = Vector3.Dot(d, right);
        float a = (f >= 0f ? torsoFront : torsoBack) * sc + wristMargin * sc;
        float b = torsoSide * sc + wristMargin * sc;
        float e = (f * f) / (a * a) + (x * x) / (b * b);   // 1 = 몸통 표면, <1 = 안쪽

        // ① 팔꿈치 벌리기: 손이 몸통 '앞'에서 가운데 쪽으로 들어올 때만(가슴 앞을 가로지르는 경우).
        //    예전엔 차렷 자세로 손이 엉덩이 옆에 있어도 몸통 가까이로 판정돼 팔꿈치가 바깥으로 벌어지며 '허리에 손 얹은' 엉거주춤 자세가 됐다.
        float inward = s.sign * x;                       // 손이 자기 쪽 옆구리에 있으면 큰 값, 가운데·반대쪽이면 작은 값
        float front = Mathf.InverseLerp(-0.02f * sc, 0.06f * sc, f);   // 몸통 앞쪽일수록 1
        float center = Mathf.InverseLerp(torsoSide * 0.9f * sc, torsoSide * 0.3f * sc, inward);   // 가운데로 들어올수록 1
        float close = inBand ? Mathf.InverseLerp(1.8f, 1.0f, e) * front * center : 0f;
        if (s.goal != null)
        {
            Vector3 flare = FlareLocal(s);
            s.goal.localPosition = Vector3.Lerp(s.baseLocal, flare, close);
            s.arm.bendGoalWeight = Mathf.Lerp(s.baseWeight, Mathf.Max(s.baseWeight, flareWeight), close);
        }

        // ② 손목 밀어내기(원격만): 몸통 안이면 표면으로(높이는 그대로)
        if (pushHands && inBand && e < 1f)
        {
            Vector3 nd;
            if (e < 1e-4f) nd = axis + fwd * a;   // 정중앙 → 앞으로
            else
            {
                float m = 1f / Mathf.Sqrt(e);
                nd = axis + fwd * (f * m) + right * (x * m);
            }
            nd.y = hp.y;
            s.savedPos = hp;
            s.pushed = true;
            s.arm.target.position = nd;
        }
    }

    // 목표 위치는 이번 풀이에만 바꾸고 되돌린다(네트워크 보간·잡은 물건 위치를 건드리지 않게)
    private void Post()
    {
        Restore(_l);
        Restore(_r);
    }

    private static void Restore(Side s)
    {
        if (s == null || !s.pushed) return;
        s.pushed = false;
        if (s.arm.target != null) s.arm.target.position = s.savedPos;
    }
}
