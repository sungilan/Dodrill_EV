using MasterServerToolkit.MasterServer;
using UnityEngine;

// ============================================================
//  SpawnerWatchdog.cs — (11/20) 마스터 서버 스포너 자동 복구
//
//  11/20 17:48 마스터 내부 소켓(localhost:9035)에서 프레임 읽기 오류로 연결이 끊긴 뒤,
//  MST 커넥터의 재접속이 인증 단계에서 실패(Validation timeout)하고 재시도를 멈춰
//  스포너가 계속 꺼져 있었다 → 모든 훈련 시작이 'Servers are busy' 로 거절됨.
//  → 한 번이라도 스포너가 등록됐던 프로세스(=마스터)에서 주기적으로 상태를 보고
//     · 마스터 연결이 끊겨 있으면 다시 접속 (접속되면 SpawnerBehaviour 가 스스로 스포너 등록)
//     · 연결은 됐는데 스포너가 등록돼 있지 않으면 다시 등록
//  클라이언트·룸 서버에는 SpawnerBehaviour 가 없어 동작하지 않는다.
// ============================================================
public sealed class SpawnerWatchdog : MonoBehaviour
{
    public const float CheckInterval = 30f;
    // 접속/등록 요청 뒤 결과를 기다리는 시간. 그 안에 다시 건드리지 않는다
    public const float GraceAfterAction = 20f;

    private bool _everRegistered;
    private float _nextCheck;
    private float _busyUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor) return;
        var go = new GameObject("[DoDrill] SpawnerWatchdog");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<SpawnerWatchdog>();
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextCheck) return;
        _nextCheck = now + CheckInterval;

        var spawner = FindSpawner();
        if (spawner == null) return;

        if (spawner.IsSpawnerRegistered) { _everRegistered = true; return; }
        // 처음 등록 전(정상 기동 중)에는 MST 기본 흐름에 맡긴다
        if (!_everRegistered || now < _busyUntil) return;

        var connector = FindConnector();
        var conn = connector != null ? connector.Connection : null;
        if (connector == null || conn == null) return;

        if (!conn.IsConnected)
        {
            Debug.LogWarning("[SpawnerWatchdog] 스포너가 마스터와 끊긴 상태 → 다시 접속합니다");
            conn.Close(false); // 반쯤 열린 소켓이 남아 있으면 정리(끊김 이벤트는 이미 처리됐으므로 다시 안 띄움)
            _busyUntil = now + GraceAfterAction;
            connector.StartConnection();
            return;
        }

        // 연결은 살아 있는데 스포너가 없음 → 상태를 비우고 다시 등록
        Debug.LogWarning("[SpawnerWatchdog] 마스터 연결은 정상인데 스포너가 등록돼 있지 않음 → 다시 등록합니다");
        _busyUntil = now + GraceAfterAction;
        if (spawner.IsSpawnerStarted) spawner.StopSpawner();
        spawner.StartSpawner();
    }

    private static SpawnerBehaviour FindSpawner()
    {
        return Object.FindFirstObjectByType<SpawnerBehaviour>();
    }

    private static ClientToMasterConnector FindConnector()
    {
        return Object.FindFirstObjectByType<ClientToMasterConnector>();
    }
}
