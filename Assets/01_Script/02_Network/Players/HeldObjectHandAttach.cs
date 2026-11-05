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
        _applied = false;
        if (!IsRemote) return;

        if (Time.unscaledTime >= _nextSearch)
        {
            _nextSearch = Time.unscaledTime + 0.1f;
            _held = SyncGrab.FindHeldBy(HolderId);
        }
        if (_held == null || !_held.IsGrabbed) return;

        if (!TryGetHandPoint(out Vector3 hand)) return;

        _saved = _held.transform.position;
        _held.transform.position = _saved + (hand - _held.GripWorldPoint);
        _applied = true;
    }

    private void Restore()
    {
        if (!_applied) return;
        _applied = false;
        if (_held != null) _held.transform.position = _saved;
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
