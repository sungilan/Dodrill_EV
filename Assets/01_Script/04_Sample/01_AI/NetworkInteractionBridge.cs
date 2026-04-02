using DG.Tweening;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoDrill.Training
{
    // ============================================================
    //  NetworkInteractionBridge.cs
    //  클라이언트 전용 — 존 인터랙션 ↔ 서버 신호 변환 + DOTween 스냅 애니메이션
    //
    //  역할:
    //    1. InteractionEvents.OnZoneActivated (클라이언트 물리 트리거)
    //       → ZoneInteractionSignal 서버 전송 (검증은 서버가 수행)
    //    2. PlaySnapAnimBroadcast 수신 (서버가 검증 완료 후 지시)
    //       → DOTween으로 아이템을 가이드 존 위치로 스냅
    //    3. TaskStateBroadcast / ScenarioSnapshotBroadcast 수신
    //       → 클라이언트 존 활성화/비활성화 관리
    //
    //  배치: GameScene의 적절한 GameObject에 부착
    //        서버 전용 모드에서는 OnEnable에서 조기 리턴
    //
    //  JSON 설정 (modules[].config.extra):
    //    "snapDuration" : DOTween 이동 시간 (기본 0.5)
    //    "snapDelay"    : 서버 완료 딜레이  (기본 1.5)s
    // ============================================================

    public class NetworkInteractionBridge : NetworkBehaviour
    {
        [Header("DOTween Settings")]
        [SerializeField] private Ease  _snapEase       = Ease.OutQuart;

        [Header("Zone Deactivation")]
        [Tooltip("존 비활성화 지연 시간 (스냅 연출 완료 대기용)")]
        [SerializeField] private float _deactivateDelay = 1f;

        private int          _currentTaskIndex = -1;
        private ScenarioData _scenarioData;
        private bool         _isVR;

        // 스냅 연출 진행 상태
        private bool _isSnapping;

        // 지연 비활성화 대기 중인 코루틴 (zoneId → Coroutine)
        private readonly Dictionary<string, Coroutine> _pendingDeactivations = new();

        // ── 생명주기 ──────────────────────────────────────────────

        private void Start()
        {
            TryFillScenarioData();
        }

        // ScenarioData가 없을 때 사용 가능한 소스에서 보완
        private void TryFillScenarioData()
        {
            if (_scenarioData != null) return;

            var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();
            if (stateReceiver?.CurrentScenario != null)
            {
                _scenarioData     = stateReceiver.CurrentScenario;
                _currentTaskIndex = stateReceiver.CurrentTaskState.taskIndex;
#if UNITY_EDITOR
                Debug.Log($"[Bridge] ScenarioData fallback (StateReceiver) Task[{_currentTaskIndex}]");
#endif
                return;
            }

            // ScenarioSnapshotBroadcast가 아직 안 온 경우 ScenarioReceiver에서 보완
            var scenarioReceiver = FindFirstObjectByType<ScenarioReceiver>();
            if (scenarioReceiver?.ReceivedData != null)
            {
                _scenarioData = scenarioReceiver.ReceivedData;
#if UNITY_EDITOR
                Debug.Log($"[Bridge] ScenarioData fallback (ScenarioReceiver)");
#endif
            }
        }

        private void OnEnable()
        {
            // 서버 전용 빌드에서는 동작 안 함
            if (InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted) return;

            _isVR = IsVRDevice();

            // 클라이언트 물리/UI 이벤트 → 서버 신호 변환
            InteractionEvents.OnZoneActivated  += HandleZoneActivated;
            InteractionEvents.OnItemUsed       += HandleItemUsed;
            InteractionEvents.OnItemGrabbed    += HandleItemGrabbed;
            InteractionEvents.OnTaskConfirmed  += HandleTaskConfirmed;

            // 시나리오 상태 수신 (존 활성화 + taskIndex 추적)
            ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      += HandleScenarioReceived;

            // 서버 → 클라이언트 스냅 지시 수신
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.RegisterBroadcast<PlaySnapAnimBroadcast>(OnReceiveSnapBroadcast);
        }

        private void OnDisable()
        {
            InteractionEvents.OnZoneActivated        -= HandleZoneActivated;
            InteractionEvents.OnItemUsed             -= HandleItemUsed;
            InteractionEvents.OnItemGrabbed          -= HandleItemGrabbed;
            InteractionEvents.OnTaskConfirmed        -= HandleTaskConfirmed;
            ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      -= HandleScenarioReceived;

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<PlaySnapAnimBroadcast>(OnReceiveSnapBroadcast);
        }

        // ── VR 감지 ────────────────────────────────────────────────

        private static bool IsVRDevice()
        {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            var d = UnityEngine.XR.XRSettings.loadedDeviceName;
            if (!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
            return false;
        }

        // ── Zone 이벤트 → 서버 신호 ───────────────────────────────

        private void HandleZoneActivated(string zoneId, string itemId)
        {
            if (!InstanceFinder.IsClientStarted) return;
            if (_currentTaskIndex < 0 || _scenarioData == null) return;

            // 1. 현재 태스크 설정 가져오기
            var taskDef = _scenarioData.scenario.tasks[_currentTaskIndex];
            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            if (config == null) return;

            // 2. [핵심 수정] Disassemble 타입인 경우 자동 완료 차단
            // 이 조건이 없으면 닿자마자 아래 Broadcast가 실행되어 볼트가 삭제됩니다.
            if (config.actionType.Equals("Disassemble", System.StringComparison.OrdinalIgnoreCase))
            {
                // 로그를 남겨서 작동 여부를 확인하세요.
                Debug.Log($"<color=cyan>[Bridge]</color> {zoneId} 접촉 감지: 'Disassemble' 단계이므로 자동 완료를 차단합니다. 볼트를 직접 끝까지 풀어야 합니다.");
                return; // ★ 여기서 함수를 종료하여 서버 신호 전송을 막습니다.
            }

            // 3. 기존 로직 (Touch 등은 그대로 유지)
            bool isTouchAction = config.actionType.Equals("Touch", System.StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(itemId) && !isTouchAction) return;

            if (!string.IsNullOrEmpty(itemId) && zoneId != "Player_Hand_Zone")
            {
                ReleaseHeldItem(itemId);
            }

            // 4. 서버로 완료 신호 전송 (이 코드가 실행되면 오브젝트가 삭제됨)
            InstanceFinder.ClientManager.Broadcast(new ZoneInteractionSignal
            {
                taskIndex = _currentTaskIndex,
                zoneId = zoneId,
                itemId = itemId,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });

#if UNITY_EDITOR
        Debug.Log($"[Bridge] ZoneSignal 전송: zone={zoneId}, item={itemId}, task={_currentTaskIndex}");
#endif
        }

        // 아이템 해제 로직
        private void ReleaseHeldItem(string itemId)
        {
            var taskItems = FindObjectsByType<TaskItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var item in taskItems)
            {
                if (item.prefabId == itemId)
                {
                    var syncGrab = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
                    if (syncGrab != null && syncGrab.IsGrabbed)
                    {
                        syncGrab.StopPCHold();
                        syncGrab.RequestRelease();
                    }

                    var grabbable = item.GetComponent<Autohand.Grabbable>() ?? item.GetComponentInParent<Autohand.Grabbable>();
                    if (grabbable != null && grabbable.IsHeld())
                    {
                        grabbable.ForceHandsRelease();
                    }
                    break;
                }
            }
        }

        private void HandleItemUsed(string itemId)
        {
            if (!InstanceFinder.IsClientStarted || _currentTaskIndex < 0) return;

            // PC: zone 태스크는 HandleItemGrabbed에서 ZoneSignal로 처리됨 → ItemUsed 중복 방지
            if (!IsVRDevice() && _scenarioData != null
                && _currentTaskIndex < _scenarioData.scenario.tasks.Count)
            {
                var config = _scenarioData.GetModuleConfig(
                    _scenarioData.scenario.tasks[_currentTaskIndex].moduleId);
                //if (config != null && !string.IsNullOrEmpty(config.targetZoneId)) return;
            }

            InstanceFinder.ClientManager.Broadcast(new ItemUsedSignal
            {
                taskIndex = _currentTaskIndex,
                itemId    = itemId,
                clientId  = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        private void HandleItemGrabbed(string itemId)
        {
            if (!InstanceFinder.IsClientStarted || _currentTaskIndex < 0) return;

            // 현재 기기 상태 실시간 재확인 (캐싱된 _isVR이 잘못되었을 가능성 대비)
            bool effectivelyVR = IsVRDevice();
            Debug.Log($"[Bridge] HandleItemGrabbed 호출 | Item: {itemId} | isVR(cache): {_isVR} | isVR(realtime): {effectivelyVR}");

            // PC/Mobile 전용: Zone 태스크이면 아이템 집기 = 존 도달로 자동 변환
            if (!effectivelyVR && _scenarioData != null
                && _currentTaskIndex < _scenarioData.scenario.tasks.Count)
            {
                var taskDef = _scenarioData.scenario.tasks[_currentTaskIndex];
                var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

                // 해당 태스크가 목표 존(targetZoneId)을 가지고 있는지 확인

                // targetZoneId 있는 Task -> 즉시 완료 신호 보내지 않음
                // TaskInteractionZone.OnTriggerEnter 에서 실제 이동 후 발행
                if(config != null && !string.IsNullOrEmpty(config.targetZoneId))
                {
                    Debug.Log($"[Bridge] Zone Task 감지 ({config.targetZoneId}) — 이동 후 Zone 진입 대기"); return;
                    //var required = config.requiredItems;
                    //// 아이템 ID 검증 (리스트가 비어있으면 통과, 아니면 포함 여부 확인)
                    //bool validItem = required == null || required.Count == 0 || required.Contains(itemId);

                    //if (validItem)
                    //{
                    //    // 서버로 '존 인터랙션' 신호 전송 (이 신호를 서버가 받아야 연출 브로드캐스트를 쏨)
                    //    InstanceFinder.ClientManager.Broadcast(new ZoneInteractionSignal
                    //    {
                    //        taskIndex = _currentTaskIndex,
                    //        zoneId = config.targetZoneId,
                    //        itemId = itemId,
                    //        clientId = InstanceFinder.ClientManager.Connection.ClientId,
                    //    });

                    //    Debug.Log($"[Bridge] PC 클릭 성공 -> ZoneSignal 전송: {itemId} -> {config.targetZoneId}");
                    //    return; // 연출 로직으로 태웠으므로 일반 Grab 신호는 보내지 않음
                    //}
                    //else
                    //{
                    //    Debug.LogWarning($"[Bridge] 아이템 불일치: 현재 태스크에 필요한 아이템이 아님 (입력:{itemId})");
                    //}
                }
            }

            // VR이거나, 목표 존이 없는 일반 집기 태스크인 경우
            InstanceFinder.ClientManager.Broadcast(new ItemGrabbedSignal
            {
                taskIndex = _currentTaskIndex,
                itemId = itemId,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        private void HandleTaskConfirmed(string moduleId)
        {
            if (!InstanceFinder.IsClientStarted || _currentTaskIndex < 0) return;
            InstanceFinder.ClientManager.Broadcast(new TaskConfirmSignal
            {
                taskIndex = _currentTaskIndex,
                moduleId  = moduleId,
                clientId  = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        [TargetRpc]
        public void TargetPlayGloveEquipAnim(NetworkConnection conn)
        {
            StartCoroutine(GloveEquipSequence());
        }

        private IEnumerator GloveEquipSequence()
        {
            // 1. 시각적 연출 실행 (내 손만 바뀜)
            if (PlayerGloveVisualizer.Instance != null)
                PlayerGloveVisualizer.Instance?.ApplyGlove(true);

            // 2. 착용 애니메이션 시간만큼 대기 (예: 1초)
            yield return new WaitForSeconds(1.0f);

            // 3. 서버에 "나 장갑 꼈어"라고 보고 (TaskConfirmed 신호 전송)
            // 이 신호가 서버의 ScenarioRunner로 전달되어 AllPlayers 체크에 합산됨
            InteractionEvents.FireTaskConfirmed("LOTO_Gloves");

            Debug.Log("[Bridge] 장갑 착용 완료 보고 전송됨");
        }

        // ── Task 상태 수신 → 존 활성화/비활성화 ────────────────────

        private void HandleScenarioReceived(ScenarioData data)
        {
            if (data == null) return;
            _scenarioData = data;
#if UNITY_EDITOR
            Debug.Log($"[Bridge] ScenarioData 수신 완료: {data.scenarioId} ({data.scenario.tasks.Count} tasks)");
#endif
        }

        private void HandleSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            _scenarioData     = broadcast.scenarioData;
            _currentTaskIndex = broadcast.currentTask.taskIndex;

            if (broadcast.currentTask.status == TaskStatus.Running)
                SetZoneActive(_currentTaskIndex, true);
        }

        private void HandleTaskStateUpdated(TaskStateBroadcast broadcast)
        {
            int prevIndex     = _currentTaskIndex;
            _currentTaskIndex = broadcast.currentTask.taskIndex;

            if (_scenarioData == null) TryFillScenarioData();

            if (_scenarioData == null) return;

            // 이전 task zone 비활성화 (인덱스가 바뀐 경우)
            if (prevIndex >= 0 && prevIndex != _currentTaskIndex)
                SetZoneActive(prevIndex, false);

            // 현재 task zone 활성화
            if (broadcast.currentTask.status == TaskStatus.Running)
                SetZoneActive(_currentTaskIndex, true);
        }

        private void SetZoneActive(int taskIndex, bool active)
        {
            if (_scenarioData == null) return;
            if (taskIndex < 0 || taskIndex >= _scenarioData.scenario.tasks.Count) return;

            var taskDef = _scenarioData.scenario.tasks[taskIndex];
            var config  = _scenarioData.GetModuleConfig(taskDef.moduleId);

            if (string.IsNullOrEmpty(config?.targetZoneId)) return;

            string zoneId = config.targetZoneId;

            if (active)
            {
                // 대기 중인 비활성화 코루틴이 있으면 취소
                if (_pendingDeactivations.TryGetValue(zoneId, out var pending))
                {
                    StopCoroutine(pending);
                    _pendingDeactivations.Remove(zoneId);
#if UNITY_EDITOR
                    Debug.Log($"[Bridge] 비활성화 취소 (재활성화 요청): {zoneId}");
#endif
                }

                var zone = TaskInteractionZone.Find(zoneId);
                zone?.Activate();
            }
            else
            {
                // 이미 대기 중이면 중복 등록 방지
                if (_pendingDeactivations.ContainsKey(zoneId)) return;

                var co = StartCoroutine(DelayedDeactivate(zoneId));
                _pendingDeactivations[zoneId] = co;

#if UNITY_EDITOR
                Debug.Log($"[Bridge] 비활성화 지연 등록: {zoneId} → {_deactivateDelay}s 후");
#endif
            }
        }

        private IEnumerator DelayedDeactivate(string zoneId)
        {
            // 스냅 연출이 끝날 때까지 대기 후 추가 버퍼
            yield return new WaitUntil(() => !_isSnapping);
            yield return new WaitForSeconds(_deactivateDelay);

            _pendingDeactivations.Remove(zoneId);

            var zone = TaskInteractionZone.Find(zoneId);
            zone?.Deactivate();

#if UNITY_EDITOR
            Debug.Log($"[Bridge] 지연 비활성화 실행: {zoneId}");
#endif
        }

        // ── DOTween 스냅 애니메이션 ─────────────────────────────────

        private void OnReceiveSnapBroadcast(PlaySnapAnimBroadcast broadcast, Channel channel)
        {
            // 현재 task가 아니면 무시
            if (broadcast.taskIndex != _currentTaskIndex) return;

            Debug.Log($"[Bridge] 스냅 신호 수신! 대상: {broadcast.itemId}, 목표: {broadcast.guideId}");

            // 아이템 탐색 (비활성 포함)
            TaskItem target = null;
            foreach (var item in FindObjectsByType<TaskItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (item.prefabId == broadcast.itemId)
                {
                    target = item;
                    break;
                }
            }

            if (target == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Bridge] 스냅 대상 없음: {broadcast.itemId}");
#endif
                return;
            }

            // 목표 존 위치 탐색 (존은 항상 씬에 존재하므로 레지스트리로 바로 탐색)
            var guideZone = TaskInteractionZone.Find(broadcast.guideId);
            if (guideZone == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Bridge] 가이드 존 없음: {broadcast.guideId}");
#endif
                return;
            }

            // 위치를 먼저 캐싱 (이후 존이 비활성화돼도 안전)
            Vector3 targetPos = guideZone.transform.position;

            // LocalNetworkTransform 비활성화 (DOTween과 충돌 방지)
            // 아이템은 snapDelay 후 서버에서 디스폰되므로 재활성화 불필요
            var lnt = target.GetComponent<LocalNetworkTransform>();
            if (lnt != null) lnt.enabled = false;

            // Rigidbody kinematic 전환
            var rb = target.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // DOTween 스냅
            _isSnapping = true;

            target.transform
                  .DOMove(targetPos, broadcast.snapDuration)
                  .SetEase(_snapEase)
                  .OnComplete(() => _isSnapping = false);

#if UNITY_EDITOR
            Debug.Log($"[Bridge] 스냅 시작: {broadcast.itemId} → {broadcast.guideId} " +
                      $"| pos={targetPos} | {broadcast.snapDuration}s");
#endif
        }
    }
}
