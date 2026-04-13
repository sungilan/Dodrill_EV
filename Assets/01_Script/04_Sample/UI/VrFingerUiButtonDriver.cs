using Autohand;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 같은 오브젝트의 <see cref="FingerTriggerAreaEvents"/>가 검지를 감지하면
/// 지정한 <see cref="Button"/>에 <see cref="VrUiButtonFingerFeedback"/>를 적용합니다.
/// VR_UICanvas 바·홈·종료 등에 붙여 사용합니다.
/// </summary>
[DisallowMultipleComponent]
public class VrFingerUiButtonDriver : MonoBehaviour
{
    [Tooltip("비우면 이 오브젝트의 Button을 사용합니다.")]
    [SerializeField] private Button targetButton;
    [Tooltip("true면 FingerExit에서 클릭(요청사항), false면 FingerEnter에서 클릭합니다.")]
    [SerializeField] private bool _clickOnFingerExit = true;

    FingerTriggerAreaEvents _finger;
    ClickButton             _clickButton;

    void Awake()
    {
        _finger = GetComponent<FingerTriggerAreaEvents>();
        if (targetButton == null)
            targetButton = GetComponent<Button>();
        _clickButton = GetComponent<ClickButton>();
    }

    void OnEnable()
    {
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
    }

    void OnFingerEnter(Finger finger, FingerTriggerAreaEvents area)
    {
        // Enter 시에는 하이라이트/눌림만 적용, 클릭(onClick)은 하지 않음.
        if (_clickOnFingerExit)
        {
            // Exit 기준 클릭 모드: Enter에서 시각만 "눌린 상태"로.
            if (_clickButton != null)
            {
                var es = EventSystem.current;
                var data = es != null ? new PointerEventData(es) : new PointerEventData(null);
                _clickButton.OnPointerDown(data);
            }
            return;
        }

        // Enter 기준 클릭 모드: 즉시 클릭 + 피드백.
        VrUiButtonFingerFeedback.InvokeWithFeedback(targetButton);
    }

    void OnFingerExit(Finger finger, FingerTriggerAreaEvents area)
    {
        if (!_clickOnFingerExit)
        {
            // Enter 기준 클릭 모드에서는 Exit 시 아무 것도 하지 않음.
            if (_clickButton != null)
            {
                var es = EventSystem.current;
                var data = es != null ? new PointerEventData(es) : new PointerEventData(null);
                _clickButton.OnPointerUp(data);
            }
            return;
        }

        // Exit 기준 클릭 모드: Exit 시 눌림 해제 + onClick 실행.
        if (_clickButton != null)
        {
            var es = EventSystem.current;
            var data = es != null ? new PointerEventData(es) : new PointerEventData(null);
            _clickButton.OnPointerDown(data);
            _clickButton.OnPointerUp(data);
        }

        VrUiButtonFingerFeedback.InvokeWithFeedback(targetButton);
    }
}
