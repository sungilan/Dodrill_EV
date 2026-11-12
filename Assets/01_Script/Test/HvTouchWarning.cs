using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  HvTouchWarning.cs
//
//  VR 전용 — 고전압 부품 접촉 경고. 아직 안전하지 않은 상태에서 손(손바닥·검지 끝)이 고전압 부품에 닿으면
//  · 손이 잠깐 빨갛게 변하고, 그 손에 강한 진동 + 경고음
//  · 시선 앞에 빨간 카드: 왜 위험한지(절연장갑 미착용 / MSD 차단 전 / 방전 대기 중)
//  안전 상태는 ElectricSafetyManager(서버가 진행시킨 단계 기준)와 현재 단계(DischargeWait)로 판단한다.
//  교육·평가 중(시나리오 진행 중)에만 동작하고, 감점은 하지 않는다 — 실제 사고 처리(부품을 잡는 순간 감전 연출·평가 기록)는
//  기존 InteractablePart 가 맡고, 이것은 그 전에 '닿기만 해도' 알려 주는 예고다.
//  고전압 부품: 측정 단자(MeasurementPoint, 접지 제외), 고전압 커넥터(Conn_Front / Conn_Rear / Conn_ICCU).
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class HvTouchWarning : MonoBehaviour
{
    private const float Margin = 0.02f;          // 부품 경계에서 이만큼 바깥까지 닿은 것으로 본다
    private const float HandCooldown = 4f;
    private const float TintSeconds = 0.8f;
    private static readonly string[] HvConnectorNames = { "Conn_Front", "Conn_Rear", "Conn_ICCU" };
    private const string DischargeModuleId = "DischargeWait";

    private static readonly Color CardRed = new Color(0.6f, 0.03f, 0.03f, 0.95f);
    private static readonly Color HandRed = new Color(1f, 0.15f, 0.12f, 1f);

    private class HandState
    {
        public Hand hand;
        public float cooldownUntil;
        public float tintUntil;
        public readonly List<Renderer> tinted = new List<Renderer>();
        public readonly List<MaterialPropertyBlock> saved = new List<MaterialPropertyBlock>();
    }

    private readonly List<HandState> _hands = new List<HandState>();
    private float _nextHandScan;
    private readonly List<Transform> _targets = new List<Transform>();
    private float _nextTargetScan;
    private ScenarioStateReceiver _recv;
    private static MaterialPropertyBlock s_mpb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<HvTouchWarning>() != null) return;
        var go = new GameObject("[HvTouchWarning]");
        DontDestroyOnLoad(go);
        go.AddComponent<HvTouchWarning>();
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        float now = Time.unscaledTime;
        if (now >= _nextHandScan) { _nextHandScan = now + 2f; ScanHands(); }
        foreach (var s in _hands) if (s.tinted.Count > 0 && now >= s.tintUntil) Untint(s);

        string reason = UnsafeReason();
        if (reason == null) return;

        if (now >= _nextTargetScan) { _nextTargetScan = now + 3f; ScanTargets(); }
        if (_targets.Count == 0) return;

        foreach (var s in _hands)
        {
            if (s.hand == null || !s.hand.isActiveAndEnabled || now < s.cooldownUntil) continue;
            Transform hit = Touching(s.hand);
            if (hit == null) continue;

            s.cooldownUntil = now + HandCooldown;
            Debug.Log($"[HvTouch] {s.hand.name} → {hit.name} 접촉 ({reason})");
            try { s.hand.PlayHapticVibration(0.3f, 1f); } catch { }
            WorldSfx.PlayAt("Beep_Error", hit.position, 1f, 0.9f);
            Tint(s);
            WarningPopup.Show("hv", "고전압 위험 — 손을 떼세요", reason, CardRed, 4f, 1);
        }
    }

    // ── 안전 상태 ─────────────────────────────

    /// <summary>지금 고전압 부품에 손대면 안 되는 이유. 안전하면 null.</summary>
    private string UnsafeReason()
    {
        var esm = ElectricSafetyManager.Instance;
        if (esm == null || !esm.IsScenarioActive || esm.bypassSafetyForTesting) return null;
        if (!esm.isGlovesEquipped) return "절연장갑을 먼저 착용하세요";
        if (!esm.isMSDRemoved) return "MSD 차단 전 — 아직 고전압이 흐르고 있습니다";

        if (_recv == null) _recv = ScenarioStateReceiver.Instance;
        var tasks = _recv != null ? _recv.CurrentScenario?.scenario?.tasks : null;
        if (tasks == null) return null;
        int discharge = -1;
        for (int i = 0; i < tasks.Count; i++)
            if (tasks[i] != null && string.Equals(tasks[i].moduleId, DischargeModuleId, System.StringComparison.OrdinalIgnoreCase)) { discharge = i; break; }
        if (discharge < 0) return null;
        var st = _recv.CurrentTaskState;
        bool waiting = st.taskIndex < discharge || (st.taskIndex == discharge && st.status != TaskStatus.Completed);
        return waiting ? "방전 대기 중 — 대기 시간이 끝난 뒤 작업하세요" : null;
    }

    // ── 접촉 판정 ─────────────────────────────

    private void ScanTargets()
    {
        _targets.Clear();
        foreach (var mp in FindObjectsByType<MeasurementPoint>(FindObjectsSortMode.None))
        {
            if (mp == null || mp.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            _targets.Add(mp.transform);
        }
        foreach (var n in HvConnectorNames)
        {
            var go = GameObject.Find(n);
            if (go != null) _targets.Add(go.transform);
        }
    }

    private Transform Touching(Hand h)
    {
        Vector3 palm = h.palmTransform != null ? h.palmTransform.position : h.transform.position;
        Vector3 tip = palm;
        if (h.fingers != null)
            foreach (var f in h.fingers)
                if (f != null && f.fingerType == FingerEnum.index && f.tip != null) { tip = f.tip.position; break; }

        foreach (var t in _targets)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Bounds b = TargetBounds(t);
            b.Expand(Margin * 2f);
            if (b.Contains(palm) || b.Contains(tip)) return t;
        }
        return null;
    }

    private static Bounds TargetBounds(Transform t)
    {
        bool any = false; Bounds b = default;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any) return b;
        foreach (var c in t.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        return any ? b : new Bounds(t.position, Vector3.one * 0.05f);
    }

    // ── 손 빨갛게 ─────────────────────────────

    private static void Tint(HandState s)
    {
        if (s.tinted.Count > 0) { s.tintUntil = Time.unscaledTime + TintSeconds; return; }
        if (s_mpb == null) s_mpb = new MaterialPropertyBlock();
        foreach (var r in s.hand.GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer || r is ParticleSystemRenderer || r.GetComponentInParent<Grabbable>() != null) continue;
            var save = new MaterialPropertyBlock();
            r.GetPropertyBlock(save);
            s.tinted.Add(r); s.saved.Add(save);
            r.GetPropertyBlock(s_mpb);
            s_mpb.SetColor("_BaseColor", HandRed);
            s_mpb.SetColor("_Color", HandRed);
            r.SetPropertyBlock(s_mpb);
        }
        s.tintUntil = Time.unscaledTime + TintSeconds;
    }

    private static void Untint(HandState s)
    {
        for (int i = 0; i < s.tinted.Count; i++)
            if (s.tinted[i] != null) s.tinted[i].SetPropertyBlock(s.saved[i]);
        s.tinted.Clear(); s.saved.Clear();
    }

    private void ScanHands()
    {
        _hands.RemoveAll(s => s.hand == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Exists(x => x.hand == hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hands.Add(new HandState { hand = hand });
        }
    }

    private void OnDestroy()
    {
        foreach (var s in _hands) Untint(s);
    }
}
