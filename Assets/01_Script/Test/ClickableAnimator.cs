//using UnityEngine;
//using FishNet;
//using FishNet.Broadcast;
//using FishNet.Transporting;
//using FishNet.Connection;
//using System.Collections;
//using System.Collections.Generic;

//// ============================================================
////  ClickableAnimator.cs
////  씬 고정 오브젝트(문/후드/트렁크)를 전체 클라이언트에 동기화.
////  NetworkObject 없이 FishNet Broadcast 사용.
////
////  설계:
////    ClickableAnimatorManager (static) 가 Broadcast 딱 1회 등록.
////    개별 ClickableAnimator는 uniqueId로 레지스트리에 등록.
////    클릭 → Manager가 Broadcast 전송 → 서버 검증 → 전체 클라 적용.
////
////  씬 세팅:
////    1. 문 오브젝트에 ClickableAnimator 부착
////    2. uniqueId를 고유값으로 설정 (예: "Door_FrontL")
////    3. Animator Bool 파라미터 이름 = animParamName (기본 "IsOpen")
////    4. Collider(Is Trigger=false) 부착 → FreeLookController 레이캐스트 감지
////    5. FreeLookController clickLayers에 해당 레이어 추가
//// ============================================================

//// ── Broadcast 구조체 ──────────────────────────────────────────

//public struct AnimatorStateBroadcast : IBroadcast
//{
//    public string id;
//    public bool   isOpen;
//}

//// ============================================================
////  ClickableAnimatorManager
////  Broadcast를 딱 1회 등록/해제하는 static 관리자.
////  씬에 ClickableAnimator가 여러 개여도 중복 등록 없음.
//// ============================================================

//public static class ClickableAnimatorManager
//{
//    private static bool _registered = false;
//    private static readonly Dictionary<string, ClickableAnimator> _registry = new();

//    // ── 레지스트리 ────────────────────────────

//    public static void Register(string id, ClickableAnimator anim)
//    {
//        _registry[id] = anim;
//        TryRegisterBroadcast();
//    }

//    public static void Unregister(string id)
//    {
//        _registry.Remove(id);
//        if (_registry.Count == 0)
//            TryUnregisterBroadcast();
//    }

//    public static ClickableAnimator Get(string id)
//        => _registry.TryGetValue(id, out var a) ? a : null;

//    // ── Broadcast 등록 ───────────────────────

//    private static void TryRegisterBroadcast()
//    {
//        if (_registered) return;
//        if (InstanceFinder.ServerManager == null && InstanceFinder.ClientManager == null) return;

//        InstanceFinder.ServerManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
//        InstanceFinder.ClientManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
//        _registered = true;
//    }

//    private static void TryUnregisterBroadcast()
//    {
//        if (!_registered) return;
//        InstanceFinder.ServerManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
//        InstanceFinder.ClientManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
//        _registered = false;
//    }

//    // ── 서버 수신 → 전체 클라에 전파 ────────────

//    private static void OnServerReceive(NetworkConnection conn,
//                                         AnimatorStateBroadcast broadcast,
//                                         Channel channel)
//    {
//        // 서버는 요청을 받아 전체 클라에 전파 (권위 있는 상태 결정)
//        var target = Get(broadcast.id);
//        if (target == null) return;

//        target.ApplyAnimState(broadcast.isOpen);
//        InstanceFinder.ServerManager.Broadcast(broadcast); // 전체 클라에 전파
//    }

//    // ── 클라이언트 수신 → 애니메이션 적용 ────────

//    private static void OnClientReceive(AnimatorStateBroadcast broadcast, Channel channel)
//    {
//        // 호스트(서버+클라)는 OnServerReceive에서 이미 적용됨 → 스킵
//        if (InstanceFinder.IsServerStarted) return;

//        var target = Get(broadcast.id);
//        target?.ApplyAnimState(broadcast.isOpen);
//    }

//    // ── 늦게 접속한 클라이언트에 현재 상태 전송 ──

//    public static void SyncAllToClient(NetworkConnection conn)
//    {
//        if (!InstanceFinder.IsServerStarted) return;
//        foreach (var kv in _registry)
//        {
//            InstanceFinder.ServerManager.Broadcast(conn,
//                new AnimatorStateBroadcast { id = kv.Key, isOpen = kv.Value.IsOpen });
//        }
//    }
//}

//// ============================================================
////  ClickableAnimator — 개별 문/후드 오브젝트에 부착
//// ============================================================

//[RequireComponent(typeof(Animator))]
//public class ClickableAnimator : MonoBehaviour
//{
//    [Header("식별")]
//    [Tooltip("씬 내 고유 ID. 다른 오브젝트와 겹치면 안 됨.")]
//    public string uniqueId = "Door_Default";

//    [Header("Animator 파라미터")]
//    [Tooltip("Bool 파라미터 이름 (useTrigger = false 일 때)")]
//    public string animParamName = "IsOpen";

//    [Tooltip("true = Trigger 방식 / false = Bool 방식")]
//    public bool useTrigger = false;

//    [Tooltip("열기 Trigger 이름 (useTrigger = true)")]
//    public string openTrigger = "Open";

//    [Tooltip("닫기 Trigger 이름 (useTrigger = true)")]
//    public string closeTrigger = "Close";

//    [Header("설정")]
//    [Tooltip("열린 후 자동 닫기까지 대기 시간 (0 = 없음)")]
//    public float autoCloseDelay = 0f;

//    [Header("상태 (읽기 전용)")]
//    [SerializeField] private bool _isOpen = false;

//    [Header("사운드 설정")]
//    [Tooltip("열릴 때 재생할 사운드 파일명 (Resources/Sounds/ 기준)")]
//    public string openSound = "Door_Open";

//    [Tooltip("닫힐 때 재생할 사운드 파일명")]
//    public string closeSound = "Door_Close";

//    [Tooltip("사운드 볼륨 (0~1)")]
//    [Range(0f, 1f)]
//    public float soundVolume = 1.0f;

//    [Header("시나리오 연동")]
//    [Tooltip("클릭 시 완료 신호를 보낼 Task ID (모듈 ID). 비어있으면 무시.")]
//    public string taskId = "";

//    [Tooltip("클릭 시 완료 신호를 보낼 Zone ID. (ZoneAndUse 모듈 대응용)")]
//    public string zoneId = "";

//    private Animator  _animator;
//    private Coroutine _autoCloseCoroutine;

//    // ═══════════════════════════════════════════
//    //  생명주기
//    // ═══════════════════════════════════════════

//    private void Awake()
//    {
//        _animator = GetComponent<Animator>();
//    }

//    private void Start()
//    {
//        // FishNet이 Start 이후 초기화되므로 여기서 등록
//        ClickableAnimatorManager.Register(uniqueId, this);
//    }

//    private void OnDestroy()
//    {
//        ClickableAnimatorManager.Unregister(uniqueId);
//        if (_autoCloseCoroutine != null)
//            StopCoroutine(_autoCloseCoroutine);
//    }

//    // ═══════════════════════════════════════════
//    //  공개 API
//    // ═══════════════════════════════════════════

//    /// <summary>FreeLookController 클릭 시 호출</summary>
//    public void OnPCClick() => RequestSetState(!_isOpen);

//    public void Toggle()        => RequestSetState(!_isOpen);
//    public void Open()          => RequestSetState(true);
//    public void Close()         => RequestSetState(false);
//    public bool IsOpen          => _isOpen;

//    // ═══════════════════════════════════════════
//    //  요청 전송
//    // ═══════════════════════════════════════════

//    private void RequestSetState(bool open)
//    {
//        var broadcast = new AnimatorStateBroadcast { id = uniqueId, isOpen = open };

//        if (InstanceFinder.IsServerStarted)
//        {
//            // 호스트: 서버에서 직접 처리 + 전파
//            ApplyAnimState(open);
//            InstanceFinder.ServerManager.Broadcast(broadcast);

//            if (open && autoCloseDelay > 0f)
//            {
//                if (_autoCloseCoroutine != null) StopCoroutine(_autoCloseCoroutine);
//                _autoCloseCoroutine = StartCoroutine(AutoCloseRoutine());
//            }
//        }
//        else if (InstanceFinder.IsClientStarted)
//        {
//            // 일반 클라이언트: 서버에 요청
//            InstanceFinder.ClientManager.Broadcast(broadcast);
//        }
//    }

//    // ═══════════════════════════════════════════
//    //  애니메이션 적용 (Manager에서 호출)
//    // ═══════════════════════════════════════════

//    public void ApplyAnimState(bool open)
//    {
//        _isOpen = open;
//        if (_animator == null) return;

//        // 1. 애니메이션 적용
//        if (useTrigger)
//            _animator.SetTrigger(open ? openTrigger : closeTrigger);
//        else
//            _animator.SetBool(animParamName, open);

//        // 2. ★ 사운드 재생 (추가된 부분)
//        string soundToPlay = open ? openSound : closeSound;

//        if (!string.IsNullOrEmpty(soundToPlay))
//        {
//            // Managers.Sound 시스템을 사용하여 효과음 재생
//            Managers.Sound.Play(soundToPlay, Define.Sound.Effect, 1.0f, soundVolume);
//        }

//        // ── ★ [추가] 시나리오 단계 완료 신호 발신 ──
//        // 이 함수는 모든 클라이언트에서 실행되므로, '이벤트'를 통해 로컬에서만 반응하거나
//        // 혹은 소유권과 상관없이 클릭한 사람(혹은 서버)이 신호를 보내게 합니다.

//        // 1순위: 특정 Task ID가 지정된 경우 (태스크 직접 완료)
//        if (!string.IsNullOrEmpty(taskId))
//        {
//            // ScenarioStateReceiver 등을 통해 서버에 완료 신호 전송
//            // (프로젝트 구조에 따라 InteractionEvents를 사용해도 좋습니다)
//            InteractionEvents.FireTaskConfirmed(taskId);
//        }

//        // 2순위: Zone ID가 지정된 경우 (특정 위치 상호작용 완료)
//        if (!string.IsNullOrEmpty(zoneId))
//        {
//            InteractionEvents.FireZoneActivated(zoneId, string.Empty);
//        }

//        Debug.Log($"[ClickableAnimator] {uniqueId} → {(open ? "열림" : "닫힘")} 사운드: {soundToPlay}");
//    }

//    private IEnumerator AutoCloseRoutine()
//    {
//        yield return new WaitForSeconds(autoCloseDelay);
//        RequestSetState(false);
//    }
//}

using Autohand;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq; // 추가

// ============================================================
//  ClickableAnimator.cs
//  씬 고정 오브젝트(커넥터/문/후드 등)를 전체 클라이언트에 동기화하고
//  시나리오 시스템(ConfirmTaskModule)과 연동하여 단계를 완료시킵니다.
// ============================================================

public struct AnimatorStateBroadcast : IBroadcast
{
    public string id;
    public bool isOpen;
}

public static class ClickableAnimatorManager
{
    private static bool _registered = false;
    private static readonly Dictionary<string, ClickableAnimator> _registry = new();

    public static void Register(string id, ClickableAnimator anim)
    {
        _registry[id] = anim;
        TryRegisterBroadcast();
    }

    public static void Unregister(string id)
    {
        _registry.Remove(id);
        if(_registry.Count == 0)
            TryUnregisterBroadcast();
    }

    public static ClickableAnimator Get(string id)
        => _registry.TryGetValue(id, out var a) ? a : null;

    private static void TryRegisterBroadcast()
    {
        if(_registered) return;
        if(InstanceFinder.ServerManager == null && InstanceFinder.ClientManager == null) return;

        InstanceFinder.ServerManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
        _registered = true;
    }

    private static void TryUnregisterBroadcast()
    {
        if(!_registered) return;
        InstanceFinder.ServerManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
        _registered = false;
    }

    private static void OnServerReceive(NetworkConnection conn, AnimatorStateBroadcast broadcast, Channel channel)
    {
        var target = Get(broadcast.id);
        if(target == null) return;

        target.ApplyAnimState(broadcast.isOpen);
        InstanceFinder.ServerManager.Broadcast(broadcast);
    }

    private static void OnClientReceive(AnimatorStateBroadcast broadcast, Channel channel)
    {
        if(InstanceFinder.IsServerStarted) return;
        var target = Get(broadcast.id);
        target?.ApplyAnimState(broadcast.isOpen);
    }
}

//[RequireComponent(typeof(Animator))]

public class ClickableAnimator : MonoBehaviour
{
    [Header("식별")]
    public string uniqueId = "Obj_Default";

    [Header("시나리오 연동 설정")]
    [Tooltip("이 오브젝트가 작동해야 하는 Task Index 리스트 (0부터 시작)")]
    public List<int> targetTaskIndices = new List<int>(); // string에서 int로 변경

    [Tooltip("체크 시 클릭하자마자 시나리오 단계 완료 신호를 보냅니다.")]
    public bool sendScenarioSignal = true;

    [Header("Animator 파라미터")]
    public string animParamName = "IsOpen";
    public bool useTrigger = false;
    public string openTrigger = "Open";
    public string closeTrigger = "Close";

    [Header("참조")]
    [SerializeField] private Animator _animator;
    [SerializeField] private Grabbable _grabbable;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private bool _isOpen = false;

    [Header("사운드 및 연출")]
    public float autoCloseDelay = 0f;
    public string openSound = "Door_Open";
    public string closeSound = "Door_Close";
    [Range(0f, 1f)] public float soundVolume = 1.0f;

    private Coroutine _autoCloseCoroutine;
    private string _currentModuleId = "";
    public bool IsOpen => _isOpen;
    public void Open() => RequestSetState(true);
    public void Close() => RequestSetState(false);
    public void Toggle() => RequestSetState(!_isOpen);

    private void Awake()
    {
        if(_animator == null) _animator = GetComponent<Animator>() ?? GetComponentInParent<Animator>();
        if(_grabbable == null) _grabbable = GetComponent<Grabbable>();
    }

    private void Start()
    {
        ClickableAnimatorManager.Register(uniqueId, this);
        if(_grabbable != null) _grabbable.onGrab.AddListener(OnVRGrab);
    }

    private void OnDestroy()
    {
        ClickableAnimatorManager.Unregister(uniqueId);
        if(_grabbable != null) _grabbable.onGrab.RemoveListener(OnVRGrab);
    }

    /// <summary>현재 시나리오 단계가 리스트에 포함되어 있는지 확인</summary>
    private bool CanInteract()
    {
        // 1. 리스트가 비어있으면 상시 허용
        if(targetTaskIndices == null || targetTaskIndices.Count == 0) return true;

        var sr = ScenarioStateReceiver.Instance;
        if(sr == null) return false;

        // 2. 서버에서 받은 현재 인덱스 확인
        int currentIdx = sr.CurrentTaskState.taskIndex;

        // 3. 현재 인덱스가 허용 리스트에 있는지 확인
        bool isAllowed = targetTaskIndices.Contains(currentIdx);

        if(!isAllowed)
        {
            Debug.Log($"<color=orange>[Clickable]</color> {uniqueId} 차단: 현재 Index({currentIdx})는 허용되지 않음.");
        }

        return isAllowed;
    }

    public void OnPCClick()
    {
        if(!CanInteract()) return;
        RequestSetState(!_isOpen);
    }

    private void OnVRGrab(Hand hand, Grabbable grabbable)
    {
        if(!CanInteract())
        {
            StartCoroutine(SafeForceRelease(hand));
            return;
        }
        StartCoroutine(ReleaseAndActivate(hand));
    }

    /// <summary>
    /// 상호작용이 차단되었을 때 핸드를 안전하게 풀어주는 루틴
    /// </summary>
    private IEnumerator SafeForceRelease(Hand hand)
    {
        // AutoHand의 Grab 로직이 이번 프레임에서 완전히 끝날 때까지 대기
        yield return new WaitForFixedUpdate();

        if(hand != null)
        {
            hand.ForceReleaseGrab();
            Debug.Log($"<color=red>[Clickable]</color> {uniqueId} 상호작용 차단으로 인한 강제 해제");
        }
    }

    private IEnumerator ReleaseAndActivate(Hand hand)
    {
        yield return null;
        hand.ForceReleaseGrab();
        RequestSetState(!_isOpen);
    }

    public void RequestSetState(bool open)
    {
        var broadcast = new AnimatorStateBroadcast { id = uniqueId, isOpen = open };

        if(InstanceFinder.IsServerStarted)
        {
            ApplyAnimState(open);
            InstanceFinder.ServerManager.Broadcast(broadcast);
        }
        else if(InstanceFinder.IsClientStarted)
        {
            InstanceFinder.ClientManager.Broadcast(broadcast);
        }
    }

    public void ApplyAnimState(bool open)
    {
        // [중요] 애니메이션 적용 전에 한 번 더 CanInteract 체크 (네트워크 수신 시점 보안)
        // 단, 서버에서 브로드캐스트된 것을 클라이언트가 실행할 때는 통과해야 함
        if(InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted)
        {
            // 클라이언트 수신 시에는 강제 적용 (CanInteract 체크 스킵)
        }
        else if(!CanInteract()) return;

        _isOpen = open;
        if(_animator != null)
        {
            if(useTrigger) _animator.SetTrigger(open ? openTrigger : closeTrigger);
            else _animator.SetBool(animParamName, open);
        }

        //string sound = open ? openSound : closeSound;
        //if(!string.IsNullOrEmpty(sound)) Managers.Sound.Play(sound, Define.Sound.Effect, 1.0f, soundVolume);

        // ── 사운드 재생 디버깅 ──
        string sound = open ? openSound : closeSound;

        Debug.Log($"<color=cyan>[Clickable-Sound]</color> {uniqueId} 사운드 시도: {sound} (Volume: {soundVolume})");

        if(!string.IsNullOrEmpty(sound))
        {
            // Managers.Sound.Play가 실행되는지 확인
            try
            {
                Managers.Sound.Play(sound, Define.Sound.Effect, 1.0f, soundVolume);
                Debug.Log($"<color=lime>[Clickable-Sound]</color> {uniqueId} Managers.Sound.Play 호출 완료");
            }
            catch(Exception e)
            {
                Debug.LogError($"<color=red>[Clickable-Sound]</color> {uniqueId} 사운드 재생 중 에러 발생: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[Clickable-Sound]</color> {uniqueId} 재생할 사운드 파일명이 비어있습니다.");
        }

        // 3. ★ 시나리오 시스템 연동 (핵심) ★
        // "나(uniqueId) 클릭됐어!"라고 신호를 보냅니다.
        // ConfirmTaskModule이 이 uniqueId를 보고 자신의 targetObjName과 일치하면 단계를 완료합니다.
        if(sendScenarioSignal)
        {
            InteractionEvents.FireZoneActivated(uniqueId, string.Empty);
            Debug.Log($"[Clickable] {uniqueId} 시나리오 신호 발신");
        }

        Debug.Log($"[Clickable] {uniqueId} 상호작용 발신 (상태: {(open ? "열림/연결" : "닫힘/분리")})");

    }
}