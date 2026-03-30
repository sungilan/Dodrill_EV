using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet;
using FishNet.Transporting;

// ============================================================
//  LobbyModeSelector.cs
//  로비 씬의 기존 LobbyUI 안에 추가하는 모드 선택 컴포넌트
//
//  씬 세팅:
//    - 로비 씬 Canvas 안에 ModeSelectPanel GO를 만들고 부착
//    - 방장 여부에 따라 UI 활성/비활성 자동 처리
//    - Game 씬 로드 직전 SessionData.gameMode 저장
//
//  로비 씬 패널 구조:
//    ModeSelectPanel
//      ├─ TitleText (TMP) — "게임 모드 선택"
//      ├─ BtnScenario (Button + Toggle)
//      ├─ BtnFreeMode (Button + Toggle)
//      ├─ DescriptionText (TMP) — 호버 설명
//      └─ ModeLabel (TMP) — 현재 선택 표시 (비방장에게도 보임)
// ============================================================
public class LobbyModeSelector : MonoBehaviour
{
    [Header("UI")]
    public Button   btnScenario;
    public Button   btnFreeMode;
    public TMP_Text descriptionText;
    public TMP_Text modeLabel;          // 현재 선택된 모드 표시

    [Header("색상 — 선택/미선택")]
    public Color selectedColor   = new Color(0.2f, 0.6f, 1f);
    public Color unselectedColor = new Color(0.3f, 0.3f, 0.3f);

    [Header("설명 텍스트")]
    [TextArea(2,3)] public string scenarioDesc =
        "P0AA6 시나리오를 단계별로 진행합니다.\n진단 → 고전압 차단 → 정비 → 최종 확인";
    [TextArea(2,3)] public string freeModeDesc =
        "차량 부품을 자유롭게 탈거·재조립합니다.\n인벤토리로 부품 관리, 멀티플레이 지원";

    private SessionData.GameMode _selected = SessionData.GameMode.Scenario;
    private bool _isHost;

    // ── 생명주기 ──────────────────────────────────────────

    private void Start()
    {
        // 현재 방장 여부 확인 (서버 = 방장)
        _isHost = InstanceFinder.IsServerStarted;

        SetInteractable(_isHost);
        RefreshButtons();
        UpdateModeLabel();

        btnScenario.onClick.AddListener(() => SelectMode(SessionData.GameMode.Scenario));
        btnFreeMode.onClick.AddListener(() => SelectMode(SessionData.GameMode.FreeMode));

        AddHoverEvents(btnScenario, scenarioDesc);
        AddHoverEvents(btnFreeMode, freeModeDesc);

        // 비방장: 방장의 선택을 받아서 표시 (RPC 필요 — 아래 참고)
        if (!_isHost)
        {
            if (descriptionText != null)
                descriptionText.text = "방장이 모드를 선택합니다.";
        }
    }

    // ── 모드 선택 ──────────────────────────────────────────

    private void SelectMode(SessionData.GameMode mode)
    {
        if (!_isHost) return;

        _selected = mode;
        SessionData.gameMode = mode;    // ★ 저장 — Game 씬에서 읽음

        RefreshButtons();
        UpdateModeLabel();

        Debug.Log($"[LobbyModeSelector] 모드 선택: {mode}");

        // TODO: 비방장에게 모드 알리기
        // 기존 LobbyManager의 RPC를 활용하거나
        // BroadcastModeToClients(mode) 호출
    }

    // ── UI 갱신 ────────────────────────────────────────────

    private void RefreshButtons()
    {
        SetButtonColor(btnScenario,
            _selected == SessionData.GameMode.Scenario);
        SetButtonColor(btnFreeMode,
            _selected == SessionData.GameMode.FreeMode);
    }

    private void SetButtonColor(Button btn, bool isSelected)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null)
            img.color = isSelected ? selectedColor : unselectedColor;
    }

    private void UpdateModeLabel()
    {
        if (modeLabel == null) return;
        modeLabel.text = _selected == SessionData.GameMode.Scenario
            ? "현재 모드: 시나리오"
            : "현재 모드: 자유탈거";
    }

    private void SetInteractable(bool interactable)
    {
        if (btnScenario != null) btnScenario.interactable = interactable;
        if (btnFreeMode  != null) btnFreeMode.interactable  = interactable;
    }

    private void AddHoverEvents(Button btn, string desc)
    {
        if (btn == null) return;
        var trigger = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();

        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            if (descriptionText != null) descriptionText.text = desc;
        });

        var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ =>
        {
            if (descriptionText != null) descriptionText.text = "";
        });

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }
}

// ============================================================
//  NOTE: 비방장에게 모드 동기화하는 방법
//
//  기존 LobbyManager에 아래를 추가하면 됩니다:
//
//  [ObserversRpc]
//  public void RpcSyncGameMode(int modeInt)
//  {
//      SessionData.gameMode = (SessionData.GameMode)modeInt;
//      // LobbyModeSelector.UpdateModeLabel() 호출
//      FindFirstObjectByType<LobbyModeSelector>()?.SyncFromRpc(SessionData.gameMode);
//  }
//
//  방장이 SelectMode() 호출 시 → RpcSyncGameMode((int)mode) 호출
// ============================================================
