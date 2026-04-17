using AYellowpaper.SerializedCollections;
using DoDrill.Training;
using MasterServerToolkit.Bridges;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using XRAirpotrSecurity;

public class LobbyListPanel : UIView
{
    [SerializeField] private MatchView matchViewPrefab;
    [SerializeField] private RectTransform listContainer;

    public UnityEvent OnJoinMatch;
    public SerializedDictionary<string, Sprite> carSprites;

    [Header("썸네일 기본 이미지")]
    [SerializeField] private Sprite defaultThumbnail;

    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showGamesListView, OnShowGamesListEventHandler);
        Mst.Events.AddListener(MstEventKeys.hideGamesListView, OnHideGamesListEventHandler);
        Mst.Events.AddListener(MstEventKeys.showLobbyView, OnShowLobbyViewEventHandler);
    }

    protected void Start()
    {
        if (listContainer)
            foreach (Transform t in listContainer)
                Destroy(t.gameObject);
    }

    private void OnShowLobbyViewEventHandler(EventMessage message) => Hide();
    private void OnShowGamesListEventHandler(EventMessage message) => Show();
    private void OnHideGamesListEventHandler(EventMessage message) => Hide();

    protected override void OnShow() => FindGames();

    public void ShowCreateNewLobbyView()
    {
        Mst.Events.Invoke(MstEventKeys.showCreateLobbyView);
    }

    private void ClearGamesList()
    {
        if (listContainer)
            foreach (Transform tr in listContainer)
                Destroy(tr.gameObject);
    }

    public void FindGames()
    {
        ClearGamesList();
        canvasGroup.interactable = false;

        Mst.Client.Matchmaker.FindGames((games) =>
        {
            canvasGroup.interactable = true;
            DrawGamesList(games);
        });
    }

    private void DrawGamesList(IEnumerable<GameInfoPacket> games)
    {
        foreach (var gameInfo in new List<GameInfoPacket>(games))
        {
            if (gameInfo.Type == GameInfoType.Room) continue;

            var property = gameInfo.Properties.ToDictionary();

            bool isPlaying = property.TryGetValue("IsPlaying", out var playingVal)
                             && playingVal == "true";

            // scenarioId 꺼내기
            property.TryGetValue("scenarioId", out string scenarioId);

            var card = Instantiate(matchViewPrefab);
            card.transform.SetParent(listContainer);
            card.transform.localPosition = Vector3.zero;
            card.transform.localScale = Vector3.one;

            // 방 이름
            card.matchName.text = property.TryGetValue(Mst.Args.Names.RoomName, out var roomName)
                ? roomName : "Unknown";
            card.matchName.name = $"gameNameLable_{Mst.Args.Names.RoomName}";

            // 인원
            string maxPlayers = gameInfo.MaxPlayers <= 0 ? "∞" : gameInfo.MaxPlayers.ToString();
            card.matchPlayers.text = $"참가인원 : {gameInfo.OnlinePlayers}/{maxPlayers}";

            // 콘텐츠 제목 + 썸네일
            ApplyScenarioInfo(card, scenarioId);

            // 진행 중 여부
            if (isPlaying)
            {
                card.isPlayingText.gameObject.SetActive(true);
                card.matchJoinButton.gameObject.SetActive(false);
            }
            else
            {
                card.isPlayingText.gameObject.SetActive(false);
                card.matchJoinButton.onClick.RemoveAllListeners();
                card.matchJoinButton.onClick.AddListener(() =>
                {
                    MatchmakingBehaviour.Instance.JoinLobby(gameInfo, Utils.GetPlatformType().ToString());
                    OnJoinMatch?.Invoke();
                });
            }
        }
    }

    // ─────────────────────────────────────────
    // scenarioId → 썸네일 + 제목 적용
    // ─────────────────────────────────────────

    private void ApplyScenarioInfo(MatchView card, string scenarioId)
    {
        if (string.IsNullOrEmpty(scenarioId))
        {
            if (card.matchContentsName) card.matchContentsName.text = "알 수 없음";
            if (card.thumbnail) card.thumbnail.sprite = defaultThumbnail;
            return;
        }

        // DataLoader 로드 완료 여부 확인
        if (ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded)
        {
            if (card.matchContentsName) card.matchContentsName.text = scenarioId;
            if (card.thumbnail) card.thumbnail.sprite = defaultThumbnail;
            return;
        }

        var entry = ScenarioDataLoader.Instance.Entries
            .FirstOrDefault(e => e.scenarioId == scenarioId);

        if (card.matchContentsName)
            card.matchContentsName.text = entry != null ? entry.scenarioName : scenarioId;

        if (card.thumbnail)
        {
            if (entry?.thumbnail != null)
            {
                card.thumbnail.sprite = entry.thumbnail;
                // Texture2D → Sprite 변환
                //var tex = entry.thumbnail;
                //card.thumbnail.sprite = Sprite.Create(
                //    tex,
                //    new Rect(0, 0, tex.width, tex.height),
                //    new Vector2(0.5f, 0.5f)
                //);
            }
            else
            {
                card.thumbnail.sprite = defaultThumbnail;
            }
        }
    }
}