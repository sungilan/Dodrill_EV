using System.Collections;
using System.Collections.Generic;
using Autohand;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  Openable.cs
//
//  씬에 놓인 가구의 문·서랍·뚜껑을 열고 닫는다(캐비닛 문, 공구 서랍장 서랍, 공구함 뚜껑 등).
//  · Hinge: pivot(이 물체 로컬 점)을 지나는 axis(로컬 축) 둘레로 openAngle 만큼 회전
//  · Slide: slideDir(로컬 방향)으로 slideDistance 만큼 밀려 나옴
//  · VR: 손으로 잡으면(그립) 바로 놓고 열림/닫힘 전환. PC: 클릭. 모바일: 터치
//  · 열림 상태는 서버를 거쳐 모든 플레이어에게 같게, 늦게 들어온 사람도 현재 상태로 맞춘다(OpenableSync)
//  시나리오와 무관한 자유 상호작용이다.
// ============================================================
public class Openable : MonoBehaviour
{
    public enum Kind { Hinge, Slide }

    [Tooltip("모든 플레이어가 같은 값을 갖는 고유 ID(씬 경로 등)")] public string uniqueId;
    public Kind kind = Kind.Hinge;
    [Tooltip("Hinge: 회전축(이 물체 로컬)")] public Vector3 axis = Vector3.up;
    [Tooltip("Hinge: 축이 지나는 점(이 물체 로컬)")] public Vector3 pivot;
    [Tooltip("Hinge: 열림 각도(도, 부호 = 방향)")] public float openAngle = 100f;
    [Tooltip("Slide: 밀려 나오는 방향(이 물체 로컬)")] public Vector3 slideDir = Vector3.forward;
    [Tooltip("Slide: 밀려 나오는 거리(m, 로컬 단위가 아니라 세계 m)")] public float slideDistance = 0.3f;
    [Tooltip("열고 닫는 데 걸리는 시간(초)")] public float duration = 0.45f;
    [Tooltip("이름표에 보일 이름")] public string displayName = "서랍";

    public bool IsOpen { get; private set; }

    /// <summary>열리거나 닫히는 중이거나, 멈춘 직후(네트워크 보간이 따라잡는 동안)인가.</summary>
    public bool IsMoving => !Mathf.Approximately(_t, IsOpen ? 1f : 0f) || Time.time < _movedUntil;
    private float _movedUntil;

    private Vector3 _closedPos;
    private Quaternion _closedRot;
    private float _t;   // 0 닫힘 ~ 1 열림
    private Grabbable _grab;

    private void Awake()
    {
        _closedPos = transform.localPosition;
        _closedRot = transform.localRotation;
        _grab = GetComponent<Grabbable>();
        if (string.IsNullOrEmpty(uniqueId)) uniqueId = Path(transform);
        // 서랍은 안에 놓인 장비(공구)가 서랍과 함께 움직여야 한다 — 서랍 안 장비는 서랍의 자식이 아니라 따로 Spawn 되는 네트워크 물체라서
        // 예전엔 서랍이 움직여도 늦게(네트워크 보간) 따라오거나 서랍을 뚫고 나왔다.
        if (kind == Kind.Slide && GetComponent<DrawerContents>() == null) gameObject.AddComponent<DrawerContents>();
    }

    private void OnEnable()
    {
        OpenableSync.Register(this);
        if (_grab != null) _grab.onGrab.AddListener(OnVRGrab);
    }

    private void OnDisable()
    {
        OpenableSync.Unregister(this);
        if (_grab != null) _grab.onGrab.RemoveListener(OnVRGrab);
    }

    public static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    // ── 입력 ─────────────────────────────

    public void OnPCClick() => OpenableSync.Request(uniqueId, !IsOpen);

    public string HoverMessage(bool touch) => (touch ? "터치하여 " : "클릭하여 ") + (IsOpen ? "닫기" : "열기");

    private float _lastVrToggle;

    private void OnVRGrab(Hand hand, Grabbable g)
    {
        StartCoroutine(ReleaseNextFrame(hand));
        if (Time.unscaledTime - _lastVrToggle < 0.4f) return;   // 두 손가락이 동시에 잡힘 등
        _lastVrToggle = Time.unscaledTime;
        OpenableSync.Request(uniqueId, !IsOpen);
        EquipFx.Haptic(hand, 0.05f, 0.5f);
    }

    private IEnumerator ReleaseNextFrame(Hand hand)
    {
        yield return new WaitForFixedUpdate();
        if (hand != null && hand.holdingObj == _grab) hand.ForceReleaseGrab();
    }

    // ── 상태 ─────────────────────────────

    public void ApplyState(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.5f);
    }

    private void Update()
    {
        float target = IsOpen ? 1f : 0f;
        if (Mathf.Approximately(_t, target)) return;
        _movedUntil = Time.time + 0.8f;
        _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(0.05f, duration));
        float e = Mathf.SmoothStep(0f, 1f, _t);
        if (kind == Kind.Hinge)
        {
            // 닫힌 자세에서 pivot 을 지나는 axis 둘레로 회전(부모 공간에서 계산)
            Quaternion q = Quaternion.AngleAxis(openAngle * e, _closedRot * axis.normalized);
            Vector3 pivotParent = _closedPos + _closedRot * Vector3.Scale(pivot, transform.localScale);
            transform.localRotation = q * _closedRot;
            transform.localPosition = pivotParent + q * (_closedPos - pivotParent);
        }
        else
        {
            Vector3 dirParent = _closedRot * slideDir.normalized;
            float scale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localPosition = _closedPos + dirParent * (slideDistance * e / Mathf.Max(1e-4f, scale));
        }
    }
}

/// <summary>
/// 서랍 안에 놓인 장비를 서랍과 함께 움직인다(모든 화면에서 즉시, 네트워크 보간을 기다리지 않고).
/// · 서랍이 가만히 있을 때 0.5초마다 서랍 안(서랍 경계)에 있는 '아무도 안 잡은' 장비를 담긴 것으로 기억한다(서랍 기준 위치·회전)
/// · 서랍이 움직이는 동안(과 직후)은 매 프레임 담긴 장비를 서랍 기준 위치로 붙인다 — 누가 집으면 목록에서 빠진다
/// </summary>
[DefaultExecutionOrder(1100)]   // 네트워크 보간·SyncGrab 이 위치를 정한 뒤에
public class DrawerContents : MonoBehaviour
{
    private class Item { public SyncGrab sg; public Vector3 lp; public Quaternion lr; }
    private readonly List<Item> _items = new List<Item>();
    private Openable _drawer;
    private Bounds _local;      // 서랍 로컬 경계(닫힌 상태)
    private bool _hasBounds;
    private float _nextScan;

    private void Awake() { _drawer = GetComponent<Openable>(); }

    private void Start()
    {
        bool any = false; Bounds b = default;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        Vector3 s = transform.lossyScale;
        Vector3 size = new Vector3(b.size.x / Mathf.Max(1e-4f, s.x), b.size.y / Mathf.Max(1e-4f, s.y), b.size.z / Mathf.Max(1e-4f, s.z));
        // 장비 기준점은 바닥에 있어 서랍 경계 안에 들어온다. 위로 넓히면 위 칸 서랍의 장비까지 잡히므로 위·아래는 그대로, 옆·앞뒤만 조금 여유
        size.x += 0.06f / Mathf.Max(1e-4f, s.x); size.z += 0.06f / Mathf.Max(1e-4f, s.z);
        _local = new Bounds(transform.InverseTransformPoint(b.center), size);
        _hasBounds = true;
    }

    private void LateUpdate()
    {
        if (!_hasBounds || _drawer == null) return;
        if (!Fishnet_HasPeer()) return;
        bool moving = _drawer.IsMoving;

        if (moving)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it.sg == null || !it.sg.IsSpawned || it.sg.IsGrabbed) { _items.RemoveAt(i); continue; }
                it.sg.transform.SetPositionAndRotation(transform.TransformPoint(it.lp), transform.rotation * it.lr);
                var rb = it.sg.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            return;
        }

        if (Time.time < _nextScan) return;
        _nextScan = Time.time + 0.5f;
        Scan();
    }

    private static bool Fishnet_HasPeer()
    {
        return FishNet.InstanceFinder.IsServerStarted || FishNet.InstanceFinder.IsClientStarted;
    }

    private void Scan()
    {
        foreach (var sg in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (sg == null || !sg.IsSpawned) continue;
            if (sg.GetComponent<CarryBasket>() != null || sg.GetComponent<FloorDragProp>() != null) continue;
            int idx = _items.FindIndex(x => x.sg == sg);
            if (sg.IsGrabbed) { if (idx >= 0) _items.RemoveAt(idx); continue; }
            Vector3 lp = transform.InverseTransformPoint(sg.transform.position);
            bool inside = _local.Contains(lp);
            var lr = Quaternion.Inverse(transform.rotation) * sg.transform.rotation;
            if (inside && idx < 0) _items.Add(new Item { sg = sg, lp = lp, lr = lr });
            else if (inside) { _items[idx].lp = lp; _items[idx].lr = lr; }
            else if (idx >= 0) _items.RemoveAt(idx);
        }
    }
}

public struct OpenableBroadcast : IBroadcast
{
    public string id;      // 비어 있으면: 클라이언트 → 서버 '전체 상태 보내 줘'
    public bool open;
}

/// <summary>Openable 상태 동기화(서버가 기억, 늦게 온 사람에게 전체 전송).</summary>
public static class OpenableSync
{
    private static readonly Dictionary<string, Openable> s_all = new Dictionary<string, Openable>();
    private static readonly Dictionary<string, bool> s_serverState = new Dictionary<string, bool>();
    private static bool s_serverReg, s_clientReg, s_asked;

    public static void Register(Openable o)
    {
        if (o == null) return;
        s_all[o.uniqueId] = o;
        EnsureRegistered();
        if (s_serverState.TryGetValue(o.uniqueId, out bool st)) o.ApplyState(st);
    }

    public static void Unregister(Openable o)
    {
        if (o != null && s_all.TryGetValue(o.uniqueId, out var cur) && cur == o) s_all.Remove(o.uniqueId);
    }

    private static void EnsureRegistered()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (!s_serverReg && sm != null) { sm.RegisterBroadcast<OpenableBroadcast>(OnServer); s_serverReg = true; }
        if (!s_clientReg && cm != null) { cm.RegisterBroadcast<OpenableBroadcast>(OnClient); s_clientReg = true; }
        if (!s_asked && cm != null && cm.Started && !InstanceFinder.IsServerStarted)
        {
            s_asked = true;
            cm.Broadcast(new OpenableBroadcast { id = "" });
        }
    }

    public static void Request(string id, bool open)
    {
        EnsureRegistered();
        if (InstanceFinder.IsServerStarted) { ServerSet(id, open); return; }
        if (InstanceFinder.IsClientStarted) { InstanceFinder.ClientManager.Broadcast(new OpenableBroadcast { id = id, open = open }); return; }
        if (s_all.TryGetValue(id, out var o)) o.ApplyState(open);   // 오프라인
    }

    private static void ServerSet(string id, bool open)
    {
        s_serverState[id] = open;
        if (s_all.TryGetValue(id, out var o)) o.ApplyState(open);
        InstanceFinder.ServerManager.Broadcast(new OpenableBroadcast { id = id, open = open });
    }

    private static void OnServer(NetworkConnection conn, OpenableBroadcast b, Channel ch)
    {
        if (string.IsNullOrEmpty(b.id))
        {
            foreach (var kv in s_serverState) InstanceFinder.ServerManager.Broadcast(conn, new OpenableBroadcast { id = kv.Key, open = kv.Value });
            return;
        }
        if (b.id.Length > 256) return;
        ServerSet(b.id, b.open);
    }

    private static void OnClient(OpenableBroadcast b, Channel ch)
    {
        if (InstanceFinder.IsServerStarted) return;
        if (s_all.TryGetValue(b.id, out var o)) o.ApplyState(b.open);
    }
}
