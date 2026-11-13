using RootMotion.FinalIK;
using UnityEngine;

// ============================================================
//  VrLyingPose.cs
//
//  VR 아바타(VRIK)를 크리퍼 위에 반듯이 누운 자세로 보이게 한다. CreeperRide 가 탄 사람의 VRIK 에 붙였다 뗐다 한다
//  (모든 화면에서 — 남이 보는 원격 아바타와 본인 시점 몸 둘 다).
//  · 아바타 루트를 '얼굴은 위, 머리는 크리퍼 앞쪽' 으로 눕히고 등이 크리퍼 판 위에 닿게 놓는다
//  · 몸통·다리·보행 IK 는 끄고(애니메이션 차렷 자세 그대로 누움), 팔 IK 만 남겨 손은 실제 손(컨트롤러)을 따라간다
//  · 원격 아바타가 머리를 따라 루트를 세우거나 키를 다시 재지 않도록 VRPlayer.lyingPose 를 켠다
//  떼면 저장해 둔 IK 가중치·루트 자세를 되돌린다.
// ============================================================
[DefaultExecutionOrder(5000)]   // VRPlayer(원격 루트 추종)·AutoHand 보다 뒤, VRIK(LateUpdate) 보다 앞
public class VrLyingPose : MonoBehaviour
{
    public Transform creeper;
    [Tooltip("크리퍼 기준 머리 위치(앞쪽, m) — CreeperRide.FollowMe 와 같은 값")] public float headForward = 0.55f;
    [Tooltip("판 윗면에서 아바타 루트(몸 두께 중심)까지 높이")] public float backThickness = 0.12f;
    [Tooltip("발바닥~머리뼈 길이(m, 스케일 1)")] public float bodyLength = 1.62f;

    private VRIK _ik;
    private Transform _root;
    private VRPlayer _vrPlayer;
    private float _topY;
    private bool _saved;
    private Quaternion _rootRot;
    private Vector3 _rootPos;
    private float _spPos, _spRot, _pelPos, _pelRot, _chest, _loco;
    private float _lPos, _lRot, _lBend, _rPos, _rRot, _rBend;
    private bool _plant;

    private void OnEnable()
    {
        _ik = GetComponent<VRIK>();
        if (_ik == null) { enabled = false; return; }
        _root = _ik.references.root != null ? _ik.references.root : _ik.transform;
        _vrPlayer = GetComponentInParent<VRPlayer>();
        if (_vrPlayer != null) _vrPlayer.lyingPose = true;

        var s = _ik.solver;
        _rootRot = _root.rotation; _rootPos = _root.position;
        _spPos = s.spine.positionWeight; _spRot = s.spine.rotationWeight;
        _pelPos = s.spine.pelvisPositionWeight; _pelRot = s.spine.pelvisRotationWeight; _chest = s.spine.chestGoalWeight;
        _loco = s.locomotion.weight; _plant = s.plantFeet;
        _lPos = s.leftLeg.positionWeight; _lRot = s.leftLeg.rotationWeight; _lBend = s.leftLeg.bendGoalWeight;
        _rPos = s.rightLeg.positionWeight; _rRot = s.rightLeg.rotationWeight; _rBend = s.rightLeg.bendGoalWeight;
        _saved = true;

        s.spine.positionWeight = 0f; s.spine.rotationWeight = 0f;
        s.spine.pelvisPositionWeight = 0f; s.spine.pelvisRotationWeight = 0f; s.spine.chestGoalWeight = 0f;
        s.locomotion.weight = 0f; s.plantFeet = false;
        s.leftLeg.positionWeight = 0f; s.leftLeg.rotationWeight = 0f; s.leftLeg.bendGoalWeight = 0f;
        s.rightLeg.positionWeight = 0f; s.rightLeg.rotationWeight = 0f; s.rightLeg.bendGoalWeight = 0f;

        _topY = creeper != null ? TopOf(creeper) : _root.position.y + 0.12f;
    }

    private void OnDisable()
    {
        if (_vrPlayer != null) _vrPlayer.lyingPose = false;
        if (!_saved || _ik == null) return;
        _saved = false;
        var s = _ik.solver;
        s.spine.positionWeight = _spPos; s.spine.rotationWeight = _spRot;
        s.spine.pelvisPositionWeight = _pelPos; s.spine.pelvisRotationWeight = _pelRot; s.spine.chestGoalWeight = _chest;
        s.locomotion.weight = _loco; s.plantFeet = _plant;
        s.leftLeg.positionWeight = _lPos; s.leftLeg.rotationWeight = _lRot; s.leftLeg.bendGoalWeight = _lBend;
        s.rightLeg.positionWeight = _rPos; s.rightLeg.rotationWeight = _rRot; s.rightLeg.bendGoalWeight = _rBend;

        // 선 자세로 되돌림: 머리 아래 바닥에 세운다(이후 VRPlayer/AutoHand 가 이어서 맞춘다)
        if (_root != null)
        {
            Vector3 fwd = creeper != null ? creeper.forward : _rootRot * Vector3.forward;
            fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 head = HeadPoint();
            _root.SetPositionAndRotation(new Vector3(head.x, _rootPos.y, head.z), Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }
        if (_ik.solver.locomotion.weight > 0f) _ik.solver.Reset();
    }

    // 본인 몸은 AutoHandVRIK 가 LateUpdate(순서 12)에서 루트 높이를 플레이어 발 높이로 되돌리므로 그 뒤에 한 번 더 맞춘다
    private void LateUpdate() => Place();
    private void Update() => Place();

    private void Place()
    {
        if (_root == null || creeper == null) return;
        Vector3 fwd = creeper.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) return;
        fwd.Normalize();

        // 얼굴 = 위, 머리 방향 = 크리퍼 앞
        Quaternion rot = Quaternion.LookRotation(Vector3.up, fwd);
        float len = bodyLength * Mathf.Max(0.01f, _root.lossyScale.y);
        Vector3 head = HeadPoint();
        Vector3 pos = new Vector3(head.x, 0f, head.z) - fwd * len;
        pos.y = _topY + backThickness * Mathf.Max(0.01f, _root.lossyScale.y);
        _root.SetPositionAndRotation(pos, rot);
    }

    /// <summary>머리가 놓일 자리(수평) — 크리퍼 앞쪽</summary>
    private Vector3 HeadPoint()
    {
        if (creeper == null) return _root.position;
        Vector3 f = creeper.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        return creeper.position + f.normalized * headForward;
    }

    private static float TopOf(Transform t)
    {
        bool any = false; Bounds b = default;
        foreach (var c in t.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (!any) foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        return any ? b.max.y : t.position.y + 0.12f;
    }
}
