using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR 손가락(FingerTrigger)으로 UI를 누를 때, <see cref="ClickButton"/>·<see cref="ToggleButton"/> 연출과
/// <see cref="Button.onClick"/> 로직을 한 번에 맞춥니다. 일반 레이캐스트 클릭과 동일한 순서를 목표로 합니다.
/// </summary>
public static class VrUiButtonFingerFeedback
{
    public static void InvokeWithFeedback(Button button)
    {
        if (button == null)
            return;

        var go = button.gameObject;
        var click = go.GetComponent<ClickButton>();
        if (click != null)
        {
            click.TriggerAsIfClicked();
            return;
        }

        var toggle = go.GetComponent<ToggleButton>();
        if (toggle != null && !toggle.UsesExternalVisualOnly)
            toggle.FingerTapToggleVisual();

        button.onClick.Invoke();
    }
}
