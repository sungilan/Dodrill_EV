using UnityEngine;
using System.Collections.Generic;
using FishNet.Object;

public class TaskDirectionCompass : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScenarioStateReceiver _stateReceiver;
    [SerializeField] private Transform _playerRoot;
    [SerializeField] private Transform _head;
    [SerializeField] private GameObject _arrowPrefab;
    [Tooltip("비워두면 VFX_Arrow를 자동 탐색해 사용")]
    [SerializeField] private string _arrowNameHint = "VFX_Arrow";

    [Header("Display")]
    [SerializeField] private float _showDistance = 2.5f;   // 1.4 → 2.5 로 복구
    [SerializeField] private float _hideDistance = 1.6f;   // 1.0 → 1.6 로 복구
    [SerializeField] private Vector3 _anchorOffset = new Vector3(0f, -0.2f, 0.65f);
    [SerializeField] private bool _followHeadYaw = true;
    [SerializeField] private bool _followHeadPitchAndRoll = true;
    [SerializeField] private bool _invertDirection = true;
    [Tooltip("VFX 로컬 축 보정")]
    [SerializeField] private Vector3 _arrowRotationOffsetEuler = new Vector3(-90f, 180f, 180f); // Y 0 → 180 복구
    [SerializeField] private bool _blinkArrow = true;
    [SerializeField] private float _blinkIntervalSeconds = 0.5f;

    [Header("Zone Guide")]
    [SerializeField] private bool _preferZoneWhileHoldingRequiredItem = true;
    [SerializeField][Range(0f, 180f)] private float _zoneFacingToleranceDeg = 16f;
    [SerializeField][Range(0f, 180f)] private float _angleShowThresholdDeg = 38f;
    [SerializeField][Range(0f, 180f)] private float _angleHideThresholdDeg = 22f;
    [SerializeField] private bool _hideArrowWhenFacingZone = true;

    private readonly List<string> _currentRequiredItems = new();
    private Transform _currentTarget;
    private bool _currentTargetIsZone;
    private GameObject _arrowInstance;
    private bool _isVisible;
    private bool _isAngleGuiding;
    private float _nextResolveTime;
    private NetworkObject _ownerNetworkObject;
    private bool _hasSyncedTaskState;
    private TaskState _latestTaskState;

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        RefreshRequiredItemsFromState();
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
    }

    private void Update()
    {
        TryResolveRuntimeReferences();
        if(!ShouldRenderForLocalPlayer())
        {
            _isAngleGuiding = false;
            SetArrowVisible(false);
            return;
        }

        if(_playerRoot == null || _arrowPrefab == null) return;

        if(IsTaskActivelyInProgress())
        {
            _isAngleGuiding = false;
            _currentTarget = null;
            _currentTargetIsZone = false;
            SetArrowVisible(false);
            return;
        }

        if(IsLocalPlayerInteractingRequiredItem())
        {
            _isAngleGuiding = false;
            _currentTarget = null;
            _currentTargetIsZone = false;
            SetArrowVisible(false);
            return;
        }

        if(IsLocalPlayerPCHoldingRequiredItem())
        {
            _isAngleGuiding = false;
            _currentTarget = null;
            _currentTargetIsZone = false;
            SetArrowVisible(false);
            return;
        }

        ResolveTargetIfNeeded();
        UpdateArrowPoseAndVisibility();
    }

    private void TryResolveRuntimeReferences()
    {
        if(Time.unscaledTime < _nextResolveTime) return;
        _nextResolveTime = Time.unscaledTime + 0.5f;

        if(_stateReceiver == null)
            _stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();

        if(_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if(_playerRoot == null)
            _playerRoot = transform.root != null ? transform.root : transform;

        if(_head == null)
        {
            var cam = Camera.main;
            if(cam != null) _head = cam.transform;
        }

        if(_arrowPrefab == null)
            _arrowPrefab = FindArrowPrefabCandidate();
    }

    private bool ShouldRenderForLocalPlayer()
    {
        if(_ownerNetworkObject == null)
            return true;

        if(!_ownerNetworkObject.IsSpawned || !_ownerNetworkObject.Owner.IsValid)
            return false;

        return _ownerNetworkObject.Owner.IsLocalClient;
    }

    private GameObject FindArrowPrefabCandidate()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var t in all)
        {
            if(t == null) continue;
            if(string.IsNullOrEmpty(_arrowNameHint)) continue;
            if(t.name.Contains(_arrowNameHint))
                return t.gameObject;
        }
        return null;
    }

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        _latestTaskState = broadcast.currentTask;
        _hasSyncedTaskState = true;
        RefreshRequiredItemsFromState();
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _latestTaskState = broadcast.currentTask;
        _hasSyncedTaskState = true;
        RefreshRequiredItemsFromState();
    }

    private void RefreshRequiredItemsFromState()
    {
        _currentRequiredItems.Clear();
        _currentTarget = null;
        _currentTargetIsZone = false;

        if(_stateReceiver == null) return;
        if(_stateReceiver.CurrentScenario == null) return;

        var state = _stateReceiver.CurrentTaskState;
        int taskIndex = state.taskIndex;
        var tasks = _stateReceiver.CurrentScenario.scenario.tasks;
        if(taskIndex < 0 || taskIndex >= tasks.Count) return;

        var taskDef = tasks[taskIndex];
        var config = _stateReceiver.CurrentScenario.GetModuleConfig(taskDef.moduleId);
        if(config?.requiredItems == null || config.requiredItems.Count == 0) return;

        if(state.status == TaskStatus.Running &&
            state.totalSteps > 0 &&
            state.currentStepIndex >= 0 &&
            state.currentStepIndex < config.requiredItems.Count)
        {
            _currentRequiredItems.Add(config.requiredItems[state.currentStepIndex]);
            return;
        }

        foreach(var id in config.requiredItems)
        {
            if(!string.IsNullOrEmpty(id))
                _currentRequiredItems.Add(id);
        }
    }

    private void ResolveTargetIfNeeded()
    {
        if(_preferZoneWhileHoldingRequiredItem &&
            TryResolveZoneTargetWhileHoldingRequiredItem(out Transform zoneTarget))
        {
            _currentTarget = zoneTarget;
            _currentTargetIsZone = true;
            return;
        }

        _currentTargetIsZone = false;
        if(_currentTarget != null && _currentTarget.gameObject.activeInHierarchy)
            return;

        if(_currentRequiredItems.Count > 0)
        {
            _currentTarget = FindClosestRequiredItem();
            if(_currentTarget != null) return;
        }

        _currentTarget = FindClosestFilteredTaskItem();
    }

    private Transform FindClosestRequiredItem()
    {
        Transform best = null;
        float bestSqr = float.MaxValue;
        Vector3 origin = GetOriginPosition();

        foreach(var requiredId in _currentRequiredItems)
        {
            if(string.IsNullOrEmpty(requiredId)) continue;
            foreach(var kv in TaskItem.Registry)
            {
                var item = kv.Value;
                if(item == null || !item.gameObject.activeInHierarchy) continue;
                //if(!SceneItemRegistry.ItemIdsMatch(requiredId, item.prefabId)) continue;
                if(requiredId != item.prefabId) continue;

                float sqr = (item.transform.position - origin).sqrMagnitude;
                if(sqr < bestSqr) { bestSqr = sqr; best = item.transform; }
            }
        }
        return best;
    }

    private Transform FindClosestFilteredTaskItem()
    {
        Transform best = null;
        float bestSqr = float.MaxValue;
        Vector3 origin = GetOriginPosition();

        foreach(var kv in TaskItem.Registry)
        {
            var item = kv.Value;
            if(item == null || !item.gameObject.activeInHierarchy) continue;
            //if(!TaskItemFilter.IsCurrentTaskItem(item.prefabId)) continue;

            float sqr = (item.transform.position - origin).sqrMagnitude;
            if(sqr < bestSqr) { bestSqr = sqr; best = item.transform; }
        }
        return best;
    }

    private bool TryResolveZoneTargetWhileHoldingRequiredItem(out Transform zoneTarget)
    {
        zoneTarget = null;
        if(_stateReceiver == null || _stateReceiver.CurrentScenario == null)
            return false;

        string zoneId = ResolveCurrentTargetZoneId();
        if(string.IsNullOrWhiteSpace(zoneId)) return false;
        if(!IsLocalPlayerHoldingRequiredItem()) return false;

        var zone = TaskInteractionZone.Find(zoneId);
        if(zone == null || !zone.gameObject.activeInHierarchy) return false;

        zoneTarget = zone.transform;
        return true;
    }

    private string ResolveCurrentTargetZoneId()
    {
        var state = _stateReceiver.CurrentTaskState;
        //if(!string.IsNullOrWhiteSpace(state.currentStepZoneId))
            //return state.currentStepZoneId;

        var scenario = _stateReceiver.CurrentScenario;
        if(scenario?.scenario?.tasks == null) return string.Empty;

        int taskIndex = state.taskIndex;
        if(taskIndex < 0 || taskIndex >= scenario.scenario.tasks.Count) return string.Empty;

        string moduleId = scenario.scenario.tasks[taskIndex].moduleId;
        if(string.IsNullOrWhiteSpace(moduleId)) return string.Empty;

        var config = scenario.GetModuleConfig(moduleId);
        return config?.targetZoneId ?? string.Empty;
    }

    private bool IsLocalPlayerHoldingRequiredItem()
    {
        if(_currentRequiredItems.Count == 0) return false;

        foreach(var kv in TaskItem.Registry)
        {
            var item = kv.Value;
            if(item == null || !item.gameObject.activeInHierarchy) continue;
            if(string.IsNullOrWhiteSpace(item.prefabId)) continue;
            if(!IsRequiredItem(item.prefabId)) continue;

            var grab = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(grab == null || !grab.IsGrabbed) continue;
            if(!grab.IsOwner) continue;

            // PC/Mobile이면 zone 안내 안 함
            //if(grab.IsPCHolding) return false;

            // VR이면 왼손으로 잡고 있을 때만 zone 안내
            //if(grab.GrabbingHand != null && grab.GrabbingHand.left) return true;
        }

        return false;
    }

    private bool IsLocalPlayerPCHoldingRequiredItem()
    {
        if(_currentRequiredItems.Count == 0) return false;

        foreach(var kv in TaskItem.Registry)
        {
            var item = kv.Value;
            if(item == null || !item.gameObject.activeInHierarchy) continue;
            if(string.IsNullOrWhiteSpace(item.prefabId)) continue;
            if(!IsRequiredItem(item.prefabId)) continue;

            var grab = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(grab == null || !grab.IsGrabbed) continue;
            if(!grab.IsOwner) continue;
            //if(grab.IsPCHolding) return true;
        }

        return false;
    }

    private bool IsLocalPlayerInteractingRequiredItem()
    {
        if(_currentRequiredItems.Count == 0) return false;

        foreach(var kv in TaskItem.Registry)
        {
            var item = kv.Value;
            if(item == null || !item.gameObject.activeInHierarchy) continue;
            if(string.IsNullOrWhiteSpace(item.prefabId)) continue;
            if(!IsRequiredItem(item.prefabId)) continue;

            var grab = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(grab == null) continue;
            if(!grab.IsOwner) continue;

            // 연출(PC 자동 이동 포함)과 VR 그랩 진행 중이면 무조건 숨김.
            if(grab.IsGrabbed) return true;
        }

        return false;
    }

    private bool IsRequiredItem(string prefabId)
    {
        for(int i = 0; i < _currentRequiredItems.Count; i++)
            //if(SceneItemRegistry.ItemIdsMatch(_currentRequiredItems[i], prefabId))
            if(string.Equals(_currentRequiredItems[i], prefabId, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void UpdateArrowPoseAndVisibility()
    {
        if(_currentTarget == null)
        {
            _isAngleGuiding = false;
            SetArrowVisible(false);
            return;
        }

        Vector3 origin = GetOriginPosition();
        Vector3 toTarget = _currentTarget.position - origin;
        float horizontalDistance = new Vector2(toTarget.x, toTarget.z).magnitude;

        bool isFarEnough = _isVisible
            ? horizontalDistance >= _hideDistance
            : horizontalDistance >= _showDistance;

        bool isAngleOff = IsAngleGuidanceNeeded(toTarget); // true = 안 보고 있음

        // 안 보고 있으면 거리 무관하게 표시
        // 보고 있어도 멀면 표시
        // 보고 있고 가까우면 숨김
        bool shouldShow = isAngleOff || isFarEnough;

        if(!shouldShow)
        {
            _isAngleGuiding = false;
            SetArrowVisible(false);
            return;
        }

        EnsureArrowInstance();
        if(_arrowInstance == null) return;

        bool blinkVisible = !_blinkArrow || IsBlinkOn();
        SetArrowVisible(blinkVisible);

        ResolveAnchorBasis(out Vector3 rightBase, out Vector3 upBase, out Vector3 forwardBase);
        Vector3 worldPos = origin
            + rightBase * _anchorOffset.x
            + upBase * _anchorOffset.y
            + forwardBase * _anchorOffset.z;
        _arrowInstance.transform.position = worldPos;

        Vector3 dir = toTarget;
        if(!_followHeadPitchAndRoll) dir.y = 0f;
        if(_invertDirection) dir = -dir;

        if(dir.sqrMagnitude > 0.0001f)
        {
            Quaternion yaw = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _arrowInstance.transform.rotation = yaw * Quaternion.Euler(_arrowRotationOffsetEuler);
        }
    }

    private bool IsTaskActivelyInProgress()
    {
        // TaskState 미수신 초기 프레임에서 default(activePlayerId=0) 오판 방지.
        if(!_hasSyncedTaskState)
            return false;

        // 진행자 확정(activePlayerId >= 0) 상태면 연출/진행 중으로 간주해 화살표를 숨긴다.
        return _latestTaskState.activePlayerId >= 0;
    }

    private bool IsBlinkOn()
    {
        float interval = Mathf.Max(0.05f, _blinkIntervalSeconds);
        int phase = Mathf.FloorToInt(Time.unscaledTime / interval);
        return (phase & 1) == 0;
    }

    // true = 안 보고 있다 (화살표 필요)  /  false = 잘 보고 있다
    private bool IsAngleGuidanceNeeded(Vector3 directionToTarget)
    {
        float hideThreshold = Mathf.Max(0f, _angleHideThresholdDeg);
        float showThreshold = Mathf.Max(hideThreshold, _angleShowThresholdDeg);

        float angle = GetFacingAngle(directionToTarget);

        if(_isAngleGuiding)
        {
            if(angle <= hideThreshold) _isAngleGuiding = false;
        }
        else
        {
            if(angle >= showThreshold) _isAngleGuiding = true;
        }

        return _isAngleGuiding;
    }

    private float GetFacingAngle(Vector3 directionToTarget)
    {
        if(directionToTarget.sqrMagnitude < 0.0001f) return 0f;

        Vector3 baseForward = (_followHeadYaw && _head != null)
            ? _head.forward
            : (_playerRoot != null ? _playerRoot.forward : transform.forward);

        // 시선 정렬 판정은 수평(yaw) 기준으로만 계산해
        // 위/아래 고개 각도 때문에 화살표가 계속 켜지는 현상을 줄인다.
        baseForward.y = 0f;
        directionToTarget.y = 0f;

        if(baseForward.sqrMagnitude < 0.0001f || directionToTarget.sqrMagnitude < 0.0001f)
            return 0f;

        return Vector3.Angle(baseForward.normalized, directionToTarget.normalized);
    }

    private void ResolveAnchorBasis(out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        if(_followHeadYaw && _head != null)
        {
            if(_followHeadPitchAndRoll)
            {
                right = _head.right.normalized;
                up = _head.up.normalized;
                forward = _head.forward.normalized;
                return;
            }

            forward = _head.forward;
            forward.y = 0f;
            if(forward.sqrMagnitude < 0.0001f)
                forward = _playerRoot != null ? _playerRoot.forward : transform.forward;
            forward.Normalize();
            right = Vector3.Cross(Vector3.up, forward).normalized;
            up = Vector3.up;
            return;
        }

        forward = (_playerRoot != null ? _playerRoot.forward : transform.forward);
        if(forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        forward.Normalize();

        right = (_playerRoot != null ? _playerRoot.right : transform.right);
        if(right.sqrMagnitude < 0.0001f) right = Vector3.Cross(Vector3.up, forward).normalized;
        else right.Normalize();

        up = (_playerRoot != null ? _playerRoot.up : transform.up);
        if(up.sqrMagnitude < 0.0001f) up = Vector3.up;
        else up.Normalize();
    }

    private Vector3 GetOriginPosition()
    {
        if(_head != null) return _head.position;
        if(_playerRoot != null) return _playerRoot.position;
        return transform.position;
    }

    private void EnsureArrowInstance()
    {
        if(_arrowInstance != null) return;
        _arrowInstance = Instantiate(_arrowPrefab);
        _arrowInstance.name = $"{_arrowPrefab.name}_TaskCompass";
        _arrowInstance.SetActive(false);
    }

    private void SetArrowVisible(bool visible)
    {
        _isVisible = visible;
        if(_arrowInstance != null)
            _arrowInstance.SetActive(visible);
    }
}