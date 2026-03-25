using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using FishNet.Connection;
using System.Collections;
using System.Collections.Generic;

// ============================================================
//  ClickableAnimator.cs
//  씬 고정 오브젝트(문/후드/트렁크)를 전체 클라이언트에 동기화.
//  NetworkObject 없이 FishNet Broadcast 사용.
//
//  설계:
//    ClickableAnimatorManager (static) 가 Broadcast 딱 1회 등록.
//    개별 ClickableAnimator는 uniqueId로 레지스트리에 등록.
//    클릭 → Manager가 Broadcast 전송 → 서버 검증 → 전체 클라 적용.
//
//  씬 세팅:
//    1. 문 오브젝트에 ClickableAnimator 부착
//    2. uniqueId를 고유값으로 설정 (예: "Door_FrontL")
//    3. Animator Bool 파라미터 이름 = animParamName (기본 "IsOpen")
//    4. Collider(Is Trigger=false) 부착 → FreeLookController 레이캐스트 감지
//    5. FreeLookController clickLayers에 해당 레이어 추가
// ============================================================

// ── Broadcast 구조체 ──────────────────────────────────────────

public struct AnimatorStateBroadcast : IBroadcast
{
    public string id;
    public bool   isOpen;
}

// ============================================================
//  ClickableAnimatorManager
//  Broadcast를 딱 1회 등록/해제하는 static 관리자.
//  씬에 ClickableAnimator가 여러 개여도 중복 등록 없음.
// ============================================================

public static class ClickableAnimatorManager
{
    private static bool _registered = false;
    private static readonly Dictionary<string, ClickableAnimator> _registry = new();

    // ── 레지스트리 ────────────────────────────

    public static void Register(string id, ClickableAnimator anim)
    {
        _registry[id] = anim;
        TryRegisterBroadcast();
    }

    public static void Unregister(string id)
    {
        _registry.Remove(id);
        if (_registry.Count == 0)
            TryUnregisterBroadcast();
    }

    public static ClickableAnimator Get(string id)
        => _registry.TryGetValue(id, out var a) ? a : null;

    // ── Broadcast 등록 ───────────────────────

    private static void TryRegisterBroadcast()
    {
        if (_registered) return;
        if (InstanceFinder.ServerManager == null && InstanceFinder.ClientManager == null) return;

        InstanceFinder.ServerManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.RegisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
        _registered = true;
    }

    private static void TryUnregisterBroadcast()
    {
        if (!_registered) return;
        InstanceFinder.ServerManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<AnimatorStateBroadcast>(OnClientReceive);
        _registered = false;
    }

    // ── 서버 수신 → 전체 클라에 전파 ────────────

    private static void OnServerReceive(NetworkConnection conn,
                                         AnimatorStateBroadcast broadcast,
                                         Channel channel)
    {
        // 서버는 요청을 받아 전체 클라에 전파 (권위 있는 상태 결정)
        var target = Get(broadcast.id);
        if (target == null) return;

        target.ApplyAnimState(broadcast.isOpen);
        InstanceFinder.ServerManager.Broadcast(broadcast); // 전체 클라에 전파
    }

    // ── 클라이언트 수신 → 애니메이션 적용 ────────

    private static void OnClientReceive(AnimatorStateBroadcast broadcast, Channel channel)
    {
        // 호스트(서버+클라)는 OnServerReceive에서 이미 적용됨 → 스킵
        if (InstanceFinder.IsServerStarted) return;

        var target = Get(broadcast.id);
        target?.ApplyAnimState(broadcast.isOpen);
    }

    // ── 늦게 접속한 클라이언트에 현재 상태 전송 ──

    public static void SyncAllToClient(NetworkConnection conn)
    {
        if (!InstanceFinder.IsServerStarted) return;
        foreach (var kv in _registry)
        {
            InstanceFinder.ServerManager.Broadcast(conn,
                new AnimatorStateBroadcast { id = kv.Key, isOpen = kv.Value.IsOpen });
        }
    }
}

// ============================================================
//  ClickableAnimator — 개별 문/후드 오브젝트에 부착
// ============================================================

[RequireComponent(typeof(Animator))]
public class ClickableAnimator : MonoBehaviour
{
    [Header("식별")]
    [Tooltip("씬 내 고유 ID. 다른 오브젝트와 겹치면 안 됨.")]
    public string uniqueId = "Door_Default";

    [Header("Animator 파라미터")]
    [Tooltip("Bool 파라미터 이름 (useTrigger = false 일 때)")]
    public string animParamName = "IsOpen";

    [Tooltip("true = Trigger 방식 / false = Bool 방식")]
    public bool useTrigger = false;

    [Tooltip("열기 Trigger 이름 (useTrigger = true)")]
    public string openTrigger = "Open";

    [Tooltip("닫기 Trigger 이름 (useTrigger = true)")]
    public string closeTrigger = "Close";

    [Header("설정")]
    [Tooltip("열린 후 자동 닫기까지 대기 시간 (0 = 없음)")]
    public float autoCloseDelay = 0f;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private bool _isOpen = false;

    private Animator  _animator;
    private Coroutine _autoCloseCoroutine;

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Start()
    {
        // FishNet이 Start 이후 초기화되므로 여기서 등록
        ClickableAnimatorManager.Register(uniqueId, this);
    }

    private void OnDestroy()
    {
        ClickableAnimatorManager.Unregister(uniqueId);
        if (_autoCloseCoroutine != null)
            StopCoroutine(_autoCloseCoroutine);
    }

    // ═══════════════════════════════════════════
    //  공개 API
    // ═══════════════════════════════════════════

    /// <summary>FreeLookController 클릭 시 호출</summary>
    public void OnPCClick() => RequestSetState(!_isOpen);

    public void Toggle()        => RequestSetState(!_isOpen);
    public void Open()          => RequestSetState(true);
    public void Close()         => RequestSetState(false);
    public bool IsOpen          => _isOpen;

    // ═══════════════════════════════════════════
    //  요청 전송
    // ═══════════════════════════════════════════

    private void RequestSetState(bool open)
    {
        var broadcast = new AnimatorStateBroadcast { id = uniqueId, isOpen = open };

        if (InstanceFinder.IsServerStarted)
        {
            // 호스트: 서버에서 직접 처리 + 전파
            ApplyAnimState(open);
            InstanceFinder.ServerManager.Broadcast(broadcast);

            if (open && autoCloseDelay > 0f)
            {
                if (_autoCloseCoroutine != null) StopCoroutine(_autoCloseCoroutine);
                _autoCloseCoroutine = StartCoroutine(AutoCloseRoutine());
            }
        }
        else if (InstanceFinder.IsClientStarted)
        {
            // 일반 클라이언트: 서버에 요청
            InstanceFinder.ClientManager.Broadcast(broadcast);
        }
    }

    // ═══════════════════════════════════════════
    //  애니메이션 적용 (Manager에서 호출)
    // ═══════════════════════════════════════════

    public void ApplyAnimState(bool open)
    {
        _isOpen = open;
        if (_animator == null) return;

        if (useTrigger)
            _animator.SetTrigger(open ? openTrigger : closeTrigger);
        else
            _animator.SetBool(animParamName, open);

        Debug.Log($"[ClickableAnimator] {uniqueId} → {(open ? "열림" : "닫힘")}");
    }

    private IEnumerator AutoCloseRoutine()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        RequestSetState(false);
    }
}
