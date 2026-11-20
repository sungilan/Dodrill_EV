using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

public class LanguageSelectionPanel : PopupView
{
    [SerializeField] private UIView startPanel;

    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showLanguageSelection, (_) => Show());
        Mst.Events.AddListener(MstEventKeys.hideLanguageSelection, (_) => Hide());
        Mst.Events.AddListener(MstEventKeys.toggleLanguageSelection, (_) => OnToggle());
        Hide(true);  // isVisible을 false로 만들고
        Show();      // 그 다음 Show 호출
    }
    private void OnToggle()
    {
        if (startPanel != null && startPanel.IsVisible)
            Toggle();
    }

    public void OnStartButtonClick()
    {
        Mst.Events.Invoke(MstEventKeys.showStartPanel);
        Hide();
    }
}