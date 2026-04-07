using MasterServerToolkit.MasterServer;
using MasterServerToolkit.UI;
using UnityEngine;

namespace DoDrill
{
    // ============================================================
    //  Popup_EndGame.cs
    //  시나리오 완료 안내 팝업
    //
    //  Popup_EndGame 오브젝트에 이 컴포넌트 붙이기
    //  버튼 OnClick 연결:
    //    BTN_Confirm → OnConfirmClick()
    //    BTN_Cancel  → OnCancelClick()
    // ============================================================

    public class Popup_EndGame : PopupView
    {
        [Header("References")]
        [SerializeField] private ScenarioExitHandler _exitHandler;

        protected override void Awake()
        {
            base.Awake();
            Rect.anchoredPosition = new Vector2(0f, 50f);

            if (_exitHandler == null)
                _exitHandler = Object.FindFirstObjectByType<ScenarioExitHandler>();

            Mst.Events.AddListener(ScenarioEventKeys.ShowEndPopup, (_) => Show());
            Mst.Events.AddListener(ScenarioEventKeys.HideEndPopup, (_) => Hide());
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
                Debug.LogWarning("[Popup_EndGame] ScenarioExitHandler 없음");
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