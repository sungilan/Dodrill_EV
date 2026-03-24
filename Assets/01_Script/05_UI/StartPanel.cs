using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

public class StartPanel : UIView
{
    [SerializeField] private GameObject clientManager; // S_ClientManager

    protected override void Awake()
    {
        base.Awake();
        Mst.Events.AddListener(MstEventKeys.showStartPanel, (_) => Show());
        Mst.Events.AddListener(MstEventKeys.hideStartPanel, (_) => Hide());
    }

    // StartButton OnClick에 연결
    public void OnStartButtonClick()
    {
        clientManager.SetActive(true);
        Mst.Events.Invoke(MstEventKeys.hideLanguageToggle);
        Mst.Events.Invoke(MstEventKeys.hideLanguageSelection);
        Mst.Events.Invoke(MstEventKeys.hideStartPanel);
        Mst.Events.Invoke(MstEventKeys.showSignInView);
    }

    // QuitButton OnClick에 연결
    public void OnQuitButtonClick()
    {
        Mst.Events.Invoke(MstEventKeys.showExitPopup);

    }
}