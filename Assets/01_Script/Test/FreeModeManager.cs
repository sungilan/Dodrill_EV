using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  FreeModeManager.cs
//  자유탈거 모드 전역 상태 관리
//
//  씬 세팅:
//    - _Managers 하위에 배치
//    - FreeModeRaycaster, FreeModeInventoryUI, FreeModeSpawner와 함께 사용
// ============================================================
public class FreeModeManager : MonoBehaviour
{
    public static FreeModeManager Instance { get; private set; }

    // 다른 스크립트에서 정적으로 모드 확인
    public static bool IsActive => Instance != null && Instance._isActive;

    [Header("연결")]
    public FreeModeRaycaster raycaster;
    public GameObject inventoryUIRoot;    // 인벤토리 UI 루트 오브젝트
    public GameObject scenarioUIRoot;     // 시나리오 HUD 루트

    private bool _isActive;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 모드 진입/해제 ────────────────────────────────────

    public void EnterFreeMode()
    {
        _isActive = true;

        if (raycaster != null)         raycaster.enabled = true;
        if (inventoryUIRoot != null)   inventoryUIRoot.SetActive(false); // 시작은 닫힌 상태
        if (scenarioUIRoot != null)    scenarioUIRoot.SetActive(false);

        FreeModeInventory.Instance?.Clear();

        Debug.Log("[FreeModeManager] 자유탈거 모드 진입");
    }

    public void ExitFreeMode()
    {
        _isActive = false;

        if (raycaster != null)         raycaster.enabled = false;
        if (inventoryUIRoot != null)   inventoryUIRoot.SetActive(false);

        Debug.Log("[FreeModeManager] 자유탈거 모드 종료");
    }

    // ── 인벤토리 토글 (I키 or UI 버튼) ───────────────────

    private void Update()
    {
        if (!_isActive) return;
        if (Input.GetKeyDown(KeyCode.I))
            ToggleInventory();
    }

    public void ToggleInventory()
    {
        if (inventoryUIRoot == null) return;
        bool next = !inventoryUIRoot.activeSelf;
        inventoryUIRoot.SetActive(next);
        Debug.Log($"[FreeModeManager] 인벤토리 {(next ? "열기" : "닫기")}");
    }
}
