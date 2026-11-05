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

        /// <summary>이 플레이어의 (플랫폼 접두어 포함) 동기화된 닉네임. 아직 안 왔으면 빈 문자열</summary>
        public string Nickname => _syncNickname.Value ?? string.Empty;

        /// <summary>닉네임이 도착/변경될 때. 미니맵 라벨 등이 '로컬 유저 이름'이 아니라 각 플레이어 자신의 이름을 쓰도록 한다.</summary>
        public event System.Action<string> NicknameChanged;

        public override void OnStartClient()
        {
            base.OnStartClient();

            // ★ [v4 수정]: 콜백 등록 방식이 OnChange += 로 변경되었습니다.
            _syncNickname.OnChange += OnNicknameChanged;

            // 내 이름표는 내 화면에서 보이지 않게 한다(가까이 있어 화면을 가득 덮던 문제). 남에게는 그대로 보인다.
            if(IsOwner && _nameTagCanvas != null) _nameTagCanvas.gameObject.SetActive(false);

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
            NicknameChanged?.Invoke(next);
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

        [Header("닉네임 태그 위치")]
        [Tooltip("머리 본 위로 올릴 높이(m, 아바타 스케일 곱). 프리팹의 캔버스는 허리 높이(0.6m)에 있어 몸통 중간에 떠 보였다.")]
        [SerializeField] private float nameTagAboveHead = 0.34f;
        [Tooltip("태그 월드 크기 배율(거리와 무관). 1=프리팹 값")]
        [SerializeField] private float nameTagScale = 0.8f;

        private PcMobileAvatarController _avatar;
        private Transform _head;
        private bool _headSearched;
        private bool _isVrHead;
        private Vector3 _tagBaseScale;

        private void LateUpdate()
        {
            if(_nameTagCanvas == null) return;
            if(!_headSearched)
            {
                _headSearched = true;
                _avatar = GetComponent<PcMobileAvatarController>();
                var anim = _avatar != null ? _avatar.AvatarAnimator : null;
                if(anim != null && anim.isHuman) _head = anim.GetBoneTransform(HumanBodyBones.Head);
                // VR 원격 아바타: 머리 본 대신 머리 추적 대상(applyHead)을 쓴다 — 예전엔 이름표 높이가 플레이어마다 제각각이었다
                if(_head == null && !IsOwner)
                {
                    var vr = GetComponent<VRPlayer>();
                    if(vr != null && vr.trackingSetup != null) _head = vr.trackingSetup.applyHead;
                    if(_head != null) _isVrHead = true;
                }
                _tagBaseScale = _nameTagCanvas.transform.localScale;
                if(_head != null) _nameTagCanvas.transform.localScale = _tagBaseScale * nameTagScale;
            }
            if(_head == null) return;   // 아바타가 없으면 프리팹 위치 유지
            float s = _avatar != null ? _avatar.AvatarScale : 1f;
            // 머리 본(턱~정수리 중간)과 카메라(눈) 기준 머리 추적 대상의 높이가 달라 VR 은 조금 더 낮게 둔다
            float up = _isVrHead ? nameTagAboveHead * 0.85f : nameTagAboveHead * s;
            Vector3 p = _head.position + Vector3.up * up;
            _nameTagCanvas.transform.position = p;
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