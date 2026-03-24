using FishNet.Object;
using MasterServerToolkit.UI;
using UnityEngine;

namespace DoDrill
{
    // ============================================================
    //  PlayerUISetup.cs
    //  PC_Player 프리팹에 붙이기
    //  오너 클라이언트일 때 UICamera를 씬의 World Space Canvas에 연결
    // ============================================================

    public class PlayerUISetup : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _uiCamera; // PC_Player/Owners/UICamera

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!IsOwner) return;
            if (_uiCamera == null)
            {
                Debug.LogWarning("[PlayerUISetup] UICamera 연결 안 됨");
                return;
            }

            // 씬의 모든 World Space Canvas에 UICamera 연결
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    canvas.worldCamera = _uiCamera;
                    Debug.Log($"[PlayerUISetup] UICamera 연결 → {canvas.name}");
                }
            }
        }
    }
}
