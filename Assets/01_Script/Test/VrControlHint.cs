using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VrControlHint.cs
//
//  VR 전용 — 도구를 잡으면 손 위에 조작 방법을 잠깐 띄운다(CMS VR 의 컨트롤러 조작 안내 참고).
//  · 렌치: "트리거를 당긴 채 볼트에 대세요" / 그리스: "트리거: 그리스 바르기"
//  · 같은 종류 안내는 세션당 3번까지만, 한 번에 5초 — 익숙해지면 더 이상 방해하지 않는다.
//  · 멀티미터는 VrProbeHandling 이 자체 안내를 띄우므로 제외.
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrControlHint : MonoBehaviour
{
    private const float ShowSeconds = 5f;
    private const int MaxShowsPerKind = 3;
    private const string ReleaseLine = "그립을 놓으면 내려놓기";

    private class HandState
    {
        public Hand hand;
        public Grabbable lastHeld;
        public GameObject label;
        public float hideAt;
    }

    private readonly List<HandState> _hands = new List<HandState>();
    private readonly Dictionary<string, int> _shownCount = new Dictionary<string, int>();
    private float _nextHandScan;
    private static GameObject s_prefab;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrControlHint>() != null) return;
        var go = new GameObject("[VrControlHint]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrControlHint>();
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }

        foreach (var s in _hands)
        {
            if (s.hand == null) continue;
            var held = s.hand.GetHeld();
            if (held != s.lastHeld)
            {
                s.lastHeld = held;
                Hide(s);
                if (held != null) TryShow(s, held);
            }
            if (s.label != null && Time.unscaledTime >= s.hideAt) Hide(s);
        }
    }

    private void LateUpdate()
    {
        foreach (var s in _hands)
        {
            if (s.label == null || s.hand == null) continue;
            Vector3 p = s.hand.palmTransform != null ? s.hand.palmTransform.position : s.hand.transform.position;
            s.label.transform.position = Vector3.Lerp(s.label.transform.position, p + Vector3.up * 0.14f, Time.deltaTime * 12f);
        }
    }

    private void ScanHands()
    {
        _hands.RemoveAll(s => { if (s.hand != null) return false; Hide(s); return true; });
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Exists(x => x.hand == hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hands.Add(new HandState { hand = hand });
        }
    }

    private void TryShow(HandState s, Grabbable held)
    {
        if (!TryGetHint(held, out string kind, out string text)) return;
        _shownCount.TryGetValue(kind, out int n);
        if (n >= MaxShowsPerKind) return;
        _shownCount[kind] = n + 1;

        if (s_prefab == null) s_prefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (s_prefab == null) return;
        Vector3 p = s.hand.palmTransform != null ? s.hand.palmTransform.position : s.hand.transform.position;
        var go = Instantiate(s_prefab, p + Vector3.up * 0.08f, Quaternion.identity);
        go.name = "[VrControlHint]";
        go.transform.localScale *= 0.7f;   // 손 가까이 뜨므로 이름표보다 작게
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
        var label = go.GetComponent<WorldHoverLabel>();
        if (label != null) label.SetText(text, ReleaseLine);
        go.AddComponent<UIHoverFader>();
        s.label = go;
        s.hideAt = Time.unscaledTime + ShowSeconds;
    }

    private static bool TryGetHint(Grabbable held, out string kind, out string text)
    {
        kind = text = null;
        var root = held.transform;
        if (Has<MultimeterMaster>(root)) return false;   // 프로브 안내는 VrProbeHandling 이 담당
        if (Has<ImpactWrench>(root)) { kind = "wrench"; text = "트리거를 당긴 채 볼트에 대세요"; return true; }
        if (Has<GreaseTool>(root)) { kind = "grease"; text = "트리거: 그리스 바르기"; return true; }
        return false;
    }

    private static bool Has<T>(Transform t) where T : Component
        => t.GetComponentInChildren<T>(true) != null || t.GetComponentInParent<T>() != null;

    private static void Hide(HandState s)
    {
        if (s.label == null) return;
        var f = s.label.GetComponent<UIHoverFader>();
        if (f != null) f.FadeOut(); else Destroy(s.label);
        s.label = null;
    }
}
