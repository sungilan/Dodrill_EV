using System.Collections;
using FishNet;
using UnityEngine;
using XRAirpotrSecurity;

/// <summary>
/// 내 닉네임·플랫폼을 서버에 알린다 (ClientSessionInfoBroadcast).
///
/// 문제: 닉네임 전송을 NetworkedSessionUserList(PC_UI 캔버스 안)가 맡고 있어, 모바일처럼 PC_UI 가 꺼진 기기는
///       서버에 닉네임이 가지 않았다 → 접속자 목록에 'Player 2'(PC 아이콘), 완료 알림에 'Player 1' 로 표시.
/// 해결: UI 와 상관없이 항상 도는 전송기. 연결(clientId)이 바뀌거나 닉네임이 바뀌면 다시 보낸다.
///       PC·VR 은 목록도 보내므로 두 번 가지만 서버는 같은 값으로 덮어쓰기만 하므로 무해.
/// </summary>
public class SessionNicknameSender : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode) return;                    // 전용 서버 빌드는 클라이언트가 아님
        if (FindFirstObjectByType<SessionNicknameSender>() != null) return;

        var go = new GameObject("[SessionNicknameSender]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<SessionNicknameSender>();
    }

    private int _sentClientId = int.MinValue;
    private string _sentName;

    private IEnumerator Start()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            TrySend();
            yield return wait;
        }
    }

    private void TrySend()
    {
        var cm = InstanceFinder.ClientManager;
        var conn = cm != null ? cm.Connection : null;
        if (conn == null || !conn.IsActive || !cm.Started)
        {
            _sentClientId = int.MinValue;                       // 연결이 끊겼다 다시 붙으면 새로 보낸다
            return;
        }

        string name = NetworkedSessionUserList.ResolveLocalDisplayName();
        if (string.IsNullOrWhiteSpace(name)) return;            // 닉네임이 정해질 때까지 기다림

        if (conn.ClientId == _sentClientId && name == _sentName) return;

        cm.Broadcast(new ClientSessionInfoBroadcast
        {
            displayName = name,
            platform = NetworkedSessionUserList.PlatformToByte(Utils.GetPlatformType())
        });
        _sentClientId = conn.ClientId;
        _sentName = name;
        Debug.Log($"[SessionNicknameSender] 닉네임 전송 clientId={_sentClientId} name='{name}'");
    }
}
