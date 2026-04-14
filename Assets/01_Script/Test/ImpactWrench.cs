using EPOOutline; // ★ EPO Outline 네임스페이스 추가
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  ImpactWrench.cs  — EPO Outline 기반 아웃라인 색상 제어
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

    [Header("── ★ EPO 아웃라인 설정 ──")]
    [Tooltip("볼트 감지 시 변경할 아웃라인 색상")]
    public Color targetOutlineColor = new Color(1f, 1f, 0f, 1f); // 노란색

    [Tooltip("아웃라인 복구 시 설정할 색상 (기본값: 흰색)")]
    public Color restoreOutlineColor = Color.white;

    [Range(0, 10)]
    public float outlineWidth = 4f;

    [Header("오디오")]
    public AudioSource workAudio;

    [Header("PC 설정")]
    public KeyCode workKey = KeyCode.F;

    private static readonly int IsWorkingHash = Animator.StringToHash("isWorking");

    private Bolt _targetBolt;
    private GameObject _currentBoltGameObject;
    private Outlinable _currentOutlinable; // ★ EPO 전용 컴포넌트 참조

    private bool _buttonHeld;
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
        RestoreBoltOutlineForced();
    }

    private void Update()
    {
        // 입력 상태 확인
        // PC일 때만 키보드 입력을 체크하도록 조건 추가
        if(Utils.GetPlatformType() == PlatformType.PC)
        {
            _buttonHeld = Input.GetKey(workKey);
        }

        bool working = _buttonHeld; // VR은 VRPress/VRRelease를 통해 값이 유지됨
        ApplyEffects(working);

        if(_targetBolt != null)
        {
            string actionName = (currentMode == WrenchMode.Unscrew) ? "볼트 분해" : "볼트 체결";

            if(working)
            {
                if(_targetBolt.isloosened)
                {
                    TriggerBoltAnimation(_targetBolt.gameObject);
                    return;
                }

                float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
                _targetBolt.InteractWithTool(dir * rotationSpeed * Time.deltaTime);

                // [작업 중] 진행도와 함께 플랫폼 맞춤 문구 표시
                InteractionProgressBarUI.Instance?.ShowBoltProgress(
                    _targetBolt.Progress,
                    $"{actionName} 중...",
                    _currentBoltGameObject.name,
                    GetPlatformInstruction(actionName, true)
                );
            }
            else
            {
                // [대기 중] 안내 문구 표시
                InteractionProgressBarUI.Instance?.ShowBoltProgress(
                    _targetBolt.Progress,
                    actionName,
                    _currentBoltGameObject.name,
                    GetPlatformInstruction(actionName, false)
                );
            }
        }
    }

    // ── 효과 적용 ───────────────────────────────────────────

    private void ApplyEffects(bool working)
    {
        if (working == _wasWorking) return;
        _wasWorking = working;

        if (wrenchAnimator != null)
            wrenchAnimator.SetBool(IsWorkingHash, working);

        if (workAudio != null)
        {
            if (working && !workAudio.isPlaying) workAudio.Play();
            else if (!working) workAudio.Stop();
        }
    }

    // ── 볼트 감지 (OnTriggerEnter/Exit) ────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;

        _targetBolt = other.GetComponent<Bolt>();
        _currentBoltGameObject = other.gameObject;

        if(_currentOutlinable != null) RestoreBoltOutline();
        ChangeBoltOutlineColor();

        // 진입 시 즉시 플랫폼별 안내 표시
        string actionName = (currentMode == WrenchMode.Unscrew) ? "볼트 분해" : "볼트 체결";
        InteractionProgressBarUI.Instance?.ShowBoltProgress(
            _targetBolt != null ? _targetBolt.Progress : 0,
            actionName,
            other.name,
            GetPlatformInstruction(actionName, false)
        );
    }

    private void OnTriggerExit(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;

        if(_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
        {
            RestoreBoltOutline();

            // [이탈] 볼트에서 멀어지면 즉시 모든 가이드 패널 숨김
            InteractionProgressBarUI.Instance?.HideBoltProgress();

            _targetBolt = null;
            _currentBoltGameObject = null;
        }
    }

    /// <summary>
    /// ★ EPO Outlinable을 사용하여 색상 변경
    /// </summary>
    private void ChangeBoltOutlineColor()
    {
        if (_currentBoltGameObject == null) return;

        // 1. Outlinable 컴포넌트 확보
        _currentOutlinable = _currentBoltGameObject.GetComponent<Outlinable>();
        if (_currentOutlinable == null)
        {
            _currentOutlinable = _currentBoltGameObject.AddComponent<Outlinable>();
            _currentOutlinable.AddAllChildRenderersToRenderingList();
        }

        // 2. 아웃라인 활성화 및 색상 설정
        _currentOutlinable.enabled = true;
        var parameters = _currentOutlinable.OutlineParameters;
        parameters.Color = targetOutlineColor;
        parameters.BlurShift = outlineWidth;
        parameters.Enabled = true;

        Debug.Log($"[ImpactWrench] ✅ EPO 아웃라인 색상 변경: {targetOutlineColor}");
    }

    /// <summary>
    /// ★ EPO Outlinable 복구 (비활성화가 아닌 색상 복구)
    /// </summary>
    private void RestoreBoltOutline()
    {
        if (_currentOutlinable == null) return;

        // 색상을 원래(복구용) 색상으로 변경
        _currentOutlinable.OutlineParameters.Color = restoreOutlineColor;

        // 만약 가이드 시스템처럼 완전히 아웃라인을 끄고 싶다면 아래 주석 해제
        // _currentOutlinable.enabled = false;

        Debug.Log($"[ImpactWrench] ✅ EPO 아웃라인 색상 복구: {restoreOutlineColor}");
        _currentOutlinable = null;
    }

    private void RestoreBoltOutlineForced()
    {
        if (_currentOutlinable != null)
        {
            _currentOutlinable.OutlineParameters.Color = restoreOutlineColor;
            _currentOutlinable = null;
        }
        _targetBolt = null;
        _currentBoltGameObject = null;
    }

    // ── VR 연동 및 나머지 로직 ─────────────────────────────────────

    public void VRPress() => _buttonHeld = true;
    public void VRRelease() => _buttonHeld = false;

    public void ToggleMode()
    {
        currentMode = (currentMode == WrenchMode.Unscrew) ? WrenchMode.Screw : WrenchMode.Unscrew;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (modeIndicator != null)
            modeIndicator.material.color = (currentMode == WrenchMode.Unscrew) ? unscrewColor : screwColor;
    }

    private void TriggerBoltAnimation(GameObject boltGO)
    {
        var ca = boltGO.GetComponent<ClickableAnimator>();
        if (ca == null || ca.IsOpen) return;

        ca.Open();
        RestoreBoltOutline();

        // ★ 추가: 애니메이션 시작 시 UI 숨김
        InteractionProgressBarUI.Instance?.HideBoltProgress();

        _targetBolt = null;
        _currentBoltGameObject = null;
    }

    public bool IsWorking => _wasWorking;
    public Bolt TargetBolt => _targetBolt;

    // --- 플랫폼별 문구 생성 헬퍼 ---
    private string GetPlatformInstruction(string actionName, bool isWorking)
    {
        PlatformType platform = Utils.GetPlatformType();

        if(isWorking)
        {
            return platform switch
            {
                PlatformType.VR => "트리거를 계속 당겨 작업을 완료하세요.",
                PlatformType.PC => $"{workKey} 키를 계속 눌러 작업을 완료하세요.",
                PlatformType.Mobile => "버튼을 계속 터치하여 작업을 완료하세요.",
                _ => "작업을 완료할 때까지 유지하세요."
            };
        }
        else
        {
            return platform switch
            {
                PlatformType.VR => $"트리거를 당겨 {actionName}를 시작하세요.",
                PlatformType.PC => $"{workKey} 키를 눌러 {actionName}를 시작하세요.",
                PlatformType.Mobile => $"터치하여 {actionName}를 시작하세요.",
                _ => $"{actionName}를 시작하세요."
            };
        }
    }
}