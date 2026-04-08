using UnityEngine;

// ============================================================
//  ImpactWrench.cs  — VR + PC 통합 + 아웃라인 색상 변경 (개선)
//  
//  ★ [수정] 변경할 색상과 복구할 색상을 모두 Inspector에서 설정
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

    [Header("── ★ 아웃라인 색상 설정 ──")]
    [Tooltip("볼트 감지 시 변경할 아웃라인 색상")]
    public Color targetOutlineColor = new Color(1f, 1f, 0f, 1f);  // 노란색

    [Tooltip("아웃라인 복구 시 설정할 색상 (기본값: 흰색)")]
    public Color restoreOutlineColor = Color.white;  // ★ 복구 색상

    [Header("오디오")]
    public AudioSource workAudio;

    [Header("PC 설정")]
    public KeyCode workKey = KeyCode.F;

    private static readonly int IsWorkingHash = Animator.StringToHash("isWorking");

    private Bolt _targetBolt;
    private GameObject _currentBoltGameObject;
    private MikeNspired.XRIStarterKit.ChrisNolet.Outline _currentOutline;

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
        if(Input.GetKey(workKey)) _buttonHeld = true;
        if(Input.GetKeyUp(workKey)) _buttonHeld = false;

        bool working = _buttonHeld;
        ApplyEffects(working);

        if(working && _targetBolt != null)
        {
            if(_targetBolt.isloosened)
            {
                TriggerBoltAnimation(_targetBolt.gameObject);
                return;
            }

            float dir = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
            _targetBolt.InteractWithTool(dir * rotationSpeed * Time.deltaTime);

            if(_targetBolt.isloosened)
                TriggerBoltAnimation(_targetBolt.gameObject);
        }
    }

    // ── 효과 적용 ───────────────────────────────────────────

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

    // ── 볼트 감지 (OnTriggerEnter/Exit) ────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;

        _targetBolt = other.GetComponent<Bolt>();
        _currentBoltGameObject = other.gameObject;

        Debug.Log($"[ImpactWrench] 볼트 감지: {other.name}");

        if(_currentOutline != null)
        {
            RestoreBoltOutline();
        }

        ChangeBoltOutlineColor();
    }

    private void OnTriggerExit(Collider other)
    {
        if(!other.CompareTag("Bolt")) return;

        if(_currentBoltGameObject != null && _currentBoltGameObject == other.gameObject)
        {
            Debug.Log($"[ImpactWrench] 볼트 이탈: {other.name}");

            RestoreBoltOutline();

            _targetBolt = null;
            _currentBoltGameObject = null;
        }
    }

    /// <summary>
    /// ★ 볼트의 아웃라인 색상을 targetOutlineColor로 변경
    /// </summary>
    private void ChangeBoltOutlineColor()
    {
        if(_targetBolt == null || _currentBoltGameObject == null) return;

        _currentOutline = _currentBoltGameObject.GetComponent<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();

        if(_currentOutline == null)
        {
            _currentOutline = _currentBoltGameObject.GetComponentInParent<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();
        }

        if(_currentOutline == null)
        {
            Debug.LogWarning($"[ImpactWrench] 볼트 '{_currentBoltGameObject.name}'에 Outline 컴포넌트가 없습니다!");
            return;
        }

        // ★ [수정] 미리 할당된 targetOutlineColor로 변경
        _currentOutline.OutlineColor = targetOutlineColor;
        Debug.Log($"[ImpactWrench] ✅ 아웃라인 색상 변경: {targetOutlineColor}");
    }

    /// <summary>
    /// ★ [수정] 볼트의 아웃라인 색상을 restoreOutlineColor로 복구
    /// </summary>
    private void RestoreBoltOutline()
    {
        if(_currentOutline == null)
        {
            Debug.LogWarning("[ImpactWrench] 복구할 아웃라인이 없습니다!");
            return;
        }

        // ★ [수정] 미리 할당된 restoreOutlineColor로 복구
        _currentOutline.OutlineColor = restoreOutlineColor;
        Debug.Log($"[ImpactWrench] ✅ 아웃라인 색상 복구: {restoreOutlineColor}");

        _currentOutline = null;
    }

    /// <summary>
    /// ★ 강제로 아웃라인 복구 (OnDisable 등에서)
    /// </summary>
    private void RestoreBoltOutlineForced()
    {
        if(_currentOutline != null)
        {
            Debug.LogWarning($"[ImpactWrench] 강제 복구 실행! 색상: {restoreOutlineColor}");
            _currentOutline.OutlineColor = restoreOutlineColor;
            _currentOutline = null;
        }

        _targetBolt = null;
        _currentBoltGameObject = null;
    }

    // ── VR 연동 ─────────────────────────────────────────────

    public void VRPress()
    {
        _buttonHeld = true;
    }

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

    private void TriggerBoltAnimation(GameObject boltGO)
    {
        var ca = boltGO.GetComponent<ClickableAnimator>();
        if(ca == null) return;
        if(ca.IsOpen) return;

        ca.Open();

        RestoreBoltOutline();
        _targetBolt = null;
        _currentBoltGameObject = null;

        Debug.Log($"[ImpactWrench] 볼트 애니메이션 실행: {boltGO.name}");
    }

    // ── 공개 API ───────────────────────────────────────────

    public bool IsWorking => _wasWorking;
    public Bolt TargetBolt => _targetBolt;
}
