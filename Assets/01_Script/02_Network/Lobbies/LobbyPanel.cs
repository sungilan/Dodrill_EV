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



    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showLobbyView, OnShowLobbyViewEventHandler);
    }

    protected override void OnDestroy()
    {
        UnsubscribeLobbyEvents();
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
        SyncLocalReadyStatus();

        RefreshContentInfo();
        Show();
    }

    private void UnsubscribeLobbyEvents()
    {
        if(_lobby == null) return;
        _lobby.OnMemberJoinedEvent -= OnMemberJoined;
        _lobby.OnMemberLeftEvent -= OnMemberLeft;
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
        _lobby.Leave();
        Hide();
        EnsureReturnViewsBound();
        contentsListView?.Show();
        lobbyBottomPanel?.ViewContentsList();
        Mst.Events.Invoke(MstEventKeys.showGamesListView);
    }

    protected override void OnShow()
    {
        isSet = false;

        if(!isSet)
        {
            var options = _lobby.Members[Mst.Client.Auth.AccountInfo.Username].Properties;
            if(!options.ToDictionary().TryGetValue("PlatformType", out string _))
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
        });
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
            if(!isSuccessful) Debug.LogError($"Lobby Change Team Error: {error}");
        });
    }

    public void OnStartGame()
    {
        bool allReady = _lobby.Members.Values.All(m => m.IsReady);
        if(!allReady)
        {
            return;
        }


        _lobby.SetLobbyProperty(Mst.Args.Names.RoomOnlineScene, "Game", (isSuccessful, error) =>
        {
            if(!isSuccessful)
            {
                Debug.LogError($"Set Property Error: {error}");
                Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                return;
            }
            _lobby.SetLobbyProperty("IsPlaying", "true", (isSuccessful2, error2) =>
            {
                if(!isSuccessful2)
                {
                    Debug.LogError($"Set Property Error: {error2}");
                    Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                    return;
                }
                _lobby.StartGame((isSuccessful3, error3) =>
                {
                    if(!isSuccessful3)
                    {
                        Debug.LogError($"Start Game Error: {error3}");
                        Mst.Events.Invoke(MstEventKeys.hideLoadingInfo);
                    }
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
        if(_lobby.Members[member.Username].Properties.FindByKey(propertyKey) == null)
            _lobby.Members[member.Username].Properties.Add(propertyKey, propertyValue);
        DrawPlayersList();
    }
}
