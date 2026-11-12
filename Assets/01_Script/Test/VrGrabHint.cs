using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VrGrabHint.cs
//
//  VR 전용 — 빈손을 잡을 수 있는 물건 가까이 가져가면 손이 닿을 지점에 작은 원을 띄운다(CMS VR 참고).
//  · 하늘색 원: 잡을 수 있는 물건이 근처에 있음
//  · 초록 원:   지금 그립을 누르면 이 물건이 잡힘(AutoHand 하이라이트 대상)
//  · UI 포인터: 물건을 잡고 있는 손은 UI 레이저를 끈다(드릴로 볼트 풀다가 메뉴가 눌리던 문제). 빈손이면 왼손·오른손 모두 켠다.
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrGrabHint : MonoBehaviour
{
    // 잡는 지점 원: 위치가 튀는 문제가 있어 일단 끔(2026-11-12). UI 포인터 차단은 계속 동작한다.
    private static readonly bool ShowGrabRing = false;
    private const float SearchRadius = 0.16f;
    private const float ScanInterval = 0.08f;
    private static readonly Color NearColor = new Color(0.45f, 0.85f, 1f, 0.9f);
    private static readonly Color ReadyColor = new Color(0.3f, 1f, 0.45f, 1f);

    private class HandHint
    {
        public Hand hand;
        public LineRenderer ring;
        public bool show;
        public Vector3 point;
        public bool ready;
        public HandCanvasPointer[] pointers;
        public bool pointerOn = true;
    }

    private readonly List<HandHint> _hints = new List<HandHint>();
    private readonly Collider[] _buf = new Collider[48];
    private float _nextHandScan, _nextScan;
    private Material _mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrGrabHint>() != null) return;
        var go = new GameObject("[VrGrabHint]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrGrabHint>();
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;

        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + ScanInterval;
            foreach (var h in _hints) Evaluate(h);
        }
        foreach (var h in _hints) UpdatePointer(h);
    }

    private void LateUpdate()
    {
        foreach (var h in _hints) Draw(h);
    }

    private void ScanHands()
    {
        _hints.RemoveAll(h =>
        {
            if (h.hand != null) return false;
            if (h.ring != null) Destroy(h.ring.gameObject);
            return true;
        });
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hints.Exists(x => x.hand == hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hints.Add(new HandHint { hand = hand, pointers = hand.GetComponentsInChildren<HandCanvasPointer>(true) });
        }
    }

    private void Evaluate(HandHint h)
    {
        h.show = false;
        if (!ShowGrabRing) return;
        var hand = h.hand;
        if (hand == null || !hand.isActiveAndEnabled || hand.GetHeld() != null) return;

        Vector3 palm = hand.palmTransform != null ? hand.palmTransform.position : hand.transform.position;
        int n = Physics.OverlapSphereNonAlloc(palm, SearchRadius, _buf, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Grabbable bestGrab = null;
        for (int i = 0; i < n; i++)
        {
            var col = _buf[i];
            if (col == null || !col.enabled) continue;
            var g = col.GetComponentInParent<Grabbable>();
            if (g == null || !g.enabled || !g.isGrabbable || g.IsHeld()) continue;
            if (col.GetComponentInParent<Hand>() != null) continue;
            Vector3 p = ClosestPoint(col, palm);
            float d = (p - palm).sqrMagnitude;
            if (d < best) { best = d; h.point = p; bestGrab = g; }
        }
        if (bestGrab == null) return;
        h.show = true;
        h.ready = hand.lookingAtObj == bestGrab;
    }

    /// <summary>잡고 있는 손의 UI 포인터는 끄고, 빈손이면 켠다.</summary>
    private static void UpdatePointer(HandHint h)
    {
        if (h.hand == null || h.pointers == null) return;
        bool want = h.hand.GetHeld() == null && h.hand.holdingObj == null;
        if (want == h.pointerOn) return;
        h.pointerOn = want;
        foreach (var p in h.pointers)
        {
            if (p == null) continue;
            if (!want)
            {
                // OnDisable 은 레이를 숨기지 않으므로 직접 숨긴다
                if (p.hitPointMarker != null) p.hitPointMarker.SetActive(false);
                var lr = p.GetComponent<LineRenderer>();
                if (lr != null) lr.enabled = false;
            }
            else if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p.enabled = want;
        }
    }

    private static Vector3 ClosestPoint(Collider col, Vector3 p)
    {
        // 볼록하지 않은 MeshCollider 는 Physics.ClosestPoint 를 쓸 수 없다
        if (col is MeshCollider mc && !mc.convex) return col.bounds.ClosestPoint(p);
        return col.ClosestPoint(p);
    }

    private void Draw(HandHint h)
    {
        if (!h.show)
        {
            if (h.ring != null && h.ring.gameObject.activeSelf) h.ring.gameObject.SetActive(false);
            return;
        }
        if (h.ring == null) h.ring = MakeRing();
        if (!h.ring.gameObject.activeSelf) h.ring.gameObject.SetActive(true);

        var cam = Camera.main;
        Vector3 nrm = cam != null ? (cam.transform.position - h.point).normalized : Vector3.up;
        Vector3 u = Vector3.Cross(nrm, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(nrm, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(nrm, u);

        float pulse = h.ready ? 0f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        float r = (h.ready ? 0.016f : 0.02f) * (1f + 0.15f * pulse);
        h.ring.startColor = h.ring.endColor = h.ready ? ReadyColor : NearColor;
        h.ring.widthMultiplier = h.ready ? 0.003f : 0.0022f;
        int cnt = h.ring.positionCount;
        Vector3 c = h.point + nrm * 0.004f;
        for (int i = 0; i < cnt; i++)
        {
            float a = Mathf.PI * 2f * i / cnt;
            h.ring.SetPosition(i, c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
        }
    }

    private LineRenderer MakeRing()
    {
        if (_mat == null)
        {
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            _mat = new Material(sh);
            _mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _mat.renderQueue = 4000;
        }
        var go = new GameObject("[VrGrabHintRing]");
        go.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 32;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = _mat;
        return lr;
    }
}
