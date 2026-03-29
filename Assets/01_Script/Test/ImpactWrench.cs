using UnityEngine;
using FishNet;

// ============================================================
//  ImpactWrench.cs  — VR + PC 통합
//
//  VR:  XRGrabInteractable 상속 제거 → SyncGrab으로 집기
//       Trigger(Activate) → isWorking = true → Bolt 풀림
//
//  PC:  FreeLookController F키(useKey) → InteractionEvents.FireItemUsed
//       ImpactWrench가 구독 → targetBolt 작동
//
//  씬 세팅:
//    - ImpactWrench 프리팹에 TaskItem + SyncGrab 부착
//    - 볼트 오브젝트에 Tag = "Bolt" + Sphere Collider(Trigger)
//    - ImpactWrench 앞쪽에 Trigger Collider (소켓 감지용)
// ============================================================
public class ImpactWrench : MonoBehaviour
{
    public enum WrenchMode { Unscrew, Screw }

    [Header("설정")]
    public WrenchMode currentMode = WrenchMode.Unscrew;
    public float rotationSpeed = 1f;  // Bolt.InteractWithTool에 전달할 deltaProgress 배수

    [Header("비주얼")]
    public MeshRenderer modeIndicator;
    public Color unscrewColor = Color.red;
    public Color screwColor = Color.green;

    [Header("오디오")]
    public AudioSource workAudio;

    [Header("테스트")]
    [Tooltip("true: F키 누르는 동안 계속 작동 / false: 볼트 콜라이더에 닿아야만 작동")]
    public bool pcHoldToWork = true;

    // 현재 작업 중인 볼트
    private Bolt _targetBolt;
    private bool _isWorking;

    // ── 생명주기 ───────────────────────────────────────

    private void Awake()
    {
        UpdateVisuals();
    }

    private void OnEnable()
    {
        // PC: F키(FireItemUsed) 이벤트 구독
        InteractionEvents.OnItemUsed += HandleItemUsed;
    }

    private void OnDisable()
    {
        InteractionEvents.OnItemUsed -= HandleItemUsed;
    }

    private void Update()
    {
        if (!_isWorking || _targetBolt == null) return;

        float direction = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
        _targetBolt.InteractWithTool(direction * rotationSpeed * Time.deltaTime);

        // PC에서 F키를 떼면 중단
        if (pcHoldToWork && !Input.GetKey(KeyCode.F))
            _isWorking = false;
    }

    // ── PC 입력 ────────────────────────────────────────

    private void HandleItemUsed(string itemId)
    {
        // 자기 자신의 prefabId가 호출된 경우만 반응
        var taskItem = GetComponent<TaskItem>();
        if (taskItem == null || taskItem.prefabId != itemId) return;

        _isWorking = !_isWorking;  // 토글
        Debug.Log($"[ImpactWrench] {(currentMode == WrenchMode.Unscrew ? "해체" : "체결")} {(_isWorking ? "시작" : "중지")} / 볼트: {(_targetBolt != null ? _targetBolt.name : "없음")}");

        if (workAudio != null)
        {
            if (_isWorking) workAudio.Play();
            else workAudio.Stop();
        }
    }

    // ── 모드 토글 (VR 버튼 or E키) ────────────────────

    public void ToggleMode()
    {
        currentMode = (currentMode == WrenchMode.Unscrew) ? WrenchMode.Screw : WrenchMode.Unscrew;
        UpdateVisuals();
        Debug.Log($"[ImpactWrench] 모드 변경: {currentMode}");
    }

    // ── 볼트 감지 (Trigger Collider) ───────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;
        _targetBolt = other.GetComponent<Bolt>();
        Debug.Log($"[ImpactWrench] 볼트 감지: {other.name}");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;
        _targetBolt = null;
        _isWorking = false;
        if (workAudio != null) workAudio.Stop();
    }

    // ── VR Squeeze 연동 (AutoHand) ─────────────────────

    // VR에서 Squeeze 시 TaskItem.OnUsed → FireItemUsed → HandleItemUsed 호출됨
    // 별도 코드 불필요

    // ── 비주얼 ─────────────────────────────────────────

    private void UpdateVisuals()
    {
        if (modeIndicator != null)
            modeIndicator.material.color = (currentMode == WrenchMode.Unscrew) ? unscrewColor : screwColor;
    }

    // ── 공개 API ───────────────────────────────────────

    public bool IsWorking => _isWorking;
    public Bolt TargetBolt => _targetBolt;
    public WrenchMode Mode => currentMode;
}