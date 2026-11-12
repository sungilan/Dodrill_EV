using DG.Tweening;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;
using XRAirpotrSecurity;

public class MiniMapEntity
{
    public bool showDetails = false;
    public Sprite icon;
    public bool rotateWithObject = true;
    public Vector3 upAxis;
    public float rotation;
    public Vector2 size;
    public bool clampInBorder;
    public float clampDist;
    public List<GameObject> mapObjects;
    public string objectName;
}

public class MiniMapComponent : MonoBehaviour
{
    [Header("아이콘 설정")]
    public Sprite icon;
    public Vector2 size = new Vector2(20, 20);
    public bool rotateWithObject = false;
    public Vector3 upAxis = new Vector3(0, 1, 0);
    public float initialIconRotation;

    [Header("로컬라이징 및 표시 설정")]
    public LocalizedString localizedName;
    public bool showNameOnMap = true;

    [Header("하이라이트 설정 (UI Outline)")]
    public float blinkDuration = 0.6f;
    public Color outlineColor = new Color32(220, 20, 60, 255);
    public Vector2 outlineDistance = new Vector2(2f, -2f);

    private MiniMapController miniMapController;
    private MiniMapEntity mme;
    private MapObject mmo;
    private bool _isRegistered = false;
    private bool _blinkReserved = false;
    private int _blinkRetryCount = 0;
    private Tween _blinkTween;
    private Outline _uiOutline;

    private int _myClientId = -1;
    private bool _isPlayer = false;
    private DoDrill.PlayerUISetup _nickSource;

    // 플레이어 여부를 판단하는 프로퍼티
    private bool IsPlayerCheck => _isPlayer || gameObject.CompareTag("Player") || gameObject.name.Contains("Player");

    private void Awake()
    {
        _isPlayer = gameObject.CompareTag("Player") || gameObject.name.Contains("Player");

        var nob = GetComponentInParent<NetworkObject>();
        if(nob != null) _myClientId = nob.OwnerId;
    }

    private void OnEnable()
    {
        miniMapController = Object.FindFirstObjectByType<MiniMapController>();

        if(IsPlayerCheck)
        {
            // [핵심] 리스트가 갱신될 때마다 이름을 다시 찾아서 업데이트하도록 이벤트 구독
            NetworkedSessionUserList.OnPlayersBroadcastReceived += HandlePlayersBroadcast;

            // 각 플레이어 프리팹의 동기화 닉네임을 구독한다 (예전에는 모든 플레이어가 '내 로컬 이름'으로 표시돼 PC/모바일을 구분할 수 없었다)
            _nickSource = GetComponentInParent<DoDrill.PlayerUISetup>();
            if(_nickSource != null)
            {
                _nickSource.NicknameChanged += UpdateMapName;
                if(_isRegistered && !string.IsNullOrEmpty(_nickSource.Nickname)) UpdateMapName(_nickSource.Nickname);
            }

            // 이미 등록된 상태에서 다시 활성화되었다면 세션 정보로 이름 갱신 시도
            //if(_isRegistered) UpdateMapNameWithSession();
        }
    }

    private void OnDisable()
    {
        if(IsPlayerCheck)
        {
            NetworkedSessionUserList.OnPlayersBroadcastReceived -= HandlePlayersBroadcast;
            if(_nickSource != null) { _nickSource.NicknameChanged -= UpdateMapName; _nickSource = null; }
        }
        StopBlinking();
    }

    private IEnumerator Start()
    {
        if(miniMapController == null) yield break;
        RegisterToMap();
    }

    /// <summary>
    /// 세션 리스트 브로드캐스트 수신 시 호출
    /// </summary>
    private void HandlePlayersBroadcast(SessionPlayersListBroadcast msg)
    {
       // UpdateMapNameWithSession();
    }

    /// <summary>
    /// 현재 세션 리스트에서 내 ClientId에 맞는 이름을 찾아 업데이트
    /// </summary>
    //private void UpdateMapNameWithSession()
    //{
    //    var userList = Object.FindFirstObjectByType<NetworkedSessionUserList>();
    //    if(userList != null)
    //    {
    //        string foundName = userList.GetDisplayName(_myClientId);
    //        if(!string.IsNullOrEmpty(foundName))
    //        {
    //            UpdateMapName(foundName);
    //        }
    //    }
    //}

    public void UpdateMapName(string newName)
    {
        if(!_isRegistered || mmo == null) return;

        if(mmo.labelText != null)
            mmo.labelText.text = newName;

        if(mme != null)
            mme.objectName = newName;

        Debug.Log($"<color=lime>[MiniMap-Name]</color> <b>{gameObject.name}</b> 이름 업데이트: {newName}");
    }

    private void RegisterToMap()
    {
        if(_isRegistered) return;

        string finalName = "";

        if(IsPlayerCheck)
        {
            // 1. 등록 시점에는 일단 폴백 이름(로컬 유저네임 혹은 기본값)으로 설정
            //    (소유자가 아닌 복사본에 내 UserName 을 붙이던 것이 'PC/모바일 구분 안 됨'의 한 원인)
            var nick = _nickSource != null ? _nickSource.Nickname : null;
            if(!string.IsNullOrEmpty(nick)) finalName = nick;
            else if(_nickSource != null) finalName = $"Player {_myClientId}"; // 닉네임 도착 전 임시 — NicknameChanged 로 곧 교체됨
            else finalName = !string.IsNullOrEmpty(UserInfo.UserName) ? UserInfo.UserName : $"Player {_myClientId}";

            // 2. 만약 이미 세션 리스트가 도착해 있다면 즉시 이름 교체 시도
            var userList = Object.FindFirstObjectByType<NetworkedSessionUserList>();
            if(userList != null)
            {
                //string sessionName = userList.GetDisplayName(_myClientId);
                //if(!string.IsNullOrEmpty(sessionName)) finalName = sessionName;
            }
        }
        else if(showNameOnMap && localizedName != null)
        {
            finalName = localizedName.GetLocalized();
        }
        else
        {
            finalName = gameObject.name;
        }

        mme = new MiniMapEntity
        {
            icon = icon,
            rotation = initialIconRotation,
            size = size,
            upAxis = upAxis,
            rotateWithObject = rotateWithObject,
            clampInBorder = true,
            clampDist = 100,
            objectName = finalName
        };

        mmo = miniMapController.RegisterMapObject(this.gameObject, mme);

        if(mmo != null)
        {
            _isRegistered = true;
            bool isPlayerOrReserved = IsPlayerCheck || _blinkReserved;
            SetIconActive(isPlayerOrReserved, isPlayerOrReserved ? "Player/Reserved" : "Initial Registration");
            if(_blinkReserved) ExecuteBlink();
        }
    }

    public void SetIconActive(bool active, string reason)
    {
        if(mmo != null && mmo.spr != null)
        {
            if(IsPlayerCheck && !active)
            {
                mmo.spr.gameObject.SetActive(true);
                if(mmo.labelText != null) mmo.labelText.gameObject.SetActive(showNameOnMap);
                return;
            }

            bool before = mmo.spr.gameObject.activeSelf;
            mmo.spr.gameObject.SetActive(active);
            if(mmo.labelText != null)
                mmo.labelText.gameObject.SetActive(active && showNameOnMap);

            // 같은 상태로 다시 설정될 때(수십 개 오브젝트가 한꺼번에) 로그가 쏟아지던 것 → 실제로 바뀔 때만 남긴다
            if(before != mmo.spr.gameObject.activeSelf)
                Debug.Log($"<color=cyan>[MiniMap-Visibility]</color> <b>{gameObject.name}</b> 가시성: {mmo.spr.gameObject.activeSelf} ({reason})");
        }
    }

    public void StartBlinking()
    {
        if(!_isRegistered || mmo == null)
        {
            _blinkReserved = true;
            return;
        }
        SetIconActive(true, "StartBlinking Execution");
        ExecuteBlink();
    }

    private void ExecuteBlink()
    {
        if(mmo == null || mmo.spr == null)
        {
            _blinkRetryCount++;
            if(_blinkRetryCount > 20) return;
            Invoke(nameof(ExecuteBlink), 0.1f);
            return;
        }

        _blinkRetryCount = 0;
        _blinkReserved = false;
        _blinkTween?.Kill();

        if(_uiOutline == null) _uiOutline = mmo.spr.gameObject.GetComponent<Outline>() ?? mmo.spr.gameObject.AddComponent<Outline>();
        _uiOutline.effectColor = outlineColor;
        _uiOutline.effectDistance = outlineDistance;
        _uiOutline.enabled = true;

        _blinkTween = mmo.spr.DOFade(0.2f, blinkDuration).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
    }

    public void StopBlinking()
    {
        _blinkReserved = false;
        CancelInvoke(nameof(ExecuteBlink));
        _blinkTween?.Kill();

        if(mmo != null && mmo.spr != null)
        {
            mmo.spr.DOKill();
            Color c = mmo.spr.color; c.a = 1f; mmo.spr.color = c;
            if(_uiOutline != null) _uiOutline.enabled = false;
        }

        if(IsPlayerCheck) SetIconActive(true, "StopBlinking (Player Stay On)");
        else SetIconActive(false, "StopBlinking Called");
    }

    private void OnDestroy()
    {
        if(_isRegistered && miniMapController != null)
            miniMapController.UnregisterMapObject(mmo, gameObject);
    }
}