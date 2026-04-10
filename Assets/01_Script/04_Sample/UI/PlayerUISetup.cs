using FishNet.Object;
using FishNet.Object.Synchronizing; // 필수
using TMPro;
using UnityEngine;

namespace DoDrill
{
    public class PlayerUISetup : NetworkBehaviour
    {
        [Header("Camera Setup")]
        [SerializeField] private Camera _uiCamera;

        [Header("Name Tag UI")]
        [SerializeField] private Canvas _nameTagCanvas;
        [SerializeField] private TextMeshProUGUI _nameText;

        // ★ [v4 수정]: [SyncVar] 속성을 제거하고 readonly SyncVar<T> 객체로 선언합니다.
        private readonly SyncVar<string> _syncNickname = new SyncVar<string>();

        public override void OnStartClient()
        {
            base.OnStartClient();

            // ★ [v4 수정]: 콜백 등록 방식이 OnChange += 로 변경되었습니다.
            _syncNickname.OnChange += OnNicknameChanged;

            if(IsOwner)
            {
                SetupUICamera();
                string myNickname = GetMyPlatformNickname();
                CmdSetNickname(myNickname);
            }

            // 초기 값 반영 (이미 값이 설정되어 있을 경우)
            UpdateNameTagUI(_syncNickname.Value);
        }

        // ★ [v4 수정]: OnStopClient에서 이벤트를 해제해주는 것이 안전합니다.
        public override void OnStopClient()
        {
            base.OnStopClient();
            _syncNickname.OnChange -= OnNicknameChanged;
        }

        [ServerRpc]
        private void CmdSetNickname(string nickname)
        {
            // ★ [v4 수정]: .Value 속성을 통해 값을 설정합니다.
            _syncNickname.Value = nickname;
        }

        // ★ [v4 수정]: 콜백 매개변수 형식이 (이전값, 새값, 서버여부)로 유지되지만 호출 방식이 바뀜
        private void OnNicknameChanged(string prev, string next, bool asServer)
        {
            UpdateNameTagUI(next);
        }

        private void UpdateNameTagUI(string nickname)
        {
            if(_nameText == null || string.IsNullOrEmpty(nickname)) return;
            _nameText.text = nickname;
        }

        private void SetupUICamera()
        {
            if(_uiCamera == null) return;
            foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if(canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    canvas.worldCamera = _uiCamera;
                }
            }
        }

        private string GetMyPlatformNickname()
        {
            string platformPrefix = "";
            switch(UserInfo.PlatformType)
            {
                case XRAirpotrSecurity.PlatformType.VR: platformPrefix = "[VR]"; break;
                case XRAirpotrSecurity.PlatformType.Mobile: platformPrefix = "[Mobile]"; break;
                case XRAirpotrSecurity.PlatformType.PC: platformPrefix = "[PC]"; break;
                default: platformPrefix = "[Unknown]"; break;
            }

            string userName = string.IsNullOrEmpty(UserInfo.UserName)
                              ? $"Guest_{OwnerId}"
                              : UserInfo.UserName;

            return $"{platformPrefix} {userName}";
        }

        private void Update()
        {
            if(_nameTagCanvas != null && Camera.main != null)
            {
                _nameTagCanvas.transform.LookAt(_nameTagCanvas.transform.position + Camera.main.transform.rotation * Vector3.forward,
                                               Camera.main.transform.rotation * Vector3.up);
            }
        }
    }
}