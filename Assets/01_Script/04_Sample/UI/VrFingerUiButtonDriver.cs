using Autohand;
using UnityEngine;
using UnityEngine.UI;

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

    FingerTriggerAreaEvents _finger;

    void Awake()
    {
        _finger = GetComponent<FingerTriggerAreaEvents>();
        if (targetButton == null)
            targetButton = GetComponent<Button>();
    }

    void OnEnable()
    {
        if (_finger != null)
            _finger.FingerEnterEvent.AddListener(OnFingerEnter);
    }

    void OnDisable()
    {
        if (_finger != null)
            _finger.FingerEnterEvent.RemoveListener(OnFingerEnter);
    }

    void OnFingerEnter(Finger finger, FingerTriggerAreaEvents area)
    {
        VrUiButtonFingerFeedback.InvokeWithFeedback(targetButton);
    }
}
