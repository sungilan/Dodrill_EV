using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VrHaptics.cs
//
//  VR 전용 — 손 진동 피드백. 예전에는 프로브 측정 외에는 진동이 전혀 없어 '잡았는지·돌아가는지'를 눈으로만 확인해야 했다.
//  · 물건을 잡는 순간: 짧은 '톡'
//  · 임팩트 렌치 작업 버튼을 누르는 동안: 약한 진동, 볼트에 물려 돌리는 동안은 조금 더 세게(드르륵)
//  · 렌치가 볼트에 처음 닿을 때: 짧은 '톡'
//  · 볼트 하나를 다 풀었을 때: 강한 한 번 / 다 조였을 때: 토크렌치처럼 '딸-깍' 두 번(조이는 동안은 끝에 갈수록 진동이 묵직해진다)
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrHaptics : MonoBehaviour
{
    private const float RumbleInterval = 0.09f;

    private class HandState
    {
        public Hand hand;
        public Bolt lastBolt;
        public float lastBoltProgress;
        public float nextRumble;
    }

    private readonly List<HandState> _hands = new List<HandState>();
    private float _nextHandScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrHaptics>() != null) return;
        var go = new GameObject("[VrHaptics]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrHaptics>();
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }

        foreach (var s in _hands)
        {
            if (s.hand == null || !s.hand.isActiveAndEnabled) continue;
            var held = s.hand.GetHeld();
            var wrench = held != null ? held.GetComponentInChildren<ImpactWrench>() : null;
            if (wrench == null) { s.lastBolt = null; continue; }

            var bolt = wrench.TargetBolt;

            // 볼트에 처음 닿음
            if (bolt != null && bolt != s.lastBolt) Play(s.hand, 0.04f, 0.3f);

            // 볼트 완료: 같은 볼트의 진행도가 1 에 도달했거나, 다 돌린 직후 대상에서 빠졌을 때
            if (s.lastBolt != null)
            {
                bool finishedNow = s.lastBolt.isloosened && s.lastBoltProgress < 1f;
                if (finishedNow)
                {
                    if (s.lastBolt.isAssembleMode)   // 조임 끝: 토크렌치처럼 '딸-깍' 두 번 (그동안 작업 진동은 잠시 멈춘다)
                    {
                        StartCoroutine(TorqueClickPulse(s.hand));
                        s.nextRumble = Time.unscaledTime + 0.25f;
                    }
                    else Play(s.hand, 0.15f, 0.9f);
                }
            }
            s.lastBolt = bolt;
            s.lastBoltProgress = bolt != null ? bolt.Progress : 0f;

            // 작업 중 진동
            if (wrench.IsWorking && Time.unscaledTime >= s.nextRumble)
            {
                s.nextRumble = Time.unscaledTime + RumbleInterval;
                // 조일 때는 끝에 가까울수록 묵직하게(저항이 커지는 느낌), 풀 때는 일정
                float amp = bolt == null ? 0.15f : (bolt.isAssembleMode ? Mathf.Lerp(0.3f, 0.75f, bolt.Progress * bolt.Progress) : 0.4f);
                Play(s.hand, RumbleInterval + 0.02f, amp);
            }
        }
    }

    private void ScanHands()
    {
        _hands.RemoveAll(s => s.hand == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Exists(x => x.hand == hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            hand.OnGrabbed += OnGrabbed;
            _hands.Add(new HandState { hand = hand });
        }
    }

    private void OnDestroy()
    {
        foreach (var s in _hands) if (s.hand != null) s.hand.OnGrabbed -= OnGrabbed;
    }

    private static void OnGrabbed(Hand hand, Grabbable grab) => Play(hand, 0.03f, 0.35f);

    private static System.Collections.IEnumerator TorqueClickPulse(Hand h)
    {
        Play(h, 0.03f, 1f);
        yield return new WaitForSecondsRealtime(0.06f);
        Play(h, 0.05f, 0.75f);
    }

    private static void Play(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}
