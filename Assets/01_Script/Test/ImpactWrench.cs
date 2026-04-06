using UnityEngine;

// ============================================================
//  ImpactWrench.cs  — VR + PC 통합
//
//  PC 동작:
//    - F키 누르는 동안 → 애니메이션/사운드 재생 (볼트 유무 무관)
//    - F키 누르는 동안 볼트에 닿아 있으면 → 볼트 제거 진행
//    - F키 떼면 → 즉시 중단
//
//  VR 동작:
//    - Squeeze(AutoHand) → VRPress() 호출 → PC F키와 동일 흐름
//    - Squeeze 해제 → VRRelease()
//
//  FireItemUsed 사용 안 함 — ImpactWrench 자체에서 입력 처리
// ============================================================
public class ImpactWrench : MonoBehaviour
{
    public enum WrenchMode { Unscrew, Screw }

    [Header("설정")]
    public WrenchMode currentMode = WrenchMode.Unscrew;
    public float rotationSpeed = 1f;

    [Header("비주얼")]
    public MeshRenderer modeIndicator;
    public Color unscrewColor = Color.red;
    public Color screwColor = Color.green;
    public Animator wrenchAnimator;

    [Header("오디오")]
    public AudioSource workAudio;

    [Header("PC 설정")]
    public KeyCode workKey = KeyCode.F;

    private static readonly int IsWorkingHash = Animator.StringToHash("isWorking");

    // 볼트 접촉 여부
    private Bolt _targetBolt;

    // 실제 "버튼 누름" 상태 (PC: GetKey, VR: Squeeze)
    private bool _buttonHeld;

    // 이전 프레임 작동 상태 (변화 감지용)
    private bool _wasWorking;

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        UpdateVisuals();
    }

    private void OnDisable()
    {
        _buttonHeld = false;
        ApplyEffects(false);
    }

    private void Update()
    {
        // PC: F키 상태를 매 프레임 반영
        // VR: _buttonHeld는 VRPress/VRRelease로 외부에서 세팅
#if !UNITY_EDITOR
        // 빌드에서는 VR 입력만 사용 → PC 키 무시하려면 아래 주석 처리
#endif
        if(Input.GetKey(workKey)) _buttonHeld = true;
        if(Input.GetKeyUp(workKey)) _buttonHeld = false;

        // 효과: 버튼 누르는 동안 항상 재생
        bool working = _buttonHeld;
        ApplyEffects(working);

        // 볼트 작업: 버튼 누르는 동안 + 볼트 접촉 시
        if(working && _targetBolt != null)
        {
            // 이미 다 풀린 볼트 — 애니메이션 트리거하고 스킵
            if(_targetBolt.isloosened)
            {
                TriggerBoltAnimation(_targetBolt.gameObject);
                return;
            }

            float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
            _targetBolt.InteractWithTool(dir * rotationSpeed * Time.deltaTime);

            // 방금 다 풀린 순간 감지 → 애니메이션 실행
            if(_targetBolt.isloosened)
                TriggerBoltAnimation(_targetBolt.gameObject);
        }
    }

    // ── 효과 적용 (애니메이션 + 사운드) ───────────────────

    private void ApplyEffects(bool working)
    {
        if(working == _wasWorking) return;
        _wasWorking = working;

        if(wrenchAnimator != null)
            wrenchAnimator.SetBool(IsWorkingHash, working);

        if(workAudio != null)
        {
            if(working && !workAudio.isPlaying) workAudio.Play();
            else if(!working) workAudio.Stop();
        }

        Debug.Log($"[ImpactWrench] {(working ? "ON" : "OFF")} | 볼트: {(_targetBolt != null ? _targetBolt.name : "없음")}");
    }

    // ── 볼트 감지 ──────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;
        _targetBolt = other.GetComponent<Bolt>();
        Debug.Log($"[ImpactWrench] 볼트 감지: {other.name}");
    }

    private void OnTriggerExit(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;
        if(_targetBolt != null && _targetBolt.gameObject == other.gameObject)
        {
            _targetBolt = null;
            Debug.Log("[ImpactWrench] 볼트 이탈");
        }
    }

    // ── VR 연동 (AutoHand Squeeze) ─────────────────────────

    /// <summary>AutoHand Squeeze 시작 시 호출 (Grabbable.onSqueeze 이벤트에 연결)</summary>
    public void VRPress()
    {
        _buttonHeld = true;
    }

    /// <summary>AutoHand Squeeze 해제 시 호출 (Grabbable.onUnsqueeze 이벤트에 연결)</summary>
    public void VRRelease()
    {
        _buttonHeld = false;
    }

    // ── 모드 토글 ──────────────────────────────────────────

    public void ToggleMode()
    {
        currentMode = (currentMode == WrenchMode.Unscrew) ? WrenchMode.Screw : WrenchMode.Unscrew;
        UpdateVisuals();
        Debug.Log($"[ImpactWrench] 모드: {currentMode}");
    }

    private void UpdateVisuals()
    {
        if(modeIndicator != null)
            modeIndicator.material.color =
                (currentMode == WrenchMode.Unscrew) ? unscrewColor : screwColor;
    }

    // ── 볼트 애니메이션 트리거 ────────────────────────────────

    /// <summary>
    /// progress >= 1 이 된 순간 호출.
    /// 같은 GO의 ClickableAnimator.Open()으로 볼트 풀리는 애니메이션 실행.
    /// ClickableAnimator가 FishNet Broadcast로 전체 클라 동기화 처리.
    /// </summary>
    private void TriggerBoltAnimation(GameObject boltGO)
    {
        var ca = boltGO.GetComponent<ClickableAnimator>();
        if(ca == null) return;
        if(ca.IsOpen) return;   // 이미 실행됨

        ca.Open();
        Debug.Log($"[ImpactWrench] 볼트 애니메이션 실행: {boltGO.name}");
    }

    // ── 공개 API ───────────────────────────────────────────

    public bool IsWorking => _wasWorking;
    public Bolt TargetBolt => _targetBolt;
}