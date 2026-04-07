using Autohand;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR_UICanvas — <see cref="FingerTriggerAreaEvents"/> / <see cref="FingerTriggerAreaToggleEvents"/>의
/// UnityEvent에서 호출해 기존 Button·<see cref="BottomButtonsPanel"/> 동작을 재현합니다.
/// </summary>
public class VrUiFingerBindings : MonoBehaviour
{
    [SerializeField] private BottomButtonsPanel _bottomButtonsPanel;
    [SerializeField] private Button _exitButton;

    /// <summary>
    /// 하단 패널: UI 버튼과 동일하게 <see cref="BottomButtonsPanel.Toggle"/> 한 번에 맞춥니다.
    /// </summary>
    public void Finger_ToggleBottomPanel(Finger finger, FingerTriggerAreaEvents area)
    {
        _bottomButtonsPanel?.Toggle();
    }

    /// <summary>
    /// <see cref="FingerTriggerAreaToggleEvents"/>용 — 열기/닫기를 번갈아 호출할 때 사용합니다.
    /// (버튼 클릭과 같이 쓰면 토글 카운터가 어긋날 수 있어, 보통은 <see cref="Finger_ToggleBottomPanel"/>을 권장합니다.)
    /// </summary>
    public void Finger_SetBottomPanelOpen(Finger finger, FingerTriggerAreaEvents area)
    {
        _bottomButtonsPanel?.SetOpen(true);
    }

    public void Finger_SetBottomPanelClosed(Finger finger, FingerTriggerAreaEvents area)
    {
        _bottomButtonsPanel?.SetOpen(false);
    }

    public void Finger_InvokeExit(Finger finger, FingerTriggerAreaEvents area)
    {
        VrUiButtonFingerFeedback.InvokeWithFeedback(_exitButton);
    }
}
