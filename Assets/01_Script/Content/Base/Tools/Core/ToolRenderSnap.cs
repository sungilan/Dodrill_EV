using System.Collections.Generic;
using Autohand;
using UnityEngine;

/// <summary>
/// 공구를 볼트에 '보이게만' 착 붙인다(에어 임팩렌치 등).
/// 공구 모델·쥔 손(아바타 손 뼈)을 그리기 직전(onBeforeRender, IK 이후)에 옮기고 그린 직후 원래대로 돌려놓는다.
/// 물리(콜라이더·잡기 조인트·AutoHand 손)는 전혀 건드리지 않는다 — 콜라이더나 잡기 기준점을 옮기면
/// 잡기 조인트가 공구를 끌고 다녀 볼트에서 밀려나고 손이 늘어났다(11/13 드릴 버그).
/// </summary>
public class ToolRenderSnap
{
    private readonly List<Transform> _moved = new List<Transform>();
    private readonly List<Vector3> _pos = new List<Vector3>();
    private readonly List<Quaternion> _rot = new List<Quaternion>();

    private Transform[] _parts;
    private Grabbable _grab;
    private bool _hooked;
    private int _frame = -10;
    private Vector3 _pivot, _delta;
    private Quaternion _r = Quaternion.identity;
    private float _w;

    public ToolRenderSnap(Grabbable grab, params Transform[] parts)
    {
        _grab = grab;
        _parts = parts;
    }

    /// <summary>이번 프레임 목표: pivot(공구 끝)을 기준으로 r 만큼 돌리고 delta 만큼 옮긴다. want=false 면 부드럽게 풀린다.</summary>
    public void Tick(bool want, Vector3 pivot, Quaternion r, Vector3 delta)
    {
        _w = Mathf.MoveTowards(_w, want ? 1f : 0f, Time.deltaTime * (want ? 8f : 6f));
        if (want) { _pivot = pivot; _r = r; _delta = delta; }
        if (_w <= 0f) return;
        _frame = Time.frameCount;
        if (!_hooked)
        {
            _hooked = true;
            Application.onBeforeRender += Apply;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering += OnEnd;
        }
    }

    public void Dispose()
    {
        Restore();
        if (!_hooked) return;
        _hooked = false;
        Application.onBeforeRender -= Apply;
        UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= OnEnd;
    }

    private void OnEnd(UnityEngine.Rendering.ScriptableRenderContext c, List<Camera> cams) => Restore();

    private void Restore()
    {
        for (int i = 0; i < _moved.Count; i++)
            if (_moved[i] != null) { _moved[i].localPosition = _pos[i]; _moved[i].localRotation = _rot[i]; }
        _moved.Clear(); _pos.Clear(); _rot.Clear();
    }

    private void Apply()
    {
        Restore();
        if (_w <= 0f || _frame < Time.frameCount - 1) return;
        Quaternion r = Quaternion.Slerp(Quaternion.identity, _r, _w);
        Vector3 d = _delta * _w;
        foreach (var t in _parts) Move(t, r, d);
        if (_grab == null) return;
        var held = _grab.GetHeldBy();
        if (held == null) return;
        foreach (var h in held) if (h != null) Move(HandBone(h), r, d);
    }

    private void Move(Transform t, Quaternion r, Vector3 d)
    {
        if (t == null) return;
        _moved.Add(t); _pos.Add(t.localPosition); _rot.Add(t.localRotation);
        t.SetPositionAndRotation(r * (t.position - _pivot) + _pivot + d, r * t.rotation);
    }

    private Transform HandBone(Hand h) => FindAvatarHandBone(h);

    // 손 → 아바타 손 뼈 캐시(공용). 못 찾은 손은 0.5초 동안 다시 찾지 않는다 — 예전엔 못 찾으면 공구를 쥐고 있는 동안
    // 렌더 프레임마다 FindObjectsByType ×2 + HashSet 할당을 반복했다(VR 프레임 저하).
    private static readonly Dictionary<Hand, Transform> s_boneCache = new Dictionary<Hand, Transform>();
    private static readonly Dictionary<Hand, float> s_boneRetry = new Dictionary<Hand, float>();
    private static readonly HashSet<RootMotion.FinalIK.VRIK> s_hidden = new HashSet<RootMotion.FinalIK.VRIK>();

    /// <summary>AutoHand 손에 대응하는 보이는 아바타의 실제 손 뼈(Animator). 숨은 IK 복사본 제외, 30cm 안 가장 가까운 것.
    /// VRIK 참조는 잡는 동안 잡기 기준점이라 쓰지 않는다.</summary>
    public static Transform FindAvatarHandBone(Hand h)
    {
        if (h == null) return null;
        if (s_boneCache.TryGetValue(h, out var c))
        {
            if (c != null) return c;
            s_boneCache.Remove(h);
        }
        if (s_boneRetry.TryGetValue(h, out float until) && Time.unscaledTime < until) return null;

        s_hidden.Clear();
        foreach (var ah in Object.FindObjectsByType<AutoHandVRIK>(FindObjectsSortMode.None)) if (ah != null && ah.invisibleIK != null) s_hidden.Add(ah.invisibleIK);
        Transform best = null; float bd = 0.3f * 0.3f;
        var side = h.left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        foreach (var ik in Object.FindObjectsByType<RootMotion.FinalIK.VRIK>(FindObjectsSortMode.None))
        {
            if (ik == null || s_hidden.Contains(ik) || !ik.isActiveAndEnabled) continue;
            var anim = ik.GetComponentInChildren<Animator>();
            var b = anim != null && anim.isHuman ? anim.GetBoneTransform(side) : null;
            if (b == null || b.IsChildOf(h.transform)) continue;
            float d = (b.position - h.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        s_hidden.Clear();
        if (best != null) { s_boneCache[h] = best; s_boneRetry.Remove(h); }
        else s_boneRetry[h] = Time.unscaledTime + 0.5f;
        return best;
    }

    /// <summary>볼트 머리(빠지는 방향 쪽 끝) 위치.</summary>
    public static Vector3 BoltHead(Bolt bolt, out Vector3 outDir)
    {
        var bt = bolt.transform;
        outDir = (bt.parent != null ? bt.parent.TransformDirection(Vector3.down) : Vector3.down).normalized;
        var rd = bolt.GetComponentInChildren<Renderer>();
        if (rd == null) return bt.position;
        var b = rd.bounds; var e = b.extents;
        float along = Mathf.Abs(e.x * outDir.x) + Mathf.Abs(e.y * outDir.y) + Mathf.Abs(e.z * outDir.z);
        return b.center + outDir * along;
    }
}
