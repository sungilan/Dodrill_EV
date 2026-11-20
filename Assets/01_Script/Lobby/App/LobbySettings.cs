using UnityEngine;

// ============================================================
//  LobbySettings.cs — 로비 설정 한 곳 (모듈화, 2026-11-20)
//
//  예전에는 씬 이름·로비 팩토리 id·기본 시나리오가 여러 파일에 문자열로 박혀 있었다.
//  이식하는 프로젝트는 Resources/LobbySettings.asset 의 값만 바꾸면 된다(없으면 아래 기본값 = EV 값).
//  만들기: Project 창 우클릭 ▸ Create ▸ DoDrill ▸ Lobby Settings  (파일 이름은 반드시 LobbySettings, Resources 폴더 안)
// ============================================================
[CreateAssetMenu(menuName = "DoDrill/Lobby Settings", fileName = "LobbySettings")]
public sealed class LobbySettings : ScriptableObject
{
    [Header("씬 이름 (빌드 설정의 씬 목록에 있어야 한다)")]
    [Tooltip("로비(클라이언트) 씬 이름. 대기실이 Preparations 상태가 되거나 마스터 재접속 보호가 돌아올 때 쓴다")]
    public string clientSceneName = "Client";
    [Tooltip("훈련이 실제로 진행되는 씬 이름. 룸 서버가 이 씬을 로드하고(RoomOnlineScene), 클라이언트는 이 씬이 뜨면 로딩 막을 걷는다")]
    public string gameSceneName = "Game";

    [Header("MST 로비")]
    [Tooltip("마스터 서버의 로비 팩토리 id (MyLobbyFactoryModule 이 등록하는 값과 방 만들기가 요청하는 값)")]
    public string lobbyFactoryId = "Game";

    [Header("시나리오")]
    [Tooltip("방 옵션에 scenarioId 가 없을 때(룸 서버 ScenarioAutoLoader)와 씬 폴백으로 쓰는 시나리오 id")]
    public string defaultScenarioId = "ev_p0aa6_battery_insulation";
    [Tooltip("시나리오 JSON 을 읽는 Resources 하위 폴더")]
    public string scenarioResourcePath = "Scenarios";
    [Tooltip("시나리오 카드 썸네일을 읽는 Resources 하위 폴더")]
    public string thumbnailResourcePath = "Thumbnails";

    private static LobbySettings _current;

    /// <summary>Resources/LobbySettings 에셋. 없으면 기본값(EV) 인스턴스를 만들어 쓴다.</summary>
    public static LobbySettings Current
    {
        get
        {
            if (_current == null)
            {
                _current = Resources.Load<LobbySettings>("LobbySettings");
                if (_current == null) _current = CreateInstance<LobbySettings>();
            }
            return _current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _current = null;
}
