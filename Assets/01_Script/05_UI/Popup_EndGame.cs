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

        private static readonly System.Collections.Generic.List<Popup_EndGame> s_awake = new System.Collections.Generic.List<Popup_EndGame>();

        /// <summary>
        /// 종료 팝업 띄우기. 씬에서 팝업 오브젝트가 꺼진 채로 저장되어 있으면 Awake 가 안 돌아 이벤트 구독이 없었다
        /// (시나리오가 끝나도 팝업이 안 뜨던 원인). 구독한 팝업이 없으면 꺼진 팝업을 찾아 켜서 띄운다.
        /// </summary>
        public static void RequestShow()
        {
            s_awake.RemoveAll(p => p == null);
            bool isVrPlayer = UnityEngine.XR.XRSettings.isDeviceActive;
            Debug.Log($"[Popup_EndGame] 종료 팝업 요청 (구독 {s_awake.Count}개, VR={isVrPlayer})");

            Popup_EndGame pick = null;
            foreach (var p in Object.FindObjectsByType<Popup_EndGame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool vr = false; for (var t = p.transform.parent; t != null; t = t.parent) if (t.name.Contains("VR")) { vr = true; break; } // VR_UICanvas 아래면 VR 용
                if (pick == null || vr == isVrPlayer) pick = p;
                if (vr == isVrPlayer) break;
            }
            if (pick == null) { Debug.LogWarning("[Popup_EndGame] 씬에 종료 팝업이 없음"); return; }
            for (var t = pick.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true); // Awake → 숨김 상태로 시작

            // PC·모바일: 씬의 종료 팝업은 월드 공간 캔버스(VR 용 배치)라 켜도 화면에 안 보였다 → 화면 위에 띄운다
            var canvas = pick.GetComponent<Canvas>() ?? pick.GetComponentInParent<Canvas>(true);
            if (!isVrPlayer && canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                canvas.transform.localScale = Vector3.one;
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            if (!isVrPlayer) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } // 버튼을 누를 수 있게

            Debug.Log($"[Popup_EndGame] 표시: {pick.name} (캔버스 {(canvas != null ? canvas.renderMode.ToString() : "없음")})");
            pick.Show();
        }

        protected override void Awake()
        {
            s_awake.Add(this);
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