using FishNet;
using UnityEngine;

// ============================================================
//  RoomIdleShutdown.cs — (11/20) 아무도 없는 룸 서버 자동 종료
//
//  룸 서버는 MST 기본 설정상 '마지막 플레이어가 나가면' 꺼진다. 그런데 방이 떴는데 아무도 접속하지 못하면
//  (클라이언트·서버 빌드 불일치로 접속 실패 등) 그 조건이 오지 않아 프로세스가 계속 남았다.
//  스포너 한도(maxProcesses 30)가 이런 빈 방으로 차면 새 훈련 시작이 'Servers are busy' 로 거절됐다.
//  → 전용 서버(배치 모드, 서버만 켜짐)에서 접속자가 0명인 상태가 일정 시간 이어지면 스스로 종료한다.
//     · 처음 뜬 뒤 아무도 안 들어옴: FirstJoinTimeout 초
//     · 들어왔다가 모두 나감(MST 종료가 안 된 경우 대비): EmptyTimeout 초
//  에디터·혼자하기(호스트)·클라이언트에서는 동작하지 않는다.
// ============================================================
public sealed class RoomIdleShutdown : MonoBehaviour
{
    public const float FirstJoinTimeout = 180f;
    public const float EmptyTimeout = 120f;

    private float _emptySince = -1f;
    private bool _everJoined;
    private bool _quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || !Application.isBatchMode) return;
        var go = new GameObject("[DoDrill] RoomIdleShutdown");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<RoomIdleShutdown>();
    }

    private float _nextCheck;

    private void Update()
    {
        if (_quitting || Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 5f;

        // FishNet 서버가 켜진 전용 서버(룸 서버)만. 마스터 서버는 FishNet 서버를 켜지 않으므로 해당 없음
        if (FishNet.Managing.NetworkManager.Instances.Count == 0) return;
        if (!InstanceFinder.IsServerStarted || InstanceFinder.IsClientStarted) { _emptySince = -1f; return; }

        int clients = InstanceFinder.ServerManager.Clients.Count;
        if (clients > 0)
        {
            _everJoined = true;
            _emptySince = -1f;
            return;
        }

        if (_emptySince < 0f) _emptySince = Time.unscaledTime;
        float limit = _everJoined ? EmptyTimeout : FirstJoinTimeout;
        float idle = Time.unscaledTime - _emptySince;
        if (idle < limit) return;

        _quitting = true;
        Debug.LogWarning($"[RoomIdleShutdown] 접속자 0명 상태 {idle:F0}초 ({(_everJoined ? "모두 나감" : "아무도 접속 안 함")}) → 룸 서버 종료");
        Application.Quit();
    }
}
