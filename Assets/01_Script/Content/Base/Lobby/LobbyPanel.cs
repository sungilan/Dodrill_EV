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
    private const string RoomDisplayTitleKey = "roomDisplayTitle";

    [SerializeField] private LobbyPlayerInfos infoPrefab;
    [SerializeField] private RectTransform listContainer;
    [SerializeField] private TMP_Text statusInfoText;
    [SerializeField] private TMP_Dropdown stageDropdown;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button setAnchorButton;


    [SerializeField] private Image thumbnailImage;
    [SerializeField] private Sprite defaultThumbnail;
    [SerializeField] private TMP_Text contentTitleText;

    private static JoinedLobby _lobby;
    public UnityEvent OnStartGameEvent;
    private RoomAccessPacket _currentRoomAccess;
    private bool readyStatus;
    public UIView LobbyListView;
    [SerializeField] private UIView contentsListView;
    [SerializeField] private LobbyBottomPanel lobbyBottomPanel;
    public GameObject startPanel;
    public TMP_Dropdown dropdown;
    private bool isHost = false;
    public TextMeshProUGUI roomTitleText;
    private readonly Dictionary<Sprite, Sprite> _thumbnailSpriteCache = new();

    private string StageName =>
        stageDropdown != null && stageDropdown.options.Count > 0
            ? stageDropdown.options[stageDropdown.value].text
            : string.Empty;



    private static LobbyPanel s_instance;

    // 준비 버튼 표시·시작 중복 방지용. 인스펙터에 비어 있으면 onClick 연결(OnReadyClick/OnStartGame)로 찾는다.
    [SerializeField] private Button readyButton;
    private TMP_Text _readyLabel, _startLabel;
    private string _readyLabelDefault, _startLabelDefault;
    private Color _readyColorDefault = Color.white;
    private static readonly Color ReadyOnColor = new Color(0.25f, 0.55f, 1f);
    private bool _starting;
    private float _startingSince;
    private const float StartTimeout = 30f;

    protected override void Awake()
    {
        base.Awake();
        s_instance = this;
        BindLobbyButtons();
        Mst.Events.AddListener(MstEventKeys.showLobbyView, OnShowLobbyViewEventHandler);
    }

    protected override void OnDestroy()
    {
        UnsubscribeLobbyEvents();
        if(_lobby != null)
        {
            _lobby.OnLobbyStateChangeEvent -= OnLobbyStateChange;
            _lobby.OnLobbyStatusTextChangeEvent -= OnLobbyStatusTextChange;
        }
        if(s_instance == this) s_instance = null;
        base.OnDestroy();
    }

    protected void Start()
    {
        EnsureReturnViewsBound();
        if(listContainer)
            foreach(Transform t in listContainer)
                Destroy(t.gameObject);

        if(_lobby != null && !_lobby.HasLeft)
        {
            ResetLobby();
            DrawPlayersList();
        }
    }



    private void OnShowLobbyViewEventHandler(EventMessage message)
    {
        //TrainingSessionLoadingFlow.Reset();
        LobbyListView.Hide();
        Mst.Events.Invoke(MstEventKeys.hideCreateLobbyView);
        Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);


        if(_lobby != null)
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
        if(_lobby != null)
        {
            // 예전에는 '-= null' 이라 해제가 안 되어 이벤트가 누수됐다 -> 실제 핸들러를 해제한다
            if(s_instance != null)
            {
                s_instance.UnsubscribeLobbyEvents();
                _lobby.OnLobbyStateChangeEvent -= s_instance.OnLobbyStateChange;
                _lobby.OnLobbyStatusTextChangeEvent -= s_instance.OnLobbyStatusTextChange;
            }
            try { _lobby.Leave(); } catch { }
            _lobby = null;
        }
    }
    private void ResetLobby()
    {
        if(_lobby == null) return;
        UnsubscribeLobbyEvents();   // Start()에서 다시 불릴 때 += 가 중복되어 화면 갱신이 여러 번 일어나던 문제
        _lobby.OnMemberJoinedEvent += OnMemberJoined;
        _lobby.OnMemberLeftEvent += OnMemberLeft;
        _lobby.OnChatMessageReceivedEvent += OnLobbyChat;
        _lobby.OnMemberReadyStatusChangedEvent += OnMemberReadyStatusChanged;
        _lobby.OnMemberPropertyChangedEvent += OnMemberPropertyChanged;
        _lobby.OnMemberTeamChangedEvent += OnMemberTeamChangedEvent;

        bool isMasterUser = _lobby.IsMasterUser(Mst.Client.Auth.AccountInfo.Username);
        startGameButton.gameObject.SetActive(isMasterUser);
        SetStarting(false);
        SyncLocalReadyStatus();
        RefreshReadyButton();

        RefreshContentInfo();
        Show();
    }

    private void UnsubscribeLobbyEvents()
    {
        if(_lobby == null) return;
        _lobby.OnMemberJoinedEvent -= OnMemberJoined;
        _lobby.OnMemberLeftEvent -= OnMemberLeft;
        _lobby.OnChatMessageReceivedEvent -= OnLobbyChat;
        _lobby.OnMemberReadyStatusChangedEvent -= OnMemberReadyStatusChanged;
        _lobby.OnMemberPropertyChangedEvent -= OnMemberPropertyChanged;
        _lobby.OnMemberTeamChangedEvent -= OnMemberTeamChangedEvent;
    }


    private void RefreshContentInfo()
    {
        RefreshRoomTitle();
        _lobby.Properties.TryGetValue("scenarioId", out string scenarioId);

        if(string.IsNullOrEmpty(scenarioId))
        {
            if(contentTitleText) contentTitleText.text = "시나리오 없음";
            if(thumbnailImage) thumbnailImage.sprite = defaultThumbnail;
            return;
        }

        if(ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded)
        {
            if(contentTitleText) contentTitleText.text = scenarioId;
            if(thumbnailImage) thumbnailImage.sprite = defaultThumbnail;

            if(ScenarioDataLoader.Instance != null)
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
        if(string.IsNullOrEmpty(scenarioId)) return;

        var entry = ScenarioDataLoader.Instance?.Entries
            .FirstOrDefault(e => e.scenarioId == scenarioId);

        if(contentTitleText)
            contentTitleText.text = entry != null ? entry.scenarioName : scenarioId;

        if(thumbnailImage)
            thumbnailImage.sprite = (entry?.thumbnail != null)
                ? GetOrCreateThumbnailSprite(entry.thumbnail)
                : defaultThumbnail;
    }

    private Sprite GetOrCreateThumbnailSprite(Sprite texture)
    {
        if(texture == null)
            return defaultThumbnail;

        if(_thumbnailSpriteCache.TryGetValue(texture, out var cached) && cached != null)
            return cached;

        //var created = Sprite.Create(
        //    texture,
        //    new Rect(0f, 0f, texture.width, texture.height),
        //    new Vector2(0.5f, 0.5f));

        _thumbnailSpriteCache[texture] = texture;
        return texture;
    }

    private void RefreshRoomTitle()
    {
        if(roomTitleText == null || _lobby == null)
            return;

        if(_lobby.Properties.TryGetValue(RoomDisplayTitleKey, out string roomDisplayTitle) &&
            !string.IsNullOrWhiteSpace(roomDisplayTitle))
        {
            roomTitleText.text = roomDisplayTitle;
            return;
        }

        if(_lobby.Properties.TryGetValue("scenarioId", out string scenarioId) &&
            !string.IsNullOrWhiteSpace(scenarioId))
        {
            roomTitleText.text = scenarioId;
            return;
        }
    }

    private void OnLobbyStatusTextChange(string text)
    {
        statusInfoText.text = text;
    }

    private void OnLobbyStateChange(LobbyState state)
    {
        switch(state)
        {
            case LobbyState.FailedToStart:
                Debug.Log("LobbyView:OnLobbyStateChange:FailedToStart");
                if(_starting) { SetStarting(false); ShowNotice("훈련을 시작하지 못했습니다. 다시 시도해 주세요."); }
                //TrainingSessionLoadingFlow.Reset();
                break;
            case LobbyState.Preparations:
                Debug.Log("LobbyView:OnLobbyStateChange:Preparations");
                Scene scene = SceneManager.GetActiveScene();
                if(!scene.name.Equals("Client", StringComparison.OrdinalIgnoreCase))
                    SceneManager.LoadScene("Client");
                break;
            case LobbyState.StartingGameServer:
                Debug.Log("LobbyView:OnLobbyStateChange:StartingGameServer");
                //TrainingSessionLoadingFlow.BeginGameTransition();
                break;
            case LobbyState.GameInProgress:
                Debug.Log("LobbyView:OnLobbyStateChange:GameInProgress");
                //TrainingSessionLoadingFlow.BeginGameTransition();
                _currentRoomAccess = null;
                _lobby.GetLobbyRoomAccess((access, error) =>
                {
                    if(!string.IsNullOrWhiteSpace(error))
                    {
                        Mst.Events.Invoke(MstEventKeys.showLoadingInfo, $"Get Lobby Room Access error: [{error}]");
                        return;
                    }
                    _currentRoomAccess = access;
                });
                break;
            case LobbyState.GameOver:
                Debug.Log("LobbyView:OnLobbyStateChange:GameOver");
                //TrainingSessionLoadingFlow.Reset();
                break;
        }
    }

    public void OnHideLobbyViewEventHandler()
    {
        if(_lobby != null)
        {
            try { _lobby.Leave(); } catch(Exception e) { Debug.LogWarning($"[LobbyPanel] 로비 나가기 실패: {e.Message}"); }
        }
        Hide();
        EnsureReturnViewsBound();
        contentsListView?.Show();
        lobbyBottomPanel?.ViewContentsList();
        Mst.Events.Invoke(MstEventKeys.showGamesListView);
    }

    protected override void OnShow()
    {
        isSet = false;

        if(!isSet && TryGetLocalMember(out var me))
        {
            string platform = Utils.GetPlatformType().ToString();
            string userName = UserInfo.UserName;
            var current = me.Properties.ToDictionary();
            // 값이 없거나 달라졌을 때만 서버에 알린다(플랫폼 변경/재입장 시 목록 표시 오류 방지)
            if(!current.TryGetValue("PlatformType", out string curPlatform) || curPlatform != platform)
                _lobby.SetMyProperty("PlatformType", platform, (s, m) => { });
            if(!string.IsNullOrEmpty(userName) && (!current.TryGetValue("UserName", out string curName) || curName != userName))
                _lobby.SetMyProperty("UserName", userName, (s, m) => { isSet = true; });
            else
                isSet = true;
        }

        DrawPlayersList();
    }

    protected override void OnHide()
    {
        isSet = false;
    }

    private void ClearPlayersList()
    {
        if(listContainer)
            foreach(Transform tr in listContainer)
                Destroy(tr.gameObject);
    }

    bool isSet = false;

    private void DrawPlayersList()
    {
        ClearPlayersList();
        if(!listContainer) { logger.Error("Not all components are setup."); return; }

        int index = 0;
        foreach(LobbyMemberData member in _lobby.Members.Values.ToArray())
        {
            var info = Instantiate(infoPrefab);
            info.transform.SetParent(listContainer);
            info.transform.localPosition = Vector3.zero;
            info.transform.localScale = Vector3.one;

            bool memberIsHost = _lobby.IsMasterUser(member.Username);
            bool isLocalPlayer = Mst.Client.Auth.AccountInfo.Username
                .Equals(member.Username, StringComparison.OrdinalIgnoreCase);

            if(isLocalPlayer)
            {
                if(Utils.GetPlatformType() == PlatformType.VR)
                {
                    if(setAnchorButton != null)
                        setAnchorButton.gameObject.SetActive(memberIsHost);
                    else
                        Debug.LogWarning("[LobbyPanel] setAnchorButton is not assigned. VR anchor toggle UI is skipped.");
                }
                UserInfo.isHost = memberIsHost;
                this.isHost = memberIsHost;
                readyStatus = member.IsReady;
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
        RefreshReadyButton();
    }

    public void OnReadyClick()
    {
        bool nextReadyState = !readyStatus;
        if(TryGetLocalMember(out var localMember))
            nextReadyState = !localMember.IsReady;

        _lobby.SetReadyStatus(nextReadyState, (bool isSuccessful, string error) =>
        {
            if(isSuccessful)
                readyStatus = nextReadyState;
            RefreshReadyButton();
        });
    }

    private void BindLobbyButtons()
    {
        if(readyButton == null || startGameButton == null)
        {
            foreach(var b in GetComponentsInChildren<Button>(true))
            {
                for(int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                {
                    string m = b.onClick.GetPersistentMethodName(i);
                    if(readyButton == null && m == nameof(OnReadyClick)) readyButton = b;
                    else if(startGameButton == null && m == nameof(OnStartGame)) startGameButton = b;
                }
            }
        }
        if(readyButton != null)
        {
            _readyLabel = readyButton.GetComponentInChildren<TMP_Text>(true);
            if(_readyLabel != null) _readyLabelDefault = _readyLabel.text;
            if(readyButton.targetGraphic != null) _readyColorDefault = readyButton.targetGraphic.color;
        }
        if(startGameButton != null)
        {
            _startLabel = startGameButton.GetComponentInChildren<TMP_Text>(true);
            if(_startLabel != null) _startLabelDefault = _startLabel.text;
        }
    }

    // 준비 상태를 버튼 색·문구로 보여준다 (예전에는 목록의 작은 아이콘만 바뀌어 눌렀는지 알기 어려웠다)
    private void RefreshReadyButton()
    {
        if(readyButton == null) return;
        bool on = TryGetLocalMember(out var me) ? me.IsReady : readyStatus;
        // 버튼을 눌렀을 때 나오는 그래픽(ClickButton 눌림 스프라이트·흰 글자)을 준비 완료 동안 그대로 유지한다
        var latch = readyButton.GetComponent<IPressLatch>();
        if(latch != null) latch.SetLatched(on);
        else if(readyButton.targetGraphic != null)
            readyButton.targetGraphic.color = on ? ReadyOnColor : _readyColorDefault;
        if(_readyLabel != null)
            _readyLabel.text = on ? "준비 완료" : (string.IsNullOrEmpty(_readyLabelDefault) ? "준비하기" : _readyLabelDefault);
    }

    private void SetStarting(bool starting)
    {
        _starting = starting;
        _startingSince = Time.unscaledTime;
        if(startGameButton != null) startGameButton.interactable = !starting;
        if(_startLabel != null && _startLabelDefault != null)
            _startLabel.text = starting ? "시작 중..." : _startLabelDefault;
        if(starting) ShowNotice("훈련을 시작하는 중입니다. 잠시만 기다려 주세요.");
    }

    private void Update()
    {
        // 서버 응답이 끝내 오지 않으면 다시 누를 수 있게 풀어준다
        if(_starting && Time.unscaledTime - _startingSince > StartTimeout)
        {
            SetStarting(false);
            ShowNotice("시작 응답이 없습니다. 다시 시도해 주세요.");
        }
    }

    private bool TryGetLocalMember(out LobbyMemberData localMember)
    {
        localMember = null;
        if(_lobby == null || _lobby.Members == null || Mst.Client?.Auth?.AccountInfo == null)
            return false;

        string username = Mst.Client.Auth.AccountInfo.Username;
        if(string.IsNullOrWhiteSpace(username))
            return false;

        return _lobby.Members.TryGetValue(username, out localMember) && localMember != null;
    }

    private void SyncLocalReadyStatus()
    {
        if(TryGetLocalMember(out var localMember))
            readyStatus = localMember.IsReady;
    }
    public void ClearLobby()
    {
        UnsubscribeLobbyEvents();
        _lobby = null;
        Hide();
    }

    private void EnsureReturnViewsBound()
    {
        if(contentsListView == null)
        {
            var allViews = FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach(var view in allViews)
            {
                if(view != null && string.Equals(view.name, "View_ContentsListView", StringComparison.Ordinal))
                {
                    contentsListView = view;
                    break;
                }
            }
        }

        if(lobbyBottomPanel == null)
            lobbyBottomPanel = FindFirstObjectByType<LobbyBottomPanel>(FindObjectsInactive.Include);
    }

    public void OnChangeTeamClick()
    {
        string currentTeam = _lobby.Members[Mst.Client.Auth.AccountInfo.Username].Team;
        string otherTeam = currentTeam.Equals("A", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
        _lobby.JoinTeam(otherTeam, (isSuccessful, error) =>
        {
            if(!isSuccessful) { Debug.LogError($"Lobby Change Team Error: {error}"); ShowNotice($"팀을 바꾸지 못했습니다: {error}"); }
        });
    }

    // 로비 상태 문구 칸에 사용자에게 보이는 안내를 남긴다 (예전에는 Debug 로그뿐이라 왜 안 되는지 알 수 없었다)
    private void ShowNotice(string message)
    {
        Debug.Log($"[LobbyPanel] {message}");
        if(statusInfoText != null) statusInfoText.text = message;
    }

    public void OnStartGame()
    {
        if(_lobby == null) return;
        if(_starting) { ShowNotice("훈련을 시작하는 중입니다. 잠시만 기다려 주세요."); return; }
        int total = _lobby.Members.Count;
        int ready = _lobby.Members.Values.Count(m => m.IsReady);
        if(ready < total)
        {
            ShowNotice($"아직 준비하지 않은 참가자가 있습니다 ({ready}/{total} 준비 완료)");
            return;
        }

        SetStarting(true);
        _lobby.SetLobbyProperty(Mst.Args.Names.RoomOnlineScene, "Game", (isSuccessful, error) =>
        {
            if(!isSuccessful)
            {
                SetStarting(false);
                Debug.LogError($"Set Property Error: {error}");
                ShowNotice($"시작하지 못했습니다: {error}");
                Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                return;
            }
            _lobby.SetLobbyProperty("IsPlaying", "true", (isSuccessful2, error2) =>
            {
                if(!isSuccessful2)
                {
                    SetStarting(false);
                    Debug.LogError($"Set Property Error: {error2}");
                    ShowNotice($"시작하지 못했습니다: {error2}");
                    Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                    return;
                }
                _lobby.StartGame((isSuccessful3, error3) =>
                {
                    if(!isSuccessful3)
                    {
                        SetStarting(false);
                        Debug.LogError($"Start Game Error: {error3} / 이유: {StartFailReason("(마스터 서버가 이유를 보내지 않음)")}");
                        ShowNotice($"게임을 시작하지 못했습니다: {StartFailReason(error3)}");
                        Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                    }
                });
            });
        });
    }

    // 마스터 서버는 '훈련 시작' 거절 이유(서버 부족·준비 안 됨·방 상태 등)를 응답이 아니라 로비 채팅(오류)으로 보낸다.
    // 응답에는 'Failed starting the game'만 와서 이유를 알 수 없었다 → 오류 채팅을 로그·안내로 보여 준다.
    private void OnLobbyChat(LobbyChatPacket packet)
    {
        if(packet == null || !packet.IsError) return;
        Debug.LogWarning($"[LobbyPanel] 마스터 서버 알림: {packet.Message}");
        _lastLobbyError = TranslateLobbyError(packet.Message);
        _lastLobbyErrorAt = Time.unscaledTime;
        ShowNotice(_lastLobbyError);
    }

    private string _lastLobbyError;
    private float _lastLobbyErrorAt = -10f;
    // 시작 실패 응답 직전에 온 거절 이유(있으면 그것, 없으면 응답 문구)
    private string StartFailReason(string fallback) =>
        !string.IsNullOrEmpty(_lastLobbyError) && Time.unscaledTime - _lastLobbyErrorAt < 3f ? _lastLobbyError : fallback;

    private static string TranslateLobbyError(string msg)
    {
        if(string.IsNullOrEmpty(msg)) return msg;
        if(msg.Contains("Servers are busy")) return "게임 서버를 띄울 수 없습니다 (스포너/룸 서버 확인 필요)";
        if(msg.Contains("Not all players are ready")) return "아직 준비하지 않은 참가자가 있습니다";
        if(msg.Contains("Invalid lobby state")) return "이미 시작 중이거나 진행 중인 방입니다. 방을 나갔다가 다시 만들어 주세요";
        if(msg.Contains("not the master")) return "방장만 훈련을 시작할 수 있습니다";
        if(msg.Contains("Not enough players")) return "참가 인원이 부족합니다";
        if(msg.Contains("cannot start the game manually")) return "이 방은 수동 시작이 꺼져 있습니다";
        return msg;
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
        if(_lobby == null) yield break;
        if(_lobby.Members.TryGetValue(member.Username, out var current) && current != null && current.Properties.FindByKey(propertyKey) == null)
            current.Properties.Add(propertyKey, propertyValue);
        DrawPlayersList();
    }
}
