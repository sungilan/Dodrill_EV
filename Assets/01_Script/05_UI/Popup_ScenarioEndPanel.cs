using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

namespace DoDrill
{
    // ============================================================
    //  Popup_ScenarioEndPanel.cs
    //  게임 도중 홈 버튼 눌렀을 때 "나가시겠습니까?" 팝업
    //
    //  홈 버튼 OnClick:
    //    Mst.Events.Invoke(ScenarioEventKeys.ShowExitPopup)
    //  버튼 OnClick 연결:
    //    BTN_Confirm → OnConfirmClick()
    //    BTN_Cancel  → OnCancelClick()
    // ============================================================

    public class Popup_ScenarioEndPanel : PopupView
    {
        [Header("References")]
        [SerializeField] private ScenarioExitHandler _exitHandler;

        protected override void Awake()
        {
            base.Awake();

            if (_exitHandler == null)
                _exitHandler = Object.FindFirstObjectByType<ScenarioExitHandler>();

            Mst.Events.AddListener(ScenarioEventKeys.ShowExitPopup, (_) => Show());
            Mst.Events.AddListener(ScenarioEventKeys.HideExitPopup, (_) => Hide());
        }

        public void OnConfirmClick()
        {
            Hide();

            if (_exitHandler != null)
            {
                _exitHandler.ReturnToTitle();
            }
            else
            {
                Debug.LogWarning("[Popup_ScenarioEndPanel] ScenarioExitHandler 없음");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        public void OnCancelClick()
        {
            Hide();
        }
    }
}