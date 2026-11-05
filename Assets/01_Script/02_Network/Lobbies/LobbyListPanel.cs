using AYellowpaper.SerializedCollections;
using DoDrill.Training;
using MasterServerToolkit.Bridges;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
        ClearEmptyNotice();
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

    private readonly List<(MatchView card, string scenarioId, string host)> _drawnCards = new();

    private void DrawGamesList(IEnumerable<GameInfoPacket> games)
    {
        _drawnCards.Clear();
        int drawn = 0;
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

            // 방 이름 (공백이 _ 로 바뀌어 저장되므로 보기 좋게 되돌리고, 길면 … 으로 줄인다)
            card.matchName.text = property.TryGetValue(Mst.Args.Names.RoomName, out var roomName)
                ? roomName.Replace('_', ' ') : "Unknown";
            card.matchName.textWrappingMode = TextWrappingModes.NoWrap;
            card.matchName.overflowMode = TextOverflowModes.Ellipsis;
            property.TryGetValue("hostName", out string hostName);
            card.matchName.name = $"gameNameLable_{Mst.Args.Names.RoomName}";

            // 인원
            string maxPlayers = gameInfo.MaxPlayers <= 0 ? "∞" : gameInfo.MaxPlayers.ToString();
            card.matchPlayers.text = $"참가인원 : {gameInfo.OnlinePlayers}/{maxPlayers}";

            // 콘텐츠 제목 + 썸네일
            ApplyScenarioInfo(card, scenarioId, hostName);
            _drawnCards.Add((card, scenarioId, hostName));
            drawn++;
            StartCoroutine(CoClampToCard(card));

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

        if (drawn == 0) ShowEmptyNotice();
        else if (ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded) StartCoroutine(CoRefreshWhenLoaded());
    }

    // 시나리오 목록이 방 목록보다 늦게 로드되면 카드가 기본 썸네일·ID 로 굳던 문제 — 로드가 끝나면 한 번 다시 채운다
    private IEnumerator CoRefreshWhenLoaded()
    {
        float t = 0f;
        while (t < 8f && (ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded)) { t += Time.unscaledDeltaTime; yield return null; }
        foreach (var (card, scenarioId, host) in _drawnCards)
            if (card != null) ApplyScenarioInfo(card, scenarioId, host);
    }

    // 카드 안의 글자가 카드 오른쪽 밖으로 튀어나오지 않게 오른쪽 끝을 카드 안쪽으로 맞춘다(레이아웃이 잡힌 다음 프레임에)
    private IEnumerator CoClampToCard(MatchView card)
    {
        yield return null;
        yield return null;
        if (card == null) yield break;
        var cardRt = card.transform as RectTransform;
        if (cardRt == null) yield break;
        foreach (var tmp in new TMP_Text[] { card.matchName, card.matchContentsName, card.matchPlayers })
            ClampText(tmp, cardRt);
    }

    private static void ClampText(TMP_Text tmp, RectTransform cardRt)
    {
        if (tmp == null) return;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        var rt = tmp.rectTransform;
        var cc = new Vector3[4]; var tc = new Vector3[4];
        cardRt.GetWorldCorners(cc); rt.GetWorldCorners(tc);
        const float pad = 16f;
        float limit = cc[2].x - cardRt.TransformVector(new Vector3(pad, 0, 0)).x;
        float overWorld = tc[2].x - limit;
        if (overWorld <= 0.001f) return;
        float overLocal = Mathf.Abs(rt.InverseTransformVector(new Vector3(overWorld, 0, 0)).x);
        float w = rt.rect.width;
        float newW = Mathf.Max(60f, w - overLocal);
        float dw = newW - w;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newW);
        var pos = rt.anchoredPosition; pos.x += rt.pivot.x * dw; rt.anchoredPosition = pos;   // 왼쪽 가장자리는 고정
    }

    // 방이 하나도 없을 때: 패널 가운데에 안내 문구, 하단 가운데에 작은 '뒤로 가기'
    // (모바일에서 상단 버튼이 화면 밖이면 돌아갈 방법이 없었다). 목록 레이아웃과 상관없이 패널 기준으로 배치한다.
    private readonly List<GameObject> _emptyNoticeObjs = new();

    private RectTransform FindPanelRoot()
    {
        Transform t = listContainer != null ? listContainer.parent : null;
        while (t != null)
        {
            if (t.GetComponent<VerticalLayoutGroup>() != null && t.GetComponent<Image>() != null) return (RectTransform)t;
            t = t.parent;
        }
        return transform as RectTransform;
    }

    private void ClearEmptyNotice()
    {
        foreach (var g in _emptyNoticeObjs) if (g != null) Destroy(g);
        _emptyNoticeObjs.Clear();
    }

    private void ShowEmptyNotice()
    {
        if (listContainer == null || matchViewPrefab == null) return;
        ClearEmptyNotice();
        var panel = FindPanelRoot();

        var go = new GameObject("EmptyNotice", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 10f); rt.sizeDelta = new Vector2(900f, 160f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (matchViewPrefab.matchName != null) tmp.font = matchViewPrefab.matchName.font;
        tmp.text = "생성된 훈련방이 없습니다.\n<size=70%><color=#9AA4B2>새로고침하거나 뒤로 가서 방을 만들어 주세요.</color></size>";
        tmp.alignment = TextAlignmentOptions.Center; tmp.fontSize = 34; tmp.color = Color.white; tmp.raycastTarget = false;
        _emptyNoticeObjs.Add(go);

        // 위쪽의 '뒤로 가기' 버튼을 복제해 하단 가운데에 작게 둔다(저장된 onClick 동작이 그대로 복사된다)
        Button back = null;
        foreach (var b in GetComponentsInChildren<Button>(true))
            if (b.name == "BTN_Back") { back = b; break; }
        if (back != null)
        {
            var clone = Instantiate(back.gameObject, panel, false);
            clone.name = "BTN_Back_Empty";
            clone.SetActive(true);
            var cle = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>(); cle.ignoreLayout = true;
            var crt = (RectTransform)clone.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f); crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 44f); crt.sizeDelta = new Vector2(230f, 58f);
            foreach (var label in clone.GetComponentsInChildren<TMP_Text>(true))
            {
                float max = label.fontSize;
                label.enableAutoSizing = true; label.fontSizeMax = max; label.fontSizeMin = Mathf.Max(14f, max * 0.6f);
                label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            }
            _emptyNoticeObjs.Add(clone);
        }
    }

    // ─────────────────────────────────────────
    // scenarioId → 썸네일 + 제목 적용
    // ─────────────────────────────────────────

    private static Sprite s_placeholder;

    /// <summary>기본 썸네일이 비었거나 흰 사각형(AutoHand 예제 이미지)이면 어두운 둥근 자리표시 이미지를 대신 쓴다.</summary>
    private Sprite SafeDefault()
    {
        if (defaultThumbnail != null && defaultThumbnail.name != "WhiteSquare") return defaultThumbnail;
        if (s_placeholder != null) return s_placeholder;
        const int S = 64; const float R = 12f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(R - d + 0.5f) * 255f);
            float g = Mathf.Lerp(0.16f, 0.10f, y / (float)S);
            px[y * S + x] = new Color32((byte)(g * 255f), (byte)((g + 0.03f) * 255f), (byte)((g + 0.08f) * 255f), a);
        }
        tex.SetPixels32(px); tex.Apply();
        s_placeholder = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        return s_placeholder;
    }

    // 카드에는 호스트 이름까지만 보인다(콘텐츠 이름 [P0AA6]… 은 글자가 잘려 보여서 표시하지 않음)
    private static string HostLine(string host, string contentName)
    {
        return string.IsNullOrWhiteSpace(host) ? "호스트 -" : $"호스트 {host}";
    }

    private void ApplyScenarioInfo(MatchView card, string scenarioId, string host = null)
    {
        if (card.thumbnail) card.thumbnail.color = Color.white;
        if (string.IsNullOrEmpty(scenarioId))
        {
            if (card.matchContentsName) card.matchContentsName.text = HostLine(host, null);
            if (card.thumbnail) card.thumbnail.sprite = SafeDefault();
            return;
        }

        // DataLoader 로드 완료 여부 확인
        if (ScenarioDataLoader.Instance == null || !ScenarioDataLoader.Instance.IsLoaded)
        {
            if (card.matchContentsName) card.matchContentsName.text = HostLine(host, null);
            if (card.thumbnail) card.thumbnail.sprite = SafeDefault();
            return;
        }

        var entry = ScenarioDataLoader.Instance.Entries
            .FirstOrDefault(e => e.scenarioId == scenarioId);

        if (card.matchContentsName)
            card.matchContentsName.text = HostLine(host, entry != null ? entry.scenarioName : scenarioId);

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
                card.thumbnail.sprite = SafeDefault();
            }
        }
    }
}