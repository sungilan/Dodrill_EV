
using Autohand;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // 추가
using UnityEngine;

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

    // ── 누가 눌렀는지(서버) ─────────────────────────────────────
    // 상태가 바뀌면 모든 클라이언트가 ApplyAnimState 를 실행하고 각자 시나리오 신호(ZoneActivated)를 서버로 보낸다.
    // 그래서 서버가 받는 신호는 '누른 사람'이 아니라 '접속한 사람 수만큼' 와서, 신호의 연결(conn)로는 누가 눌렀는지 알 수 없다.
    // 평가(감점)는 실제로 누른 사람에게 해야 하므로, 서버가 요청을 받은 연결을 여기에 기록해 둔다.
    private static readonly Dictionary<string, (int clientId, float time)> s_lastActor = new Dictionary<string, (int, float)>();

    internal static void RecordActor(string id, int clientId)
    {
        if(string.IsNullOrEmpty(id) || clientId < 0) return;
        s_lastActor[id] = (clientId, Time.time);
    }

    /// <summary>[서버] 이 오브젝트를 최근(within 초 안에) 누른 사람. 없으면 false.</summary>
    public static bool TryGetRecentActor(string id, float within, out int clientId)
    {
        clientId = -1;
        if(string.IsNullOrEmpty(id)) return false;
        if(!s_lastActor.TryGetValue(id, out var a)) return false;
        if(Time.time - a.time > within) return false;
        clientId = a.clientId;
        return true;
    }

    private static void OnServerReceive(NetworkConnection conn, AnimatorStateBroadcast broadcast, Channel channel)
    {
        var target = Get(broadcast.id);
        if(target == null) return;
        if(!target.CanInteract()) return;
        if(conn != null && conn.IsValid) RecordActor(broadcast.id, conn.ClientId);
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
    [Tooltip("이 오브젝트가 작동해야 하는 Task ID (moduleId) 리스트입니다.")]
    public List<string> targetTaskIds = new List<string>(); // int에서 string으로 변경

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

    private void OnEnable()
    {
        // UI와 동일하게 이벤트 구독
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
    }

    private void OnDisable()
    {
        // 이벤트 구독 해제
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
    }

    // ── 이벤트 핸들러 ───────────────────────────────────────────

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        // 태스크 단계가 변경될 때마다 내 Grabbable 상태 갱신
        UpdateGrabbableState();
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        // 처음에 전체 데이터를 받았을 때 상태 갱신
        UpdateGrabbableState();
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

    /// <summary>현재 시나리오 단계의 Module ID가 허용 리스트에 있는지 확인</summary>
    public bool CanInteract()
    {
        if(targetTaskIds == null || targetTaskIds.Count == 0) return true;

        var sr = ScenarioStateReceiver.Instance;
        ScenarioData data = null;

        // ★ TaskProgressUI와 동일한 데이터 확보 로직 (Fallback)
        if(sr != null && sr.CurrentScenario != null)
        {
            data = sr.CurrentScenario;
        }
        else
        {
            var receiver = FindFirstObjectByType<ScenarioReceiver>();
            if(receiver != null && receiver.ReceivedData != null)
                data = receiver.ReceivedData;
        }

        // 로딩 중이거나 데이터가 아예 없는 경우 조작 허용 (true)
        if(data == null) return true;

        int currentIndex = (sr != null) ? sr.CurrentTaskState.taskIndex : 0;
        var tasks = data.scenario.tasks;

        if(currentIndex < 0 || currentIndex >= tasks.Count) return false;

        string currentModuleId = tasks[currentIndex].moduleId;

        return targetTaskIds.Any(id =>
            id.Trim().Equals(currentModuleId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 시나리오 단계에 따라 Grabbable 컴포넌트를 활성화/비활성화
    /// </summary>
    /// <summary>
    /// 시나리오 단계에 따라 Grabbable 컴포넌트를 활성화/비활성화
    /// </summary>
    private void UpdateGrabbableState()
    {
        if(_grabbable == null) return;

        // CanInteract 내부에서 이미 FindFirstObjectByType 등을 통해 체크하므로
        // 결과값에 따라 enabled만 조절해줍니다.
        bool canNowInteract = CanInteract();

        if(_grabbable.enabled != canNowInteract)
        {
            _grabbable.enabled = canNowInteract;
            Debug.Log($"<color=lime>[Clickable]</color> {uniqueId} 상태 갱신 (이벤트 수신): {canNowInteract}");
        }
    }

    /// <summary>
    /// 이 클라이언트의 사용자가 직접(PC 클릭·모바일 터치) 눌러 상태를 바꿀 때 발행 — 아바타 손 뻗기용. 인자 = 눌린 지점(월드).
    /// 예전에는 클라이언트마다 시나리오 신호(FireZoneActivated)가 메아리쳐서 그 신호로 손을 뻗었는데,
    /// 신호를 서버에서만 보내도록 바꾼 뒤(정비 순서 오탐 수정) 해치·문을 열고 닫아도 손이 안 움직였다.
    /// </summary>
    public static event Action<Vector3> OnLocalClicked;

    // 누른 지점 — 이 오브젝트 콜라이더 묶음에서 카메라에 가장 가까운 점(정확한 히트 점은 호출부마다 달라 넘기지 않는다)
    private Vector3 ReachPoint()
    {
        var cam = Camera.main;
        var cols = GetComponentsInChildren<Collider>(false);
        bool any = false; Bounds b = default;
        foreach(var c in cols)
        {
            if(c == null || !c.enabled || c.isTrigger) continue;
            if(!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if(!any) return transform.position;
        return cam != null ? b.ClosestPoint(cam.transform.position) : b.center;
    }

    public void OnPCClick()
    {
        if(!CanInteract()) return;
        try { OnLocalClicked?.Invoke(ReachPoint()); } catch(Exception e) { Debug.LogException(e); }
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
            // 호스트(서버 겸 클라이언트)가 직접 누른 경우: 누른 사람은 호스트 자신
            if(InstanceFinder.IsClientStarted && InstanceFinder.ClientManager.Connection != null)
                ClickableAnimatorManager.RecordActor(uniqueId, InstanceFinder.ClientManager.Connection.ClientId);
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
        //else if(!CanInteract()) return;

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
        // 서버(호스트 포함)에서만 보낸다. 단계 완료 판정은 서버의 모듈이 이 신호로 한다.
        // (예전: 서버가 적용 → 단계 완료 → 다음 단계로 넘어간 '뒤에' 클라이언트가 같은 상태를 받아 또 신호를 보내서,
        //  서버는 그걸 '다음 단계에서 엉뚱한 곳을 누름'으로 보고 정비 순서 위반(PROC_ORDER) 감점을 매겼다)
        if(sendScenarioSignal && InstanceFinder.IsServerStarted)
        {
            InteractionEvents.FireZoneActivated(uniqueId, string.Empty);
            Debug.Log($"[Clickable] {uniqueId} 시나리오 신호 발신");
        }

        Debug.Log($"[Clickable] {uniqueId} 상호작용 발신 (상태: {(open ? "열림/연결" : "닫힘/분리")})");

    }
}