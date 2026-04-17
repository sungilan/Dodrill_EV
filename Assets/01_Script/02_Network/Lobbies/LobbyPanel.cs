using AYellowpaper.SerializedCollections;
using DoDrill.Training;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using XRAirpotrSecurity;

public class LobbyPanel : UIView
{
    [SerializeField] private LobbyPlayerInfos infoPrefab;
    [SerializeField] private RectTransform listContainer;
    [SerializeField] private TMP_Text statusInfoText;
    [SerializeField] private TMP_Dropdown stageDropdown;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button setAnchorButton;

    [Header("콘텐츠 정보")]
    [SerializeField] private Image thumbnailImage;
    [SerializeField] private Sprite defaultThumbnail;
    [SerializeField] private TMP_Text contentTitleText;

    private static JoinedLobby _lobby;
    public UnityEvent OnStartGameEvent;
    private RoomAccessPacket _currentRoomAccess;
    private bool readyStatus;
    public UIView LobbyListView;
    public GameObject startPanel;
    public TMP_Dropdown dropdown;
    private bool isHost = false;
    public TextMeshProUGUI roomTitleText;

    private string StageName =>
        stageDropdown != null && stageDropdown.options.Count > 0
            ? stageDropdown.options[stageDropdown.value].text
            : string.Empty;

    // ─────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showLobbyView, OnShowLobbyViewEventHandler);

        Managers.Sound.Play("MainTheme", Define.Sound.Bgm);
    }

    protected override void OnDestroy()
    {
        UnsubscribeLobbyEvents();
        base.OnDestroy();
    }

    protected void Start()
    {
        if (listContainer)
            foreach (Transform t in listContainer)
                Destroy(t.gameObject);

        // _lobby 가 있고 아직 나가지 않은 상태일 때만 표시
        // (타이틀 씬으로 복귀 후 ResetStaticState() 호출 시 _lobby = null 이므로 표시 안 됨)
        if (_lobby != null && !_lobby.HasLeft)
        {
            ResetLobby();
            DrawPlayersList();
        }
    }


    // ─────────────────────────────────────────
    // 이벤트 핸들러
    // ─────────────────────────────────────────

    private void OnShowLobbyViewEventHandler(EventMessage message)
    {
        LobbyListView.Hide();
        Mst.Events.Invoke(MstEventKeys.hideCreateLobbyView);
        Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);

        // 이전 로비 이벤트 구독 해제
        if (_lobby != null)
        {
            _lobby.OnLobbyStateChangeEvent -= OnLobbyStateChange;
            _lobby.OnLobbyStatusTextChangeEvent -= OnLobbyStatusTextChange;
            UnsubscribeLobbyEvents();
        }

        _lobby = message.As<JoinedLobby>();
        _lobby.OnLobbyStateChangeEvent += OnLobbyStateChange;
        _lobby.OnLobbyStatusTextChangeEvent += OnLobbyStatusTextChange;

        ResetLobby();
    }
    public static void ResetStaticState()
    {
        if (_lobby != null)
        {
            _lobby.OnLobbyStateChangeEvent -= null;
            _lobby.OnLobbyStatusTextChangeEvent -= null;
            try { _lobby.Leave(); } catch { }
            _lobby = null;
        }
    }
    private void ResetLobby()
    {
        _lobby.OnMemberJoinedEvent += OnMemberJoined;
        _lobby.OnMemberLeftEvent += OnMemberLeft;
        _lobby.OnMemberReadyStatusChangedEvent += OnMemberReadyStatusChanged;
        _lobby.OnMemberPropertyChangedEvent += OnMemberPropertyChanged;
        _lobby.OnMemberTeamChangedEvent += OnMemberTeamChangedEvent;

        bool isMasterUser = _lobby.IsMasterUser(Mst.Client.Auth.AccountInfo.Username);
        startGameButton.gameObject.SetActive(isMasterUser);

        RefreshContentInfo();
        Show();
    }

    private void UnsubscribeLobbyEvents()
    {
        if (_lobby == null) return;
        _lobby.OnMemberJoinedEvent -= OnMemberJoined;
        _lobby.OnMemberLeftEvent -= OnMemberLeft;
        _lobby.OnMemberReadyStatusChangedEvent -= OnMemberReadyStatusChanged;
        _lobby.OnMemberPropertyChangedEvent -= OnMemberPropertyChanged;
        _lobby.OnMemberTeamChangedEvent -= OnMemberTeamChangedEvent;
    }

    // ─────────────────────────────────────────
    // 콘텐츠 정보 표시
    // ─────────────────────────────────────────

    private void RefreshContentInfo()
    {
        _lobby.Properties.TryGetValue("scenarioId", out string scenarioId);

        if (string.IsNullOrEmpty(scenarioId))
        {
            if (contentTitleText) contentTitleText.text = "콘텐츠 없음";
            if (thumbnailImage) thumbnailImage.sprite = defaultThumbnail;
            return;
        }

        if (ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded)
        {
            if (contentTitleText) contentTitleText.text = scenarioId;
            if (thumbnailImage) thumbnailImage.sprite = defaultThumbnail;

            if (ScenarioDataLoader.Instance != null)
                ScenarioDataLoader.Instance.OnLoaded += OnDataLoaded;
            return;
        }

        ApplyEntry(scenarioId);
    }

    private void OnDataLoaded(List<ScenarioEntry> entries)
    {
        ScenarioDataLoader.Instance.OnLoaded -= OnDataLoaded;
        _lobby.Properties.TryGetValue("scenarioId", out string scenarioId);
        ApplyEntry(scenarioId);
    }

    private void ApplyEntry(string scenarioId)
    {
        if (string.IsNullOrEmpty(scenarioId)) return;

        var entry = ScenarioDataLoader.Instance?.Entries
            .FirstOrDefault(e => e.scenarioId == scenarioId);

        if (contentTitleText)
            contentTitleText.text = entry != null ? entry.scenarioName : scenarioId;

        if (thumbnailImage)
            thumbnailImage.sprite = (entry?.thumbnail != null) ? entry.thumbnail : defaultThumbnail;
    }

    // ─────────────────────────────────────────
    // 기존 기능
    // ─────────────────────────────────────────

    private void OnLobbyStatusTextChange(string text)
    {
        statusInfoText.text = text;
    }

    private void OnLobbyStateChange(LobbyState state)
    {
        switch (state)
        {
            case LobbyState.FailedToStart:
                Debug.Log("LobbyView:OnLobbyStateChange:FailedToStart");
                break;
            case LobbyState.Preparations:
                Debug.Log("LobbyView:OnLobbyStateChange:Preparations");
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.name.Equals("Client", StringComparison.OrdinalIgnoreCase))
                    SceneManager.LoadScene("Client");
                break;
            case LobbyState.StartingGameServer:
                Debug.Log("LobbyView:OnLobbyStateChange:StartingGameServer");
                break;
            case LobbyState.GameInProgress:
                Debug.Log("LobbyView:OnLobbyStateChange:GameInProgress");
                _currentRoomAccess = null;
                _lobby.GetLobbyRoomAccess((access, error) =>
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Mst.Events.Invoke(MstEventKeys.showLoadingInfo, $"Get Lobby Room Access error: [{error}]");
                        return;
                    }
                    _currentRoomAccess = access;
                });
                break;
            case LobbyState.GameOver:
                Debug.Log("LobbyView:OnLobbyStateChange:GameOver");
                break;
        }
    }

    public void OnHideLobbyViewEventHandler()
    {
        _lobby.Leave();
        Hide();
        Mst.Events.Invoke(MstEventKeys.showGamesListView);
    }

    protected override void OnShow()
    {
        isSet = false;

        if (!isSet)
        {
            var options = _lobby.Members[Mst.Client.Auth.AccountInfo.Username].Properties;
            if (!options.ToDictionary().TryGetValue("PlatformType", out string _))
            {
                options.Add("PlatformType", Utils.GetPlatformType().ToString());
                options.Add("UserName", UserInfo.UserName);
                _lobby.SetMyProperties(options, (s, m) => { isSet = true; });
            }
        }

        DrawPlayersList();
    }

    protected override void OnHide()
    {
        isSet = false;
    }

    private void ClearPlayersList()
    {
        if (listContainer)
            foreach (Transform tr in listContainer)
                Destroy(tr.gameObject);
    }

    bool isSet = false;

    private void DrawPlayersList()
    {
        ClearPlayersList();
        if (!listContainer) { logger.Error("Not all components are setup."); return; }

        int index = 0;
        foreach (LobbyMemberData member in _lobby.Members.Values.ToArray())
        {
            var info = Instantiate(infoPrefab);
            info.transform.SetParent(listContainer);
            info.transform.localPosition = Vector3.zero;
            info.transform.localScale = Vector3.one;

            bool memberIsHost = _lobby.IsMasterUser(member.Username);
            bool isLocalPlayer = Mst.Client.Auth.AccountInfo.Username
                .Equals(member.Username, StringComparison.OrdinalIgnoreCase);

            if (isLocalPlayer)
            {
                if (Utils.GetPlatformType() == PlatformType.VR)
                    setAnchorButton.gameObject.SetActive(memberIsHost);
                UserInfo.isHost = memberIsHost;
                this.isHost = memberIsHost;
            }

            string userName = member.Properties.AsString("UserName");
            string platformType = member.Properties.AsString("PlatformType");

            info.playerName.Text = $"{userName}{(isLocalPlayer ? " (You)" : "")}{(memberIsHost ? " (Host)" : "")}";
            info.playerName.name = $"memberNameLable_{index}";
            info.playerType.Text = platformType;
            info.playerType.name = $"memberPlatformTypeLable_{platformType}";
            info.SetReadyStatus(member.IsReady);
            index++;
        }
    }

    public void OnReadyClick()
    {
        _lobby.SetReadyStatus(!readyStatus, (bool isSuccessful, string error) =>
        {
            if (isSuccessful) readyStatus = !readyStatus;
        });
    }
    public void ClearLobby()
    {
        UnsubscribeLobbyEvents();
        _lobby = null;
        Hide();
    }
    public void OnChangeTeamClick()
    {
        string currentTeam = _lobby.Members[Mst.Client.Auth.AccountInfo.Username].Team;
        string otherTeam = currentTeam.Equals("A", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
        _lobby.JoinTeam(otherTeam, (isSuccessful, error) =>
        {
            if (!isSuccessful) Debug.LogError($"Lobby Change Team Error: {error}");
        });
    }

    public void OnStartGame()
    {
        bool allReady = _lobby.Members.Values.All(m => m.IsReady);
        if (!allReady) { Debug.Log("플레이어가 모두 준비되지 않았습니다."); return; }

        _lobby.SetLobbyProperty(Mst.Args.Names.RoomOnlineScene, "Game", (isSuccessful, error) =>
        {
            if (!isSuccessful) { Debug.LogError($"Set Property Error: {error}"); return; }
            _lobby.SetLobbyProperty("IsPlaying", "true", (isSuccessful2, error2) =>
            {
                if (!isSuccessful2) { Debug.LogError($"Set Property Error: {error2}"); return; }
                _lobby.StartGame((isSuccessful3, error3) =>
                {
                    if (!isSuccessful3) Debug.LogError($"Start Game Error: {error3}");
                });
            });
        });
    }

    private void OnMemberJoined(LobbyMemberData member) => DrawPlayersList();
    private void OnMemberLeft(LobbyMemberData member) => DrawPlayersList();
    private void OnMemberReadyStatusChanged(LobbyMemberData m, bool r) => DrawPlayersList();
    private void OnMemberTeamChangedEvent(LobbyMemberData m, LobbyTeamData t) => DrawPlayersList();

    private void OnMemberPropertyChanged(LobbyMemberData member, string propertyKey, string propertyValue)
    {
        Debug.Log($"OnMemberPropertyChanged: {propertyKey}={propertyValue}");
        StartCoroutine(DelayUpdate(member, propertyKey, propertyValue));
    }

    private IEnumerator DelayUpdate(LobbyMemberData member, string propertyKey, string propertyValue)
    {
        yield return new WaitForEndOfFrame();
        if (_lobby.Members[member.Username].Properties.FindByKey(propertyKey) == null)
            _lobby.Members[member.Username].Properties.Add(propertyKey, propertyValue);
        DrawPlayersList();
    }
}