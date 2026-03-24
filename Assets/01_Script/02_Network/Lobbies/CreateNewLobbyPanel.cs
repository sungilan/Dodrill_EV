using AYellowpaper.SerializedCollections;
using DoDrill.Training;
using MasterServerToolkit.Bridges;
using MasterServerToolkit.Logging;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

public class CreateNewLobbyPanel : UIView
{
    [Header("Components"), SerializeField]
    private TMP_InputField roomNameInputField;
    [SerializeField]
    private TMP_InputField roomPasswordInputField;
    public TextMeshProUGUI maxPlayer;
    public TextMeshProUGUI contentNameText;
    private int currentMaxPlayer = 4;
    public SerializedDictionary<string, Sprite> carSprites;

    // ─────────────────────────────────────────
    // 콘텐츠 선택 추가
    // ─────────────────────────────────────────

    [Header("콘텐츠 선택")]
    [SerializeField] private RawImage thumbnailImage;
    [SerializeField] private Texture2D defaultThumbnail;

    private List<ScenarioEntry> _entries = new();
    private int _currentIndex = 0;

    public string SelectedScenarioId =>
        _entries.Count > 0 ? _entries[_currentIndex].scenarioId : string.Empty;

    // ─────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        RoomName = $"Room#{Mst.Helper.CreateFriendlyId()}";
        Mst.Events.AddListener(MstEventKeys.showCreateLobbyView, OnShowCreateLobbyViewEventHandler);
        Mst.Events.AddListener(MstEventKeys.hideCreateLobbyView, OnHideCreateLobbyViewEventHandler);
    }

    private void OnShowCreateLobbyViewEventHandler(EventMessage message) => Show();
    private void OnHideCreateLobbyViewEventHandler(EventMessage message) => Hide();

    protected override void OnShow()
    {
        base.OnShow();
        _currentIndex = 0;

        if (ScenarioDataLoader.Instance != null && ScenarioDataLoader.Instance.IsLoaded)
        {
            _entries = new List<ScenarioEntry>(ScenarioDataLoader.Instance.Entries);
            RefreshContentView();
        }
        else if (ScenarioDataLoader.Instance != null)
        {
            ScenarioDataLoader.Instance.OnLoaded += OnDataLoaded;
        }
    }

    protected override void OnHide()
    {
        base.OnHide();
        if (ScenarioDataLoader.Instance != null)
            ScenarioDataLoader.Instance.OnLoaded -= OnDataLoaded;
    }

    private void OnDataLoaded(List<ScenarioEntry> entries)
    {
        _entries = entries;
        RefreshContentView();
        ScenarioDataLoader.Instance.OnLoaded -= OnDataLoaded;
    }

    // ─────────────────────────────────────────
    // ← → 버튼 (Inspector에서 OnClick 연결)
    // ─────────────────────────────────────────

    public void OnPrevContent()
    {
        if (_entries.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _entries.Count) % _entries.Count;
        RefreshContentView();
    }

    public void OnNextContent()
    {
        if (_entries.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _entries.Count;
        RefreshContentView();
    }

    private void RefreshContentView()
    {
        if (_entries.Count == 0)
        {
            if (contentNameText) contentNameText.text = "콘텐츠 없음";
            if (thumbnailImage) thumbnailImage.texture = defaultThumbnail;
            return;
        }

        var entry = _entries[_currentIndex];

        if (contentNameText) contentNameText.text = entry.scenarioName;
        if (thumbnailImage) thumbnailImage.texture = entry.thumbnail != null
                                                      ? entry.thumbnail
                                                      : defaultThumbnail;
    }

    // ─────────────────────────────────────────
    // 방 생성 — scenarioId 포함
    // ─────────────────────────────────────────

    public void CreateNewMatch()
    {
        if (string.IsNullOrEmpty(SelectedScenarioId))
        {
            Debug.LogWarning("[CreateNewLobbyPanel] 시나리오가 선택되지 않았습니다.");
            return;
        }

        Mst.Events.Invoke(MstEventKeys.showLoadingInfo, "Starting lobby... Please wait!");
        Logs.Debug("Starting lobby... Please wait!");

        Regex roomNameRe = new Regex(@"\s+");
        var options = new MstProperties();
        options.Add(Mst.Args.Names.RoomName, roomNameRe.Replace(RoomName, "_"));
        options.Add(Mst.Args.Names.LobbyId, "Game");
        options.Add(Mst.Args.Names.RoomMaxConnections, currentMaxPlayer);
        options.Add("scenarioId", SelectedScenarioId); // ← 추가

        if (!string.IsNullOrEmpty(Password))
            options.Add(Mst.Args.Names.RoomPassword, Password);

        MatchmakingBehaviour.Instance.CreateNewLobby("Game", options, () =>
        {
            Show();
        }, platformType: Utils.GetPlatformType().ToString());

        UserInfo.anchorSetted = false;
    }

    // ─────────────────────────────────────────
    // 기존 프로퍼티 유지
    // ─────────────────────────────────────────

    public string RoomName
    {
        get => roomNameInputField != null ? roomNameInputField.text : string.Empty;
        set { if (roomNameInputField) roomNameInputField.text = value; }
    }

    public string Password
    {
        get => roomPasswordInputField != null ? roomPasswordInputField.text : string.Empty;
        set { if (roomPasswordInputField) roomPasswordInputField.text = value; }
    }

    public void IncreaseMaxPlayer(int add)
    {
        int num = currentMaxPlayer + add;
        if (num < 1 || num > 30) return;
        currentMaxPlayer = num;
        maxPlayer.text = currentMaxPlayer.ToString();
    }
}