using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  ProbePoseNet.cs
//
//  (11/20 내부 시연) 멀티미터·메거 프로브를 꺼내면 케이블이 프로브를 따라 나오는데, 다른 사람 화면에서는 케이블이
//  본체에 늘어진 그대로였다. PcProbeAim·VrProbeHandling 은 프로브를 '내 화면에서만' 움직였기 때문이다.
//
//  하는 일
//   · 프로브를 꺼낸 사람이 프로브 끝(tip)·꼬리(tail) 위치를 초당 10번 서버로 보낸다(본체 기준 로컬 좌표).
//     넣으면(본체로 돌아가면) 한 번 '넣음'을 보낸다.
//   · 서버는 보낸 사람을 뺀 전원에게 다시 보낸다.
//   · 받은 쪽은 그 계측기에 RemoteProbeView 를 붙여 펜 + 본체→펜 케이블을 그리고, 원래 늘어진 케이블 메시는 숨긴다.
//     2초 동안 소식이 없으면(나간 사람 등) 원래대로 돌린다.
//  측정값·판정은 기존 그대로(서버) — 이 스크립트는 보이는 모습만 맞춘다.
// ============================================================
public struct ProbePoseBroadcast : IBroadcast
{
    public int objectId;
    public bool red;
    public bool isOut;
    public Vector3 tipLocal;
    public Vector3 tailLocal;
}

public static class ProbePoseNet
{
    private const float SendInterval = 0.1f;
    private static FishNet.Managing.Server.ServerManager s_sm;
    private static FishNet.Managing.Client.ClientManager s_cm;
    private static readonly Dictionary<long, float> s_nextSend = new Dictionary<long, float>();
    private static readonly Dictionary<long, bool> s_lastOut = new Dictionary<long, bool>();

    private static void EnsureRegistered()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (sm != null && sm != s_sm)
        {
            if (s_sm != null) s_sm.UnregisterBroadcast<ProbePoseBroadcast>(OnServer);
            sm.RegisterBroadcast<ProbePoseBroadcast>(OnServer);
            s_sm = sm;
        }
        if (cm != null && cm != s_cm)
        {
            if (s_cm != null) s_cm.UnregisterBroadcast<ProbePoseBroadcast>(OnClient);
            cm.RegisterBroadcast<ProbePoseBroadcast>(OnClient);
            s_cm = cm;
        }
    }

    /// <summary>서버·클라이언트 수신 등록 — 계측기(MultimeterMaster)가 생길 때 부른다.</summary>
    public static void Init() => EnsureRegistered();

    /// <summary>내 화면에서 프로브가 나와 있으면 매 프레임 불러도 된다(내부에서 0.1초 간격으로 줄인다).</summary>
    public static void Report(Component body, bool red, bool isOut, Vector3 tipWorld, Vector3 tailWorld)
    {
        if (body == null || !InstanceFinder.IsClientStarted) return;
        var nob = body.GetComponentInParent<NetworkObject>();
        if (nob == null || !nob.IsSpawned) return;
        EnsureRegistered();

        long key = ((long)nob.ObjectId << 1) | (red ? 1L : 0L);
        bool changed = !s_lastOut.TryGetValue(key, out bool last) || last != isOut;
        if (!isOut && !changed) return;   // 넣은 상태는 바뀔 때 한 번만
        if (!changed && s_nextSend.TryGetValue(key, out float next) && Time.unscaledTime < next) return;
        s_nextSend[key] = Time.unscaledTime + SendInterval;
        s_lastOut[key] = isOut;

        var t = nob.transform;
        InstanceFinder.ClientManager.Broadcast(new ProbePoseBroadcast
        {
            objectId = nob.ObjectId,
            red = red,
            isOut = isOut,
            tipLocal = t.InverseTransformPoint(tipWorld),
            tailLocal = t.InverseTransformPoint(tailWorld),
        });
    }

    private static void OnServer(NetworkConnection conn, ProbePoseBroadcast msg, Channel channel)
    {
        InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true, Channel.Unreliable);
    }

    private static void OnClient(ProbePoseBroadcast msg, Channel channel)
    {
        var cm = InstanceFinder.ClientManager;
        if (cm == null || !cm.Objects.Spawned.TryGetValue(msg.objectId, out var nob) || nob == null) return;
        var view = nob.GetComponent<RemoteProbeView>() ?? nob.gameObject.AddComponent<RemoteProbeView>();
        view.Apply(msg);
    }
}

/// <summary>다른 사람이 꺼낸 프로브를 내 화면에 그린다(ProbePoseNet 이 붙인다).</summary>
[DefaultExecutionOrder(300)]
public class RemoteProbeView : MonoBehaviour
{
    private const float Timeout = 2f;
    private const int CordSegments = 20;
    private static Material s_mat;

    private class View
    {
        public bool red;
        public bool isOut;
        public float lastAt;
        public Vector3 tip, tail;          // 본체 기준 로컬(받은 값)
        public Vector3 shownTip, shownTail; // 월드(부드럽게)
        public bool hasShown;
        public Transform socket;
        public SkinnedMeshRenderer cable;
        public GameObject pen;
        public LineRenderer cord;
    }

    private View _red, _black;

    public void Apply(ProbePoseBroadcast msg)
    {
        var v = msg.red ? (_red ??= Build(true)) : (_black ??= Build(false));
        v.isOut = msg.isOut;
        v.lastAt = Time.unscaledTime;
        v.tip = msg.tipLocal;
        v.tail = msg.tailLocal;
        if (!msg.isOut) Hide(v);
    }

    private View Build(bool red)
    {
        string color = red ? "Red" : "Black";
        var v = new View { red = red };
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if ((t.name.EndsWith($"_{color}_Root") || t.name.EndsWith($"Cable_{color}")) && t.GetComponent<Renderer>() == null) { v.socket = t; break; }
        if (v.socket == null) v.socket = transform;
        var master = GetComponent<MultimeterMaster>();
        var probe = master != null ? (red ? master.redProbe : master.blackProbe) : null;
        if (probe != null) v.cable = probe.GetComponent<SkinnedMeshRenderer>();

        if (s_mat == null) s_mat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        Color body = red ? new Color(0.85f, 0.08f, 0.08f) : new Color(0.1f, 0.1f, 0.1f);

        v.pen = new GameObject(red ? "[RemoteProbePen_Red]" : "[RemoteProbePen_Black]");
        v.pen.layer = 1;   // TransparentFX — VR 카메라도 그린다
        var penLr = v.pen.AddComponent<LineRenderer>();
        penLr.useWorldSpace = true; penLr.positionCount = 3; penLr.widthMultiplier = 0.012f;
        penLr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.24f, 0.25f), new Keyframe(0.26f, 1f), new Keyframe(1f, 1f));
        penLr.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(0.75f, 0.75f, 0.78f), 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.78f), 0.24f), new GradientColorKey(body, 0.26f), new GradientColorKey(body, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        };
        penLr.sharedMaterial = s_mat;
        penLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        v.pen.SetActive(false);

        var cordGo = new GameObject(red ? "[RemoteProbeCord_Red]" : "[RemoteProbeCord_Black]");
        cordGo.layer = 1;
        v.cord = cordGo.AddComponent<LineRenderer>();
        v.cord.useWorldSpace = true; v.cord.positionCount = CordSegments; v.cord.widthMultiplier = 0.006f; v.cord.numCapVertices = 2;
        v.cord.sharedMaterial = s_mat;
        v.cord.startColor = v.cord.endColor = red ? new Color(0.7f, 0.06f, 0.06f) : new Color(0.06f, 0.06f, 0.06f);
        v.cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        v.cord.enabled = false;
        return v;
    }

    private void LateUpdate()
    {
        Draw(_red);
        Draw(_black);
    }

    private void Draw(View v)
    {
        if (v == null || !v.isOut) return;
        if (Time.unscaledTime - v.lastAt > Timeout) { v.isOut = false; Hide(v); return; }

        Vector3 tip = transform.TransformPoint(v.tip), tail = transform.TransformPoint(v.tail);
        float k = v.hasShown ? 1f - Mathf.Exp(-18f * Time.deltaTime) : 1f;   // 0.1초 간격 값 사이를 부드럽게
        v.shownTip = Vector3.Lerp(v.shownTip, tip, k);
        v.shownTail = Vector3.Lerp(v.shownTail, tail, k);
        v.hasShown = true;

        if (v.cable != null && v.cable.enabled) v.cable.enabled = false;
        if (!v.pen.activeSelf) v.pen.SetActive(true);
        var penLr = v.pen.GetComponent<LineRenderer>();
        penLr.SetPosition(0, v.shownTip);
        penLr.SetPosition(1, Vector3.Lerp(v.shownTip, v.shownTail, 0.25f));
        penLr.SetPosition(2, v.shownTail);

        Vector3 a = v.socket.position, b = v.shownTail;
        Vector3 dir = (v.shownTip - v.shownTail).normalized;
        float d = Vector3.Distance(a, b);
        float sag = Mathf.Clamp((2.6f - d) * 0.35f, 0.03f, 0.5f);
        Vector3 c1 = a + Vector3.down * sag;
        Vector3 c2 = b - dir * Mathf.Min(0.12f, d * 0.3f) + Vector3.down * sag * 0.6f;
        if (!v.cord.enabled) v.cord.enabled = true;
        for (int i = 0; i < CordSegments; i++)
        {
            float t = (float)i / (CordSegments - 1), u = 1f - t;
            v.cord.SetPosition(i, u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b);
        }
    }

    private static void Hide(View v)
    {
        v.hasShown = false;
        if (v.pen != null) v.pen.SetActive(false);
        if (v.cord != null) v.cord.enabled = false;
        if (v.cable != null) v.cable.enabled = true;
    }

    private void OnDestroy()
    {
        foreach (var v in new[] { _red, _black })
        {
            if (v == null) continue;
            if (v.pen != null) Destroy(v.pen);
            if (v.cord != null) Destroy(v.cord.gameObject);
        }
    }
}
