
using DG.Tweening;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoDrill.Training
{
    // ============================================================
    //  NetworkInteractionBridge.cs
    //  클라이언트 전용 — 존 인터랙션 ↔ 서버 신호 변환 + DOTween 스냅 애니메이션
    // ============================================================

    public class NetworkInteractionBridge : NetworkBehaviour
    {
        [Header("DOTween Settings")]
        [SerializeField] private Ease _snapEase = Ease.OutQuart;

        [Header("Zone Deactivation")]
        [Tooltip("존 비활성화 지연 시간 (스냅 연출 완료 대기용)")]
        [SerializeField] private float _deactivateDelay = 1f;

        [Header("Current State (Debug)")]
        [SerializeField] private int _currentTaskIndex = 0;
        [SerializeField] private ScenarioData _scenarioData;

        private bool _isVR;
        private bool _isSnapping;

        // 지연 비활성화 대기 중인 코루틴 (zoneId → Coroutine)
        private readonly Dictionary<string, Coroutine> _pendingDeactivations = new();

        // ── 생명주기 ──────────────────────────────────────────────

        private void Start()
        {
            // 시작 시 이미 데이터가 와있는지 한 번 더 확인
            TryFillScenarioData();
        }

        private void TryFillScenarioData()
        {
            if (_scenarioData != null) return;

            // 1. 싱글톤을 통한 직접 참조 (가장 빠름)
            if (ScenarioStateReceiver.Instance != null && ScenarioStateReceiver.Instance.CurrentScenario != null)
            {
                AssignScenarioData(ScenarioStateReceiver.Instance.CurrentScenario);
                _currentTaskIndex = ScenarioStateReceiver.Instance.CurrentTaskState.taskIndex;
                Debug.Log("<color=lime>[Bridge]</color> ScenarioStateReceiver 싱글톤에서 데이터 복구 성공");
                return;
            }

            // 2. 씬 탐색 (폴백)
            var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();
            if (stateReceiver?.CurrentScenario != null)
            {
                AssignScenarioData(stateReceiver.CurrentScenario);
                _currentTaskIndex = stateReceiver.CurrentTaskState.taskIndex;
                Debug.Log("<color=lime>[Bridge]</color> 씬 내 StateReceiver 오브젝트에서 데이터 복구 성공");
            }
        }

        private void OnEnable()
        {
            if (InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted) return;

            _isVR = IsVRDevice();

            // 이벤트 구독
            InteractionEvents.OnZoneActivated += HandleZoneActivated;
            InteractionEvents.OnItemUsed += HandleItemUsed;
            InteractionEvents.OnItemGrabbed += HandleItemGrabbed;
            InteractionEvents.OnTaskConfirmed += HandleTaskConfirmed;
            InteractionEvents.OnValueMeasured += HandleValueMeasured;

            ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived += HandleScenarioReceived; // 시나리오 전체 데이터 수신 이벤트

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.RegisterBroadcast<PlaySnapAnimBroadcast>(OnReceiveSnapBroadcast);
        }

        private void OnDisable()
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            InteractionEvents.OnItemUsed -= HandleItemUsed;
            InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
            InteractionEvents.OnTaskConfirmed -= HandleTaskConfirmed;
            ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived -= HandleScenarioReceived;
            InteractionEvents.OnValueMeasured -= HandleValueMeasured;

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<PlaySnapAnimBroadcast>(OnReceiveSnapBroadcast);
        }

        // ── 데이터 할당 공통 로직 ──────────────────────────────────────

        private void AssignScenarioData(ScenarioData data)
        {
            if (data == null) return;
            _scenarioData = data;
            Debug.Log($"<color=lime>[Bridge]</color> 시나리오 데이터 할당 완료: <b>{data.scenarioName}</b>");
        }

        // ── 이벤트 핸들러 (데이터 수신) ───────────────────────────────

        private void HandleScenarioReceived(ScenarioData data)
        {
            AssignScenarioData(data);
        }

        private void HandleSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            AssignScenarioData(broadcast.scenarioData);
            _currentTaskIndex = broadcast.currentTask.taskIndex;
            Debug.Log($"<color=lime>[Bridge]</color> 스냅샷 수신: Task Index = {_currentTaskIndex}");
        }

        private void HandleTaskStateUpdated(TaskStateBroadcast broadcast)
        {
            int prevIndex = _currentTaskIndex;
            _currentTaskIndex = broadcast.currentTask.taskIndex;

            if (_scenarioData == null) TryFillScenarioData();

            if (prevIndex >= 0 && prevIndex != _currentTaskIndex)
                SetZoneActive(prevIndex, false);

            if (broadcast.currentTask.status == TaskStatus.Running)
                SetZoneActive(_currentTaskIndex, true);
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

        // ── Zone 이벤트 처리 ───────────────────────────────────────

        private void HandleZoneActivated(string zoneId, string itemId)
        {
            if (!InstanceFinder.IsClientStarted) return;

            // 데이터가 없을 경우 실시간 복구 시도
            if (_scenarioData == null) TryFillScenarioData();

            // 데이터가 있을 때만 정밀 검사 수행
            if (_scenarioData != null && _currentTaskIndex >= 0 && _currentTaskIndex < _scenarioData.scenario.tasks.Count)
            {
                var taskDef = _scenarioData.scenario.tasks[_currentTaskIndex];
                var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

                if (config != null)
                {
                    bool isConfirmAction = config.actionType.Equals("ConfirmTask", StringComparison.OrdinalIgnoreCase);
                    // 1. Disassemble 타입이면 자동 완료 차단
                    if (config.actionType.Equals("Disassemble", System.StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"<color=cyan>[Bridge]</color> {zoneId} 접촉: 'Disassemble' 단계이므로 직접 해제 대기.");
                        //return;
                    }

                    // 2. Touch 액션 여부 판단
                    bool isTouchAction = config.actionType.Equals("Touch", System.StringComparison.OrdinalIgnoreCase);
                    //if (string.IsNullOrEmpty(itemId) && !isTouchAction) return;
                }
            }
            else
            {
                // 데이터가 없는 경우의 방어 로그
                Debug.LogWarning($"<color=orange>[Bridge]</color> 데이터 없음(Index:{_currentTaskIndex}): {zoneId} 신호를 검증 없이 서버로 보냅니다.");
            }

            // 아이템 릴리즈: 부품을 제자리에 '놓는' 단계(Swap)의 목표 존일 때만.
            // 예전에는 아이템을 든 채 보낸 존 신호마다 내려놓아서, 드릴로 볼트를 푸는 중(ZoneAndUse) F 를 누르면 드릴이 손에서 떨어졌다.
            if (!string.IsNullOrEmpty(itemId) && zoneId != "Player_Hand_Zone" && ShouldReleaseOnZone(zoneId))
            {
                ReleaseHeldItem(itemId);
            }

            // 서버로 신호 전송
            InstanceFinder.ClientManager.Broadcast(new ZoneInteractionSignal
            {
                taskIndex = _currentTaskIndex,
                zoneId = zoneId,
                itemId = itemId,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        /// <summary>이 존 신호에서 든 아이템을 내려놓아야 하는지. 시나리오 정보가 없으면 예전처럼 내려놓는다.</summary>
        private bool ShouldReleaseOnZone(string zoneId)
        {
            if (_scenarioData == null || _currentTaskIndex < 0 || _currentTaskIndex >= _scenarioData.scenario.tasks.Count) return true;
            var config = _scenarioData.GetModuleConfig(_scenarioData.scenario.tasks[_currentTaskIndex].moduleId);
            if (config == null || string.IsNullOrEmpty(config.actionType)) return true;
            if (!config.actionType.Equals("Swap", StringComparison.OrdinalIgnoreCase)) return false;
            return string.IsNullOrEmpty(config.targetZoneId) || string.Equals(config.targetZoneId, zoneId, StringComparison.OrdinalIgnoreCase);
        }

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

        // ── 기타 핸들러 (수정 없음) ──────────────────────────────────

        private void HandleItemUsed(string itemId)
        {
            if (!InstanceFinder.IsClientStarted || _currentTaskIndex < 0) return;
            InstanceFinder.ClientManager.Broadcast(new ItemUsedSignal
            {
                taskIndex = _currentTaskIndex,
                itemId = itemId,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        private void HandleItemGrabbed(string itemId)
        {
            if (!InstanceFinder.IsClientStarted || _currentTaskIndex < 0) return;
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
                moduleId = moduleId,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        private void HandleValueMeasured(string terminalId, float value)
        {
            if(!InstanceFinder.IsClientStarted || _currentTaskIndex < 0)
            {
                Debug.LogWarning($"<color=red>[Bridge]</color> 측정 신호 무시됨: ClientStarted={InstanceFinder.IsClientStarted}, TaskIndex={_currentTaskIndex}");
                return;
            }

            // ★ 이 로그가 찍혀야 Bridge가 정상 작동하는 것입니다.
            Debug.Log($"<color=orange>[Bridge]</color> 측정 신호 수신 및 서버 전송 시도: 단자={terminalId}, 값={value:F2}");

            InstanceFinder.ClientManager.Broadcast(new ValueMeasuredSignal
            {
                taskIndex = _currentTaskIndex,
                terminalId = terminalId,
                value = value,
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
            });
        }

        private void SetZoneActive(int taskIndex, bool active)
        {
            if (_scenarioData == null) return;
            if (taskIndex < 0 || taskIndex >= _scenarioData.scenario.tasks.Count) return;

            var taskDef = _scenarioData.scenario.tasks[taskIndex];
            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            if (string.IsNullOrEmpty(config?.targetZoneId)) return;

            string zoneId = config.targetZoneId;
            if (active)
            {
                if (_pendingDeactivations.TryGetValue(zoneId, out var pending))
                {
                    StopCoroutine(pending);
                    _pendingDeactivations.Remove(zoneId);
                }
                TaskInteractionZone.Find(zoneId)?.Activate();
            }
            else
            {
                if (_pendingDeactivations.ContainsKey(zoneId)) return;
                var co = StartCoroutine(DelayedDeactivate(zoneId));
                _pendingDeactivations[zoneId] = co;
            }
        }

        private IEnumerator DelayedDeactivate(string zoneId)
        {
            yield return new WaitUntil(() => !_isSnapping);
            yield return new WaitForSeconds(_deactivateDelay);
            _pendingDeactivations.Remove(zoneId);
            TaskInteractionZone.Find(zoneId)?.Deactivate();
        }

        private void OnReceiveSnapBroadcast(PlaySnapAnimBroadcast broadcast, Channel channel)
        {
            if (broadcast.taskIndex != _currentTaskIndex) return;

            TaskItem target = null;
            foreach (var item in FindObjectsByType<TaskItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (item.prefabId == broadcast.itemId) { target = item; break; }
            }

            if (target == null) return;
            // 끼워 넣을 위치: 가이드 구역 → 같은 이름의 오브젝트(Guide_LockBox 등) → 지금 단계의 목표 구역 순으로 찾는다.
            // 예전에는 가이드 ID 로 구역을 못 찾으면 그냥 끝나, 놓은 물건이 공중에 멈춘 채 다음 단계까지(약 2초) 기다렸다.
            Transform snapTo = TaskInteractionZone.Find(broadcast.guideId)?.transform;
            if (snapTo == null && !string.IsNullOrEmpty(broadcast.guideId))
            {
                var go = GameObject.Find(broadcast.guideId);
                if (go != null) snapTo = go.transform;
            }
            if (snapTo == null && _scenarioData != null && _currentTaskIndex >= 0 && _currentTaskIndex < _scenarioData.scenario.tasks.Count)
            {
                var cfg = _scenarioData.GetModuleConfig(_scenarioData.scenario.tasks[_currentTaskIndex].moduleId);
                if (!string.IsNullOrEmpty(cfg?.targetZoneId)) snapTo = TaskInteractionZone.Find(cfg.targetZoneId)?.transform;
            }
            if (snapTo == null)
            {
                Debug.LogWarning($"<color=orange>[Bridge]</color> 스냅 위치를 찾지 못함: {broadcast.guideId}");
                return;
            }

            Vector3 targetPos = snapTo.position;
            var lnt = target.GetComponent<LocalNetworkTransform>();
            if (lnt != null) lnt.enabled = false;

            var rb = target.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            _isSnapping = true;
            target.transform.DOMove(targetPos, broadcast.snapDuration)
                  .SetEase(_snapEase)
                  .OnComplete(() => _isSnapping = false);
        }
    }
}