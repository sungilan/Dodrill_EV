using ER.Selection;
using System.Collections;
using UnityEngine;

public class StartupManager : MonoBehaviour
{
    public static StartupManager Instance;

    [Header("Spawn Settings")]
    public VehicleSpawner spawner;      // 차량 스폰 담당
    public Transform playerStartPoint; // VR 카메라 리그 시작 위치
    public GameObject playerRig;       // XR Origin 또는 Hurricane Rig

    [Header("UI & Intro")]
    public GameObject loadingCanvas;   // 시작 전 암전/로딩 화면
    public float introDelay = 2f;

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        // 1. 암전 상태에서 시작 (VR 멀미 방지 및 로딩 처리)
        if(loadingCanvas != null) loadingCanvas.SetActive(true);

        // 2. 플레이어 위치 초기화
        InitializePlayer();

        yield return new WaitForSeconds(0.5f);

        // 3. 차량 소환 및 부품 등록
        if(spawner != null)
        {
            spawner.SpawnAndInitialize();
        }

        // 4. 시스템 초기화 확인 (싱글톤들이 준비될 시간 확보)
        yield return new WaitForSeconds(introDelay);

        // 5. 로딩 화면 해제 및 첫 가이드 시작
        if(loadingCanvas != null) loadingCanvas.SetActive(false);

        StartScenario();
    }

    private void InitializePlayer()
    {
        if(playerRig != null && playerStartPoint != null)
        {
            playerRig.transform.position = playerStartPoint.position;
            playerRig.transform.rotation = playerStartPoint.rotation;
        }
    }

    private void StartScenario()
    {
        // 가상 어시스턴트의 첫 마디 시작
        string carName = "아이오닉 5"; // 실제로는 선택된 차량 데이터에서 가져옴
        //AIGuideManager.Instance.StartGuide();

        // TTS 도입부 출력
        if(TTSManager.Instance != null)
        {
            TTSManager.Instance.Speak($"{carName} 정비 교육을 시작합니다. 작업대에서 안전 장갑을 착용하세요.");
        }

        Debug.Log("[Startup] 모든 시스템 준비 완료. 시나리오 시작.");
    }

    public void InitializeTraining(VehicleData selectedData)
    {
        // 1. 선택 UI 비활성화
        // (이미 Controller에서 끄고 있지만, 여기서 한 번 더 관리해도 좋습니다)

        // 2. 스포너에 선택된 프리팹 전달 및 생성 시작
        if(spawner != null)
        {
            spawner.selectedPrefab = selectedData.modelPrefab; // 데이터 전달
            StartCoroutine(StartScenarioRoutine(selectedData.modelName)); // 코루틴 실행
        }
    }

    private IEnumerator StartScenarioRoutine(string vehicleName)
    {
        // 암전/로딩 시작
        if(loadingCanvas != null) loadingCanvas.SetActive(true);

        InitializePlayer();
        yield return new WaitForSeconds(0.5f);

        // 실제 차량 스폰 및 부품 등록
        spawner.SpawnAndInitialize();

        yield return new WaitForSeconds(introDelay);

        if(loadingCanvas != null) loadingCanvas.SetActive(false);

        // TTS 가이드 시작
        AIGuideManager.Instance.StartGuide();
        if(TTSManager.Instance != null)
        {
            TTSManager.Instance.Speak($"{vehicleName} 정비 교육을 시작합니다. 먼저 작업대에서 절연 장갑을 착용하세요.");
        }
    }
}