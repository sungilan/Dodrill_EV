using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 다른 사람(원격) 아바타가 든 물체가 손에 붙어 보이게 한다.
///
/// 문제: 물체는 잡은 사람의 카메라 앞(HoldPoint)을 네트워크로 따라가고, 원격 아바타의 손은 팔 길이 안에서만 뻗는다.
///       그래서 보는 사람 화면에서는 물체가 손보다 앞 허공에 떠 있고, 네트워크 보간·손 IK 가 서로 어긋나 깜박였다.
/// 해결: 화면을 그리기 직전(URP beginCameraRendering)에만 물체를 손 위치로 옮기고, 그린 직후(endCameraRendering)
///       원래 위치로 되돌린다. 물리·네트워크·IK 목표는 진짜 위치를 그대로 쓰므로 부작용이 없다.
///       (내가 든 물체는 1인칭 화면이므로 건드리지 않는다.)
/// </summary>
[DisallowMultipleComponent]
public class HeldObjectHandAttach : MonoBehaviour
{
    private static readonly List<HeldObjectHandAttach> s_all = new List<HeldObjectHandAttach>();
    private static bool s_hooked;

    private PcMobileAvatarController _pc;
    private VRPlayer _vr;
    private PcArmReach _arm;

    private SyncGrab _held;
    private float _nextSearch;
    private Vector3 _saved;
    private Quaternion _savedRot;
    private Transform _appliedTo;
    private bool _applied;

    private void Awake()
    {
        _pc = GetComponent<PcMobileAvatarController>();
        _vr = GetComponent<VRPlayer>();
        _arm = GetComponentInChildren<PcArmReach>(true);
    }

    private void OnEnable()
    {
        s_all.Add(this);
        if (!s_hooked)
        {
            s_hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }
    }

    private void OnDisable()
    {
        Restore();
        s_all.Remove(this);
    }

    private static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        for (int i = 0; i < s_all.Count; i++) if (s_all[i] != null) s_all[i].Apply();
    }

    private static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        for (int i = 0; i < s_all.Count; i++) if (s_all[i] != null) s_all[i].Restore();
    }

    private bool IsRemote
    {
        get
        {
            if (_pc != null) return !_pc.IsOwnerView;
            if (_vr != null) return !_vr.IsOwner;
            return false;
        }
    }

    private int HolderId => _pc != null ? _pc.HolderId : (_vr != null ? _vr.OwnerId : -1);

    private void Apply()
    {
        // 한 프레임에 카메라가 여러 대(UI 오버레이 카메라 등) 그려지면 begin 이 연달아 불릴 수 있다.
        // 되돌리기 전에 다시 옮기면 '옮겨진 위치'를 원래 위치로 저장해 버려서, 주인이 가만히 있는 동안
        // (네트워크 위치 갱신이 없을 때) 물체가 매 프레임 조금씩 밀려 수백 m 까지 날아갔다.
        // → 손 IK 가 그 물체를 쫓다 팔이 펴져서 '움직일 때만 잡은 자세가 보이던' 원인.
        Restore();
        // 원격 아바타만. 본인 화면은 도구를 카메라 앞에 두고 손목을 도구에 맞춘다(PcArmReach.SetWristSnap). VR 본인은 Auto Hand 가 직접 쥔다.
        if (!IsRemote) return;

        if (Time.unscaledTime >= _nextSearch)
        {
            _nextSearch = Time.unscaledTime + 0.1f;
            _held = SyncGrab.FindHeldBy(HolderId);
        }
        if (_held == null || !_held.IsGrabbed) return;

        // PC·모바일 + 도구에 잡기 포즈가 있으면: 손목(IK 결과) 기준으로 도구의 위치·회전을 포즈대로 맞춘다.
        // 물체는 카메라 앞(HoldPoint), 손은 어깨에서 IK 로 따로 움직여서 둘이 어긋나고 움직일 때 손이 따로 노는 것처럼 보였다.
        if (_pc != null && _arm != null && _arm.HoldWeight >= 0.6f && TryAttachByPose()) return;

        if (!TryGetHandPoint(out Vector3 hand)) return;

        // 손에 붙일 기준점: PC·모바일은 IK 가 향하는 점과 같아야 한다(잡기 포즈가 있는 도구는 손잡이 위 손목 위치)
        Vector3 anchorPoint = (_pc != null && _arm != null) ? _held.HandTargetPoint(_arm.HoldUsesRightHand) : _held.GripWorldPoint;
        Vector3 delta = hand - anchorPoint;
        if (delta.sqrMagnitude > 2f * 2f) return;   // 손과 2m 넘게 떨어진 물체는 아직 동기화 전 — 붙이지 않는다

        _appliedTo = _held.transform;
        _saved = _appliedTo.position;
        _savedRot = _appliedTo.rotation;
        _appliedTo.position = _saved + delta;
        _applied = true;
    }

    private bool TryAttachByPose()
    {
        bool right = _arm.HoldUsesRightHand;
        if (!_held.TryGetGripPose(right, out var pose, out var poseTf)) return false;
        Transform wrist = _arm.HandTransform(right);
        if (wrist == null) return false;

        Transform obj = _held.transform;
        // 포즈 기준 오브젝트(GrabPose_R 등)가 도구 루트에서 어떻게 놓였는지(도구 로컬)
        Quaternion poseInObj = Quaternion.Inverse(obj.rotation) * poseTf.rotation;
        Vector3 wristInObj = Quaternion.Inverse(obj.rotation) * (poseTf.TransformPoint(pose.handOffset) - obj.position);

        // 손목 회전 = 도구 회전 * poseInObj * localQuaternionOffset → 도구 회전을 거꾸로 구한다
        Quaternion objRot = wrist.rotation * Quaternion.Inverse(pose.localQuaternionOffset) * Quaternion.Inverse(poseInObj);
        Vector3 objPos = wrist.position - objRot * wristInObj;
        if ((objPos - obj.position).sqrMagnitude > 2f * 2f) return false;   // 아직 동기화 전이면 붙이지 않는다

        _appliedTo = obj;
        _saved = obj.position;
        _savedRot = obj.rotation;
        obj.SetPositionAndRotation(objPos, objRot);
        _applied = true;
        return true;
    }

    private void Restore()
    {
        if (!_applied) return;
        _applied = false;
        if (_appliedTo != null) _appliedTo.SetPositionAndRotation(_saved, _savedRot);
        _appliedTo = null;
    }

    private bool TryGetHandPoint(out Vector3 hand)
    {
        hand = default;

        if (_pc != null)
        {
            // 손이 잡기 자세에 거의 도달한 뒤에만 붙인다(날아오는 중에는 그대로)
            if (_arm == null || _arm.HoldWeight < 0.6f) return false;
            Transform h = _arm.HandTransform(_arm.HoldUsesRightHand);
            if (h == null) return false;
            hand = h.position;
            return true;
        }

        if (_vr != null && _vr.trackingSetup != null)
        {
            Transform l = _vr.trackingSetup.leftHand != null ? _vr.trackingSetup.leftHand.applyWrist : null;
            Transform r = _vr.trackingSetup.rightHand != null ? _vr.trackingSetup.rightHand.applyWrist : null;
            Vector3 g = _held.GripWorldPoint;
            Transform best = null;
            float bestD = float.MaxValue;
            if (l != null) { float d = (l.position - g).sqrMagnitude; if (d < bestD) { bestD = d; best = l; } }
            if (r != null) { float d = (r.position - g).sqrMagnitude; if (d < bestD) { bestD = d; best = r; } }
            if (best == null) return false;
            hand = best.position;
            return true;
        }

        return false;
    }
}
