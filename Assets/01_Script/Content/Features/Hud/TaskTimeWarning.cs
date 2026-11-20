using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  TaskTimeWarning.cs
//
//  단계 제한 시간 경고 — 모든 플랫폼. 예전에는 시간이 다 되면 아무 예고 없이 단계가 실패했다
//  (절연저항 측정 중 프로브를 못 꺼내 헤매다 120초가 지나 실패한 사례).
//  · 남은 30초: 시선 앞 주황 카드 "남은 시간 30초" + 경고음 + (VR) 양손 진동,
//               교육 모드면 다음 할 일 안내를 크게 강조하고 단계 안내 창을 다시 띄운다
//  · 남은 10초: 빨간 카드 + 경고음 + 진동
//  평가 모드에서는 안내를 자동으로 띄우지 않는다(힌트는 감점 항목) — 시간 경고만 한다.
//  제한 시간이 60초 미만인 단계는 30초 경고를 하지 않는다.
//  씬 배치 불필요 — 게임 시작 시 자동 생성된다.
// ============================================================
public class TaskTimeWarning : MonoBehaviour
{
    private const float FirstWarn = 30f;
    private const float FinalWarn = 10f;
    private const float EmphasizeSeconds = 12f;

    private static readonly Color Orange = new Color(0.55f, 0.3f, 0.02f, 0.92f);
    private static readonly Color Red = new Color(0.55f, 0.04f, 0.04f, 0.94f);

    private float _timeout, _elapsed;
    private bool _running;
    private string _taskKey;
    private bool _firstDone, _finalDone;

    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<TaskTimeWarning>() != null) return;
        var go = new GameObject("[TaskTimeWarning]");
        DontDestroyOnLoad(go);
        go.AddComponent<TaskTimeWarning>();
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnState;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshot;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnState;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshot;
    }

    private void OnState(TaskStateBroadcast b) => Apply(b.currentTask);
    private void OnSnapshot(ScenarioSnapshotBroadcast b) => Apply(b.currentTask);

    private void Apply(TaskState s)
    {
        _timeout = s.timeoutSeconds;
        _elapsed = s.elapsedTime;
        _running = s.status == TaskStatus.Running && _timeout > 0f;

        // 같은 단계라도 재도전이면 다시 경고한다
        string key = s.taskIndex + "/" + s.retryCount;
        if (key != _taskKey)
        {
            _taskKey = key;
            _firstDone = _finalDone = false;
            WarningPopup.Hide("time");
        }
        // 이미 지난 경고는 다시 하지 않는다(중간 접속·상태 재수신)
        float remaining = _timeout - _elapsed;
        if (remaining <= FirstWarn - 1f) _firstDone = true;
        if (remaining <= FinalWarn - 1f) _finalDone = true;
        if (!_running) WarningPopup.Hide("time");
    }

    private void Update()
    {
        if (!_running) return;
        _elapsed += Time.deltaTime;
        float remaining = _timeout - _elapsed;

        if (!_firstDone && _timeout >= 60f && remaining <= FirstWarn)
        {
            _firstDone = true;
            bool eval = EvaluationMode.ClientIsEvaluation;
            WarningPopup.Show("time", "남은 시간 30초", eval ? "서둘러 이 단계를 마치세요" : "안내 표시를 따라 진행하세요", Orange, 4f);
            Beep(0.7f, 1f);
            Rumble(0.2f, 0.5f);
            if (!eval)
            {
                NextActionGuide.Emphasize(EmphasizeSeconds);
                var gs = GuideSystem.Instance;
                if (gs != null && gs.IsGuiding) gs.ReopenGuideUI();
            }
        }

        if (!_finalDone && remaining <= FinalWarn)
        {
            _finalDone = true;
            _firstDone = true;
            WarningPopup.Show("time", "남은 시간 10초", "시간이 지나면 이 단계를 다시 해야 합니다", Red, 4f);
            Beep(0.9f, 1.15f);
            Rumble(0.35f, 0.8f);
            if (!EvaluationMode.ClientIsEvaluation) NextActionGuide.Emphasize(FinalWarn);
        }
    }

    private static void Beep(float vol, float pitch)
    {
        var cam = Camera.main;
        if (cam != null) WorldSfx.PlayAt("Beep_Error", cam.transform.position + cam.transform.forward * 0.3f, vol, pitch);
    }

    private void Rumble(float duration, float amp)
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (Time.unscaledTime >= _nextHandScan)
        {
            _nextHandScan = Time.unscaledTime + 5f;
            _hands.RemoveAll(h => h == null);
            foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
            {
                if (hand == null || _hands.Contains(hand)) continue;
                var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
                if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
                _hands.Add(hand);
            }
        }
        foreach (var h in _hands)
        {
            if (h == null || !h.isActiveAndEnabled) continue;
            try { h.PlayHapticVibration(duration, amp); } catch { }
        }
    }
}
