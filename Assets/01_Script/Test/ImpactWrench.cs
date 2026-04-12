using UnityEngine;
using EPOOutline; // ★ EPO Outline 네임스페이스 추가

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
        if (Input.GetKey(workKey)) _buttonHeld = true;
        if (Input.GetKeyUp(workKey)) _buttonHeld = false;

        bool working = _buttonHeld;
        ApplyEffects(working);

        if (working && _targetBolt != null)
        {
            if (_targetBolt.isloosened)
            {
                TriggerBoltAnimation(_targetBolt.gameObject);
                return;
            }

            // 회전 속도 보정 (timeToComplete 반영을 원하시면 Bolt 내부 로직을 따름)
            float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
            _targetBolt.InteractWithTool(dir * rotationSpeed * Time.deltaTime);

            if (_targetBolt.isloosened)
                TriggerBoltAnimation(_targetBolt.gameObject);
        }
        else if (!working) // 버튼을 뗐을 때 UI 숨김
        {
            InteractionProgressBarUI.Instance?.HideProgress();
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
        if (!other.CompareTag("Bolt")) return;

        _targetBolt = other.GetComponent<Bolt>();
        _currentBoltGameObject = other.gameObject;

        Debug.Log($"[ImpactWrench] 볼트 감지: {other.name}");

        // 기존에 잡고 있던 아웃라인이 있다면 복구
        if (_currentOutlinable != null)
        {
            RestoreBoltOutline();
        }

        ChangeBoltOutlineColor();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Bolt")) return;

        if (_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
        {
            Debug.Log($"[ImpactWrench] 볼트 이탈: {other.name}");

            RestoreBoltOutline();

            // ★ 추가: 볼트에서 멀어지면 프로그레스바 즉시 숨김
            InteractionProgressBarUI.Instance?.HideProgress();

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
        InteractionProgressBarUI.Instance?.HideProgress();

        _targetBolt = null;
        _currentBoltGameObject = null;
    }

    public bool IsWorking => _wasWorking;
    public Bolt TargetBolt => _targetBolt;
}