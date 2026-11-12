using Autohand;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 같은 오브젝트의 <see cref="FingerTriggerAreaEvents"/>가 검지를 감지하면
/// 지정한 <see cref="Button"/>에 <see cref="VrUiButtonFingerFeedback"/>를 적용합니다.
/// VR_UICanvas 바·홈·종료 등에 붙여 사용합니다.
///
/// [DoDrill] 오작동 방지:
///  · 팝업(종료·홈·게임종료)은 숨겨도 오브젝트가 켜진 채 알파만 0이라 손가락 트리거가 계속 살아 있다.
///    세 팝업의 확인/취소 버튼이 같은 자리에 겹쳐 있어서 손이 지나가면 '보이지 않는 확인'이 눌려 앱이 꺼졌다.
///    → 버튼이 실제로 보이고(알파·interactable) 클릭 가능한 동안에만 반응한다.
///  · 팝업이 막 떴을 때 손가락이 이미 영역 안에 있으면 눌림/클릭을 무시한다(유예시간).
///  · 한 번 클릭되면 짧은 시간 동안 다른 VR 버튼은 반응하지 않는다(겹침·연타 방지).
///  · Exit 클릭은 '유효한 Enter 가 먼저 있었을 때'만 인정한다.
/// </summary>
[DisallowMultipleComponent]
public class VrFingerUiButtonDriver : MonoBehaviour
{
    [Tooltip("비우면 이 오브젝트의 Button을 사용합니다.")]
    [SerializeField] private Button targetButton;
    [Tooltip("true면 FingerExit에서 클릭(요청사항), false면 FingerEnter에서 클릭합니다.")]
    [SerializeField] private bool _clickOnFingerExit = true;
    [Tooltip("버튼이 보이게 된 직후 이 시간(초) 동안은 손가락 입력을 무시합니다.")]
    [SerializeField] private float _graceAfterVisible = 0.45f;
    [Tooltip("어떤 VR 버튼이든 클릭된 직후 이 시간(초) 동안은 다른 버튼 클릭을 무시합니다.")]
    [SerializeField] private float _globalCooldown = 0.5f;

    private static float s_lastClickTime = -10f;

    /// <summary>
    /// 손가락이 버튼 안에 이 시간(초) 이상 머물렀다 빠져야 클릭으로 친다. 0이면 기존과 같다.
    /// 손을 뻗다가 스치고 지나간 것을 클릭으로 받지 않기 위해 쓴다(MR 전환 버튼 등).
    /// </summary>
    public float MinHoldSeconds { get; set; }
    float _enterTime;

    FingerTriggerAreaEvents _finger;
    ClickButton             _clickButton;

    bool  _wasUsable;
    float _usableSince;
    bool  _armed;        // 유효한 Enter 가 있었음

    void Awake()
    {
        _finger = GetComponent<FingerTriggerAreaEvents>();
        if (targetButton == null)
            targetButton = GetComponent<Button>();
        _clickButton = GetComponent<ClickButton>();
    }

    void OnEnable()
    {
        _wasUsable = false;
        _armed = false;
        if (_finger != null)
        {
            _finger.FingerEnterEvent.AddListener(OnFingerEnter);
            _finger.FingerExitEvent.AddListener(OnFingerExit);
        }
    }

    void OnDisable()
    {
        if (_finger != null)
        {
            _finger.FingerEnterEvent.RemoveListener(OnFingerEnter);
            _finger.FingerExitEvent.RemoveListener(OnFingerExit);
        }
        _armed = false;
    }

    void Update()
    {
        bool usable = IsUsable();
        if (usable && !_wasUsable) _usableSince = Time.unscaledTime;
        if (!usable && _wasUsable)
        {
            // 숨겨지는 순간 눌림 표시·무장 해제
            _armed = false;
            ReleaseVisual();
        }
        _wasUsable = usable;
    }

    /// <summary>버튼이 화면에 보이고 클릭 가능한 상태인지(숨겨진 팝업의 겹친 버튼 걸러내기).</summary>
    bool IsUsable()
    {
        if (targetButton == null || !targetButton.isActiveAndEnabled) return false;
        if (!targetButton.IsInteractable()) return false;
        var groups = targetButton.GetComponentsInParent<CanvasGroup>(false);
        for (int i = 0; i < groups.Length; i++)
        {
            var g = groups[i];
            if (!g.interactable || g.alpha < 0.5f) return false;
            if (g.ignoreParentGroups) break;
        }
        return true;
    }

    bool InputAllowed()
    {
        if (!IsUsable()) return false;
        if (Time.unscaledTime - _usableSince < _graceAfterVisible) return false;
        return true;
    }

    void PressVisual()
    {
        if (_clickButton == null) return;
        var es = EventSystem.current;
        _clickButton.OnPointerDown(es != null ? new PointerEventData(es) : new PointerEventData(null));
    }

    void ReleaseVisual()
    {
        if (_clickButton == null) return;
        var es = EventSystem.current;
        _clickButton.OnPointerUp(es != null ? new PointerEventData(es) : new PointerEventData(null));
    }

    void OnFingerEnter(Finger finger, FingerTriggerAreaEvents area)
    {
        // 숨겨진/막 뜬 팝업, 다른 버튼이 방금 클릭된 직후에는 반응하지 않는다.
        if (!InputAllowed() || Time.unscaledTime - s_lastClickTime < _globalCooldown) return;
        _armed = true;
        _enterTime = Time.unscaledTime;

        // Enter 시에는 하이라이트/눌림만 적용, 클릭(onClick)은 하지 않음.
        if (_clickOnFingerExit)
        {
            PressVisual();
            return;
        }

        // Enter 기준 클릭 모드: 즉시 클릭 + 피드백.
        s_lastClickTime = Time.unscaledTime;
        VrUiButtonFingerFeedback.InvokeWithFeedback(targetButton);
    }

    void OnFingerExit(Finger finger, FingerTriggerAreaEvents area)
    {
        if (!_clickOnFingerExit)
        {
            // Enter 기준 클릭 모드에서는 Exit 시 눌림만 해제.
            ReleaseVisual();
            _armed = false;
            return;
        }

        // Exit 기준 클릭 모드: 유효한 Enter 가 있었고, 지금도 클릭 가능한 상태일 때만 실행.
        bool fire = _armed && InputAllowed() && Time.unscaledTime - s_lastClickTime >= _globalCooldown
                    && Time.unscaledTime - _enterTime >= MinHoldSeconds;   // 스치고 지나간 손가락은 무시
        _armed = false;
        ReleaseVisual();
        if (!fire) return;

        s_lastClickTime = Time.unscaledTime;
        VrUiButtonFingerFeedback.InvokeWithFeedback(targetButton);
    }
}
