using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

namespace DoDrill
{
    public class Popup_ExitPanel : PopupView
    {
        protected override void Awake()
        {
            base.Awake();
            Rect.anchoredPosition = new Vector2(0f, 50f);
            Mst.Events.AddListener(MstEventKeys.showExitPopup, (_) => Show());
            Mst.Events.AddListener(MstEventKeys.hideExitPopup, (_) => Hide());
        }

        public void OnConfirmClick()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void OnCancelClick()
        {
            Mst.Events.Invoke(MstEventKeys.hideExitPopup);
        }
    }
}