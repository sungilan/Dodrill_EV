using Autohand;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR_UICanvas — <see cref="FingerTriggerAreaEvents"/> / <see cref="FingerTriggerAreaToggleEvents"/>의
/// UnityEvent에서 호출해 기존 Button·<see cref="BottomButtonsPanel"/> 동작을 재현합니다.
/// 컨트롤러 레이로 누를 때는 UIPointer(HandCanvasPointer) StartSelect만 소리를 내고,
/// Button/Toggle의 onClick·onValueChanged에는 소리를 붙이지 않아 이중 재생을 막습니다.
/// 손가락으로 누를 때는 <see cref="VrUiButtonFingerFeedback"/>가 이 컴포넌트에 알려 클릭음을 냅니다.
/// </summary>
public class VrUiFingerBindings : MonoBehaviour
{
    [SerializeField] private BottomButtonsPanel _bottomButtonsPanel;
    [SerializeField] private Button _exitButton;
    [Header("UI Click Sound")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clickClip;
    [SerializeField] private float _clickVolume = 1f;
    [SerializeField] private bool _autoCreateAudioSource = true;
    [SerializeField] private float _minClickInterval = 0.08f;

    float _lastClickSoundTime = -999f;

    void Awake()
    {
        if (_audioSource == null && _autoCreateAudioSource)
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
        }
    }

    /// <summary>
    /// 손가락·<see cref="VrUiButtonFingerFeedback"/> 경로 전용. 컨트롤러 UIPointer와 겹치지 않습니다.
    /// </summary>
    public static void NotifyFingerDrivenClick(Button button)
    {
        if (button == null) return;
        var root = button.GetComponentInParent<VrUiFingerBindings>();
        root?.PlayFingerUiClickSound();
    }

    void PlayFingerUiClickSound()
    {
        if (_clickClip == null || _audioSource == null) return;
        if (Time.unscaledTime - _lastClickSoundTime < _minClickInterval) return;
        _lastClickSoundTime = Time.unscaledTime;
        _audioSource.PlayOneShot(_clickClip, _clickVolume);
    }

    /// <summary>
    /// 하단 패널: UI 버튼과 동일하게 <see cref="BottomButtonsPanel.Toggle"/> 한 번에 맞춥니다.
    /// </summary>
    public void Finger_ToggleBottomPanel(Finger finger, FingerTriggerAreaEvents area)
    {
        PlayFingerUiClickSound();
        _bottomButtonsPanel?.Toggle();
    }

    /// <summary>
    /// <see cref="FingerTriggerAreaToggleEvents"/>용 — 열기/닫기를 번갈아 호출할 때 사용합니다.
    /// (버튼 클릭과 같이 쓰면 토글 카운터가 어긋날 수 있어, 보통은 <see cref="Finger_ToggleBottomPanel"/>을 권장합니다.)
    /// </summary>
    public void Finger_SetBottomPanelOpen(Finger finger, FingerTriggerAreaEvents area)
    {
        PlayFingerUiClickSound();
        _bottomButtonsPanel?.SetOpen(true);
    }

    public void Finger_SetBottomPanelClosed(Finger finger, FingerTriggerAreaEvents area)
    {
        PlayFingerUiClickSound();
        _bottomButtonsPanel?.SetOpen(false);
    }

    public void Finger_InvokeExit(Finger finger, FingerTriggerAreaEvents area)
    {
        VrUiButtonFingerFeedback.InvokeWithFeedback(_exitButton);
    }
}
