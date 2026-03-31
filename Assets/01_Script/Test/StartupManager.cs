using ER.Selection;
using FishNet;
using System.Collections;
using UnityEngine;

// ============================================================
//  StartupManager.cs
//  Game 씬 로드 후 SessionData.gameMode를 읽어서 분기
//
//  흐름:
//    Lobby 씬 → LobbyModeSelector에서 SessionData.gameMode 저장
//    → Game 씬 로드 (FishNet)
//    → StartupManager.Start() → gameMode 읽기 → 분기
// ============================================================
public class StartupManager : MonoBehaviour
{
    public static StartupManager Instance;

    [Header("참조")]
    public VehicleSpawner spawner;
    public Transform      playerStartPoint;
    public GameObject     playerRig;

    [Header("UI")]
    public GameObject loadingCanvas;
    public float      introDelay = 0.5f;

    [Header("모드별 루트")]
    [Tooltip("EVBootstrapper가 붙은 GO — 초기 비활성 상태로 배치")]
    public GameObject scenarioBootstrapRoot;
    //public FreeModeManager freeModeManager;

    private void Awake()
    {
        Instance = this;

        // Game 씬 진입 시 시나리오 루트는 항상 비활성에서 시작
        if (scenarioBootstrapRoot != null)
            scenarioBootstrapRoot.SetActive(false);
    }

    private IEnumerator Start()
    {
        if (loadingCanvas != null) loadingCanvas.SetActive(true);
        InitializePlayer();

        // 차량 스폰 (모드 무관 공통)
        if (spawner != null && spawner.selectedPrefab != null)
            spawner.SpawnAndInitialize();

        yield return new WaitForSeconds(introDelay);
        if (loadingCanvas != null) loadingCanvas.SetActive(false);

        // ★ 로비에서 저장한 모드로 분기
        switch (SessionData.gameMode)
        {
            case SessionData.GameMode.FreeMode:
                StartFreeMode();
                break;

            case SessionData.GameMode.Scenario:
            default:
                StartScenarioMode();
                break;
        }
    }

    // ── 시나리오 모드 ──────────────────────────────────────

    private void StartScenarioMode()
    {
        if (scenarioBootstrapRoot != null)
            scenarioBootstrapRoot.SetActive(true);
        // EVBootstrapper.Start()가 여기서 실행됨

        Debug.Log("[StartupManager] 시나리오 모드");
    }

    // ── 자유탈거 모드 ──────────────────────────────────────

    private void StartFreeMode()
    {
        //freeModeManager?.EnterFreeMode();
        // FishNet은 Lobby→Game 씬 전환 시 이미 연결된 상태
        // FreeModeManager 안에서 멀티플레이 동기화 가능

        Debug.Log("[StartupManager] 자유탈거 모드");
    }

    // ── 공통 ───────────────────────────────────────────────

    private void InitializePlayer()
    {
        if (playerRig != null && playerStartPoint != null)
            playerRig.transform.SetPositionAndRotation(
                playerStartPoint.position, playerStartPoint.rotation);
    }

    public void InitializeTraining(VehicleData selectedData)
    {
        if (spawner != null)
            spawner.selectedPrefab = selectedData.modelPrefab;
    }
}
