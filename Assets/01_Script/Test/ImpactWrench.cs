using EPOOutline;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  ImpactWrench.cs  — 단계별 볼트 상호작용 검증 및 동적 문구 로직
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
    private Outlinable _currentOutlinable;

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
        if (Utils.GetPlatformType() == PlatformType.PC)
        {
            _buttonHeld = Input.GetKey(workKey);
        }

        bool working = _buttonHeld;
        ApplyEffects(working);

        if (_targetBolt != null)
        {
            // 실시간 검증: 작업 중 태스크가 바뀌는 상황 대비
            if (!IsCorrectBoltTask(_targetBolt))
            {
                InteractionProgressBarUI.Instance?.HideBoltProgress();
                RestoreBoltOutline();
                _targetBolt = null;
                return;
            }

            // ✅ 렌치 모드와 상관없이 볼트 자체의 모드에 따라 문구 결정
            string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";

            if (working)
            {
                if (_targetBolt.isloosened)
                {
                    TriggerBoltAnimation(_targetBolt.gameObject);
                    return;
                }

                // 렌치 회전 방향은 물리적 일관성을 위해 currentMode 유지 가능 (필요시 수정)
                float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
                _targetBolt.InteractWithTool(dir * rotationSpeed * Time.deltaTime, transform.forward);

                // [작업 중] 
                InteractionProgressBarUI.Instance?.ShowBoltProgress(
                    _targetBolt.Progress,
                    $"{actionName} 중...",
                    _currentBoltGameObject.name,
                    GetPlatformInstruction(actionName, true)
                );
            }
            else
            {
                // [대기 중]
                InteractionProgressBarUI.Instance?.ShowBoltProgress(
                    _targetBolt.Progress,
                    actionName,
                    _currentBoltGameObject.name,
                    GetPlatformInstruction(actionName, false)
                );
            }
        }
    }

    // ── 단계 검증 로직 ──────────────────────────────────────

    // ── 단계 검증 로직 (디버그 강화) ──────────────────────────────────────
    private bool IsCorrectBoltTask(Bolt bolt)
    {
        if (bolt == null) return false;

        // 1. 볼트 그룹의 설정 ID 가져오기
        BoltGroupCounter group = bolt.GetComponentInParent<BoltGroupCounter>();
        if (group == null) return false;

        // 2. ScenarioStateReceiver 및 시나리오 데이터 참조
        var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();
        if (stateReceiver == null || stateReceiver.CurrentScenario == null)
        {
            Debug.LogWarning("[ImpactWrench] 데이터 수신 대기 중...");
            return false;
        }

        // 3. 현재 인덱스를 사용하여 실제 시나리오 데이터에서 moduleId 추출
        int currentIndex = stateReceiver.CurrentTaskState.taskIndex;
        var tasks = stateReceiver.CurrentScenario.scenario.tasks;

        if (currentIndex < 0 || currentIndex >= tasks.Count) return false;

        // ✅ 숫자 인덱스가 아닌, JSON에 정의된 실제 문자열 ID를 가져옴
        string currentModuleId = tasks[currentIndex].moduleId;

        // 4. 비교 및 결과 반환
        bool isMatch = group.taskId.Equals(currentModuleId, System.StringComparison.OrdinalIgnoreCase);

        if (!isMatch)
        {
            Debug.Log($"<color=orange>[ImpactWrench]</color> 단계 불일치: 현재[<b>{currentModuleId}</b>] vs 볼트설정[<b>{group.taskId}</b>]");
        }
        else
        {
            Debug.Log($"<color=lime>[ImpactWrench]</color> 단계 일치 확인: {currentModuleId}");
        }

        return isMatch;
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
        if (!other.CompareTag("Bolt")) return;

        Debug.Log($"<color=white>[ImpactWrench]</color> 볼트 충돌 감지: {other.name}. 검증 시작...");

        Bolt detectedBolt = other.GetComponent<Bolt>();
        if (detectedBolt == null)
        {
            Debug.LogError($"<color=red>[ImpactWrench]</color> {other.name}에 Bolt 스크립트가 없습니다!");
            return;
        }

        // 검증 로직 실행
        if (!IsCorrectBoltTask(detectedBolt)) return;

        // 일치할 경우에만 아래 로직 실행
        _targetBolt = detectedBolt;
        _currentBoltGameObject = other.gameObject;

        if (_currentOutlinable != null) RestoreBoltOutline();
        ChangeBoltOutlineColor();

        string actionName = _targetBolt.isAssembleMode ? "볼트 체결" : "볼트 분해";
        InteractionProgressBarUI.Instance?.ShowBoltProgress(
            _targetBolt.Progress,
            actionName,
            other.name,
            GetPlatformInstruction(actionName, false)
        );
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;

        if (_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
        {
            RestoreBoltOutline();
            InteractionProgressBarUI.Instance?.HideBoltProgress();

            _targetBolt = null;
            _currentBoltGameObject = null;
        }
    }

    private void ChangeBoltOutlineColor()
    {
        if (_currentBoltGameObject == null) return;

        _currentOutlinable = _currentBoltGameObject.GetComponent<Outlinable>();
        if (_currentOutlinable == null)
        {
            _currentOutlinable = _currentBoltGameObject.AddComponent<Outlinable>();
            _currentOutlinable.AddAllChildRenderersToRenderingList();
        }

        _currentOutlinable.enabled = true;
        var parameters = _currentOutlinable.OutlineParameters;
        parameters.Color = targetOutlineColor;
        parameters.BlurShift = outlineWidth;
        parameters.Enabled = true;
    }

    private void RestoreBoltOutline()
    {
        if (_currentOutlinable == null) return;
        _currentOutlinable.OutlineParameters.Color = restoreOutlineColor;
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

    // ── 외부 연동 ─────────────────────────────────────

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

        InteractionProgressBarUI.Instance?.HideBoltProgress();

        _targetBolt = null;
        _currentBoltGameObject = null;
    }

    private string GetPlatformInstruction(string actionName, bool isWorking)
    {
        PlatformType platform = Utils.GetPlatformType();
        if (isWorking)
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