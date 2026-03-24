using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

namespace DoDrill
{
    public class Popup_LogOutPanel : PopupView
    {
        [Header("References")]
        [SerializeField] private UIView view_ContentsListView;
        [SerializeField] private LobbyListPanel lobbyListPanel;
        [SerializeField] private LoginPanel panel_LoginPanel;
        [SerializeField] private GameObject panel_StartPanel;

        protected override void Awake()
        {
            base.Awake();
            Mst.Events.AddListener(MstEventKeys.showLogOutPopup, (_) => Show());
            Mst.Events.AddListener(MstEventKeys.hideLogOutPopup, (_) => Hide());
        }

        public void OnConfirmClick()
        {
            panel_LoginPanel?.SignOut();
            view_ContentsListView?.Hide();
            lobbyListPanel?.Hide();            
            panel_StartPanel?.SetActive(false);
            Mst.Events.Invoke(MstEventKeys.hideLogOutPopup);
        }

        public void OnCancelClick()
        {
            Mst.Events.Invoke(MstEventKeys.hideLogOutPopup);
        }
    }
}