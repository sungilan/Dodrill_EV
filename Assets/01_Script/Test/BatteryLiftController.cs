using DG.Tweening;
using DoDrill;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections;
using UnityEngine;

public class BatteryLiftController : LiftControllerBase
{
    [Header("── TransformBased 전용 ──")]
    public Transform liftPlate;
    public float maxHeight = 0.5f;

    [Header("── 배터리팩 연동 ──")]
    [Tooltip("수동 지정 (비우면 자동 탐색)")]
    public Transform batteryPackTransform;
    public Transform animBatteryPackTransform;

    [Tooltip("부착을 시도할 최소 높이")]
    public float contactHeight = 0.08f;

    [Header("── ★ 차량 정위치 배치 ──")]
    [Tooltip("차량 정위치 트리거")]
    public Collider vehiclePositionTrigger;
    [Tooltip("차량 정위치 진입 시 이동할 목표 위치")]
    public Transform vehiclePositionTarget;
    [Tooltip("목표 위치로 이동하는 시간 (초)")]
    public float moveToPositionDuration = 0.5f;
    [Tooltip("목표 위치로 이동할 때의 Ease")]
    public Ease moveToPositionEase = Ease.InOutQuad;

    [Header("── ★ 정위치 도달 시 회전 오프셋 ──")]
    [Tooltip("정위치에 도달했을 때 적용할 회전 오프셋 (X, Y, Z)")]
    public Vector3 positionReachedRotationOffset = new Vector3(-90f, 0f, 0f);

    //[Header("── ★ 배터리 감지 방식 ──")]
    [Tooltip("배터리 감지 방식: Height = 높이만 / Collider = 콜라이더 접촉")]
    public enum BatteryDetectionMode { Height, Collider }
    public BatteryDetectionMode detectionMode = BatteryDetectionMode.Collider;
    [Tooltip("배터리 감지 트리거 (자동 탐색됨, 수동 지정 가능)")]
    public Collider batteryDetectionTrigger;

    [Header("배터리 상태")]
    private readonly SyncVar<bool> _syncBatteryAttached = new SyncVar<bool>();
    private readonly SyncVar<bool> _syncIsInPosition = new SyncVar<bool>();

    public bool IsBatteryAttached => _syncBatteryAttached.Value;
    public bool IsInPosition => _syncIsInPosition.Value;

    private bool _isLocallyInPosition = false;
    private bool _isBatteryInContactZone = false;
    private Vector3 _plateOrigin;
    private Vector3 _batteryOrigin;
    private Vector3 _animBatteryOffset;
    private Rigidbody _rigidbody;
    private bool _isMovingToPosition = false;
    private LocalNetworkTransform _lnt;
    private Collider _liftCollider;

    private SyncGrab _liftSyncGrab;

    protected override float GetMaxHeight() => maxHeight;

    public override void OnStartClient()
    {
        base.OnStartClient();
        _syncBatteryAttached.OnChange += OnBatteryAttachmentChanged;
        _syncIsInPosition.OnChange += OnIsInPositionChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _syncBatteryAttached.OnChange -= OnBatteryAttachmentChanged;
        _syncIsInPosition.OnChange -= OnIsInPositionChanged;
    }

    private void OnBatteryAttachmentChanged(bool prev, bool next, bool asServer)
    {
        Debug.Log($"[BatteryLift] ★★★ 배터리 부착 상태 변경: {prev} → {next} ★★★");

        if(next)
        {
            Debug.Log($"[BatteryLift] ✅ 배터리 팩이 잭에 부착되었습니다!");
        }
        else
        {
            Debug.Log($"[BatteryLift] ❌ 배터리 팩이 분리되었습니다.");
        }

        if(next && liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
        {
            _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;
        }
    }

    private void OnIsInPositionChanged(bool prev, bool next, bool asServer)
    {
        Debug.Log($"[BatteryLift] 정위치 상태 변경: {prev} → {next}");
    }

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null) liftAnimator.enabled = false;
            else
            {
                var anim = GetComponent<Animator>();
                if(anim != null) anim.enabled = false;
            }

            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
        }

        _rigidbody = GetComponent<Rigidbody>();
        _lnt = GetComponent<LocalNetworkTransform>();
        _liftSyncGrab = GetComponent<SyncGrab>();
        _liftCollider = GetComponent<Collider>();

        Debug.Log($"[BatteryLift] 시작 - 모드: {liftMode}, 감지 방식: {detectionMode}, 접촉 높이: {contactHeight}");

        StartCoroutine(AutoSearchAndAttachBatteryPack());
        if(vehiclePositionTrigger == null)
            StartCoroutine(AutoSearchVehiclePositionTrigger());
    }

    private IEnumerator AutoSearchVehiclePositionTrigger()
    {
        var wait = new WaitForSeconds(0.5f);

        while(vehiclePositionTrigger == null)
        {
            var allColliders = FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach(var col in allColliders)
            {
                if(!col.isTrigger) continue;

                string name = col.gameObject.name.ToLower();
                if(name.Contains("battery_mount") || name.Contains("battery_zone"))
                {
                    vehiclePositionTrigger = col;
                    Debug.Log($"🎉 [BatteryLift] 차량 정위치 트리거 자동 발견: {col.gameObject.name}");
                    yield break;
                }
            }

            yield return wait;
        }
    }

    private IEnumerator AutoSearchAndAttachBatteryPack()
    {
        var wait = new WaitForSeconds(0.5f);

        while(true)
        {
            bool isAttached = (liftMode == LiftMode.TransformBased && batteryPackTransform != null) ||
                              (liftMode == LiftMode.AnimatorBased && animBatteryPackTransform != null);

            if(isAttached)
            {
                Debug.Log($"🎉 [BatteryLift] 배터리팩 자동 탐색 완료");

                // ★ [핵심] 배터리 발견 시 감지 트리거도 함께 탐색
                if(detectionMode == BatteryDetectionMode.Collider && batteryDetectionTrigger == null)
                {
                    SearchBatteryDetectionTrigger();
                }

                yield break;
            }

            var allBatteries = FindObjectsByType<Battery>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach(var battery in allBatteries)
            {
                var netObj = battery.GetComponent<NetworkObject>();
                if(netObj == null) continue;

                var lnt = battery.GetComponent<LocalNetworkTransform>();
                if(lnt == null) continue;

                var otherBatteryLift = FindObjectOfType<BatteryLiftController>();
                if(otherBatteryLift != null && otherBatteryLift != this)
                {
                    if(otherBatteryLift.batteryPackTransform == battery.transform ||
                       otherBatteryLift.animBatteryPackTransform == battery.transform)
                        continue;
                }

                AttachBatteryPackTransform(battery.transform);
                Debug.Log($"🎉 [BatteryLift] 배터리팩 자동 발견: {battery.gameObject.name}");

                // ★ [핵심] 배터리 발견 시 감지 트리거도 함께 탐색
                if(detectionMode == BatteryDetectionMode.Collider && batteryDetectionTrigger == null)
                {
                    SearchBatteryDetectionTrigger(battery.transform);
                }

                yield break;
            }

            yield return wait;
        }
    }

    /// <summary>
    /// ★ [핵심] 배터리의 자식에서 감지 트리거 찾기
    /// Battery 컴포넌트가 있는 오브젝트의 자식 중 Trigger Collider를 찾습니다.
    /// </summary>
    private void SearchBatteryDetectionTrigger(Transform batteryTransform = null)
    {
        // 배터리 트랜스폼이 전달되지 않으면 현재 배터리팩 사용
        if(batteryTransform == null)
        {
            if(batteryPackTransform == null)
            {
                Debug.LogWarning("[BatteryLift] 배터리팩을 찾을 수 없어 감지 트리거 탐색 실패");
                return;
            }
            batteryTransform = batteryPackTransform;
        }

        Debug.Log($"[BatteryLift] 배터리({batteryTransform.name})의 자식에서 감지 트리거 탐색 시작...");

        // ★ 배터리의 모든 자식 Collider 검사
        var allChildColliders = batteryTransform.GetComponentsInChildren<Collider>();

        foreach(var col in allChildColliders)
        {
            // Trigger이고 자식인 콜라이더만 선택
            if(!col.isTrigger) continue;

            string name = col.gameObject.name.ToLower();

            // 감지 트리거 이름 패턴 확인
            if(name.Contains("contact") || name.Contains("detect") ||
               name.Contains("zone") || name.Contains("trigger"))
            {
                batteryDetectionTrigger = col;
                Debug.Log($"🎉 [BatteryLift] 배터리 감지 트리거 자동 발견: {col.gameObject.name}");
                return;
            }
        }

        // 특정 이름 패턴이 없으면 첫 번째 Trigger Collider를 사용
        if(allChildColliders.Length > 0)
        {
            foreach(var col in allChildColliders)
            {
                if(col.isTrigger && col.gameObject != batteryTransform.gameObject)
                {
                    batteryDetectionTrigger = col;
                    Debug.Log($"🎉 [BatteryLift] 배터리 감지 트리거 자동 발견 (첫 번째): {col.gameObject.name}");
                    return;
                }
            }
        }

        Debug.LogWarning($"[BatteryLift] 배터리({batteryTransform.name})의 자식에서 Trigger Collider를 찾을 수 없습니다!");
    }

    private void AttachBatteryPackTransform(Transform batteryPack)
    {
        if(liftMode == LiftMode.TransformBased)
        {
            batteryPackTransform = batteryPack;
            _batteryOrigin = batteryPack.position;
        }
        else
        {
            animBatteryPackTransform = batteryPack;
            if(platformBone != null)
                _animBatteryOffset = batteryPack.position - platformBone.position;
        }
    }

    protected override void ApplyHeightTransform(float h)
    {
        if(liftPlate != null)
        {
            Vector3 targetLocalPos = _plateOrigin + Vector3.up * h;
            Vector3 targetWorldPos = liftPlate.parent != null
                ? liftPlate.parent.TransformPoint(targetLocalPos)
                : targetLocalPos;
            //UpdateTargetPosition(liftPlate, targetWorldPos);
        }

        if(IsBatteryAttached && batteryPackTransform != null)
        {
            Vector3 targetWorldPos = _batteryOrigin + Vector3.up * h;
            //UpdateTargetPosition(liftPlate, targetWorldPos);
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        if(!_isInitialized || liftMode != LiftMode.AnimatorBased) return;
        if(!IsBatteryAttached) return;
        if(platformBone == null || animBatteryPackTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animBatteryOffset;
        //UpdateTargetPosition(liftPlate, targetWorldPos);
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        if(!IsServerInitialized) return;
        if(IsBatteryAttached) return;
        if(_direction != 1) return;

        bool heightOk = newHeight >= contactHeight;
        bool positionOk = _isLocallyInPosition;
        bool batteryContactOk = (detectionMode == BatteryDetectionMode.Height)
            ? true
            : _isBatteryInContactZone;

        if(!heightOk && prevHeight < contactHeight && newHeight >= contactHeight)
        {
            Debug.Log($"[BatteryLift] ⬆️ 접촉 높이 도달: {newHeight:F3}m (목표: {contactHeight:F3}m)");
            Debug.Log($"[BatteryLift] 정위치 상태: {(positionOk ? "✅ 진입함" : "❌ 미진입")}");
            Debug.Log($"[BatteryLift] 배터리 감지 방식: {detectionMode}");
            if(detectionMode == BatteryDetectionMode.Collider)
                Debug.Log($"[BatteryLift] 배터리 콜라이더 접촉: {(batteryContactOk ? "✅ 접촉함" : "❌ 미접촉")}");
        }

        if(newHeight < contactHeight)
        {
            Debug.Log($"[BatteryLift] 높이 미달: {newHeight:F3}m < {contactHeight:F3}m");
            return;
        }

        if(!positionOk)
        {
            if(Time.frameCount % 120 == 0)
                Debug.LogWarning($"[BatteryLift] ⚠️ 높이는 도달({newHeight:F3}m)했으나 정위치(Zone)가 아닙니다!");
            return;
        }

        if(detectionMode == BatteryDetectionMode.Collider && !batteryContactOk)
        {
            if(Time.frameCount % 120 == 0)
                Debug.LogWarning($"[BatteryLift] ⚠️ 정위치 진입했으나 배터리 콜라이더가 감지 영역에 없습니다!");
            return;
        }

        Debug.Log($"[BatteryLift] 🎯 배터리 부착 조건 충족! 부착 시도...");
        ServerAttachBattery();
    }

    protected override void OnReachedBottom()
    {
        if(!IsServerInitialized) return;
        if(IsBatteryAttached)
        {
            Debug.Log($"[BatteryLift] 🔴 최하단 도달 → 배터리 분리");
            ServerDetachBattery();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerAttachBattery()
    {
        Debug.Log("[BatteryLift] ★ 서버: 배터리 부착 RPC 수신 및 처리 중... ★");

        if(liftMode == LiftMode.TransformBased && batteryPackTransform != null)
        {
            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;
        }
        else if(liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
        {
            _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;
        }

        _syncBatteryAttached.Value = true;
        InteractionEvents.FireZoneActivated("BatteryJack_Contact", "BatteryJack");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerDetachBattery()
    {
        _syncBatteryAttached.Value = false;
        InteractionEvents.FireZoneActivated("BatteryPack_BoltGroup", "BatteryJack");
    }

    protected override string GetHeightDisplayText()
    {
        string state = _isMoving
            ? (_direction > 0 ? "▲ 상승 중" : "▼ 하강 중")
            : (IsBatteryAttached ? "■ 배터리 지지 중" : "■ 대기");

        string heightStr = liftMode == LiftMode.TransformBased
            ? $"{_currentHeight * 100:F0}cm"
            : $"{_currentHeight * 100:F0}%";

        return $"{state}\n{heightStr}";
    }

    public void SetBatteryPack(Transform pack)
    {
        AttachBatteryPackTransform(pack);

        // ★ [추가] 수동으로 배터리팩을 설정할 때도 감지 트리거 탐색
        if(detectionMode == BatteryDetectionMode.Collider && batteryDetectionTrigger == null)
        {
            SearchBatteryDetectionTrigger(pack);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 차량 정위치 트리거 처리
    // ═══════════════════════════════════════════════════════

    private void OnTriggerEnter(Collider other)
    {
        // 배터리 감지 트리거
        if(detectionMode == BatteryDetectionMode.Collider &&
           batteryDetectionTrigger != null &&
           other == batteryDetectionTrigger)
        {
            _isBatteryInContactZone = true;
            Debug.Log($"[BatteryLift] ✅ 배터리가 감지 영역에 진입!");
            return;
        }

        // 차량 정위치 트리거
        if(other == vehiclePositionTrigger ||
           other.name.Contains("Battery_Mount") ||
           other.CompareTag("BatteryZone"))
        {
            _isLocallyInPosition = true;
            Debug.Log($"[BatteryLift] ✅ 차량 정위치 진입!");

            if(IsServerInitialized)
                ServerSetIsInPosition(true);
            else
                RequestIsInPositionServerRpc(true);

            if(_liftSyncGrab != null && _liftSyncGrab.IsGrabbed)
            {
                Debug.Log($"[BatteryLift] 🔓 배터리 리프트가 잡혀있었으므로 내려놓기 요청");
                _liftSyncGrab.RequestRelease();
            }

            if(vehiclePositionTarget != null && !_isMovingToPosition)
            {
                MoveToTargetPosition();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // 배터리 감지 트리거
        if(detectionMode == BatteryDetectionMode.Collider &&
           batteryDetectionTrigger != null &&
           other == batteryDetectionTrigger)
        {
            _isBatteryInContactZone = false;
            Debug.Log($"[BatteryLift] ❌ 배터리가 감지 영역에서 빠짐!");
            return;
        }

        // 차량 정위치 트리거
        if(other == vehiclePositionTrigger ||
           other.name.Contains("Battery_Mount") ||
           other.CompareTag("BatteryZone"))
        {
            _isLocallyInPosition = false;
            Debug.Log($"[BatteryLift] ❌ 차량 정위치 이탈");

            if(IsServerInitialized)
                ServerSetIsInPosition(false);
            else
                RequestIsInPositionServerRpc(false);
        }
    }

    private void MoveToTargetPosition()
    {
        if(_isMovingToPosition) return;
        if(vehiclePositionTarget == null) return;

        _isMovingToPosition = true;
        Debug.Log($"[BatteryLift] 📍 목표 위치로 이동 시작 (LNT 일시 비활성화)");

        if(_lnt != null)
        {
            _lnt.enabled = false;
            Debug.Log($"[BatteryLift] 🔌 LNT 비활성화");
        }

        if(_liftCollider != null)
        {
            _liftCollider.enabled = false;
            Debug.Log($"[BatteryLift] 🔌 박스 콜라이더 비활성화");
        }

        if(_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
        }

        Quaternion targetRotation = vehiclePositionTarget.rotation * Quaternion.Euler(positionReachedRotationOffset);

        Sequence seq = DOTween.Sequence();

        seq.Join(transform.DOMove(vehiclePositionTarget.position, moveToPositionDuration)
            .SetEase(moveToPositionEase));

        seq.Join(transform.DORotateQuaternion(targetRotation, moveToPositionDuration)
            .SetEase(moveToPositionEase));

        seq.OnComplete(() =>
        {
            _isMovingToPosition = false;

            if(_lnt != null)
            {
                _lnt.enabled = true;
                Debug.Log($"[BatteryLift] ⚡ LNT 재활성화");
            }

            if(_liftCollider != null)
            {
                _liftCollider.enabled = true;
                Debug.Log($"[BatteryLift] ⚡ 박스 콜라이더 재활성화");
            }

            if(_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
            }

            Debug.Log($"[BatteryLift] 📍 목표 위치 도달 및 고정 (회전 오프셋 적용: {positionReachedRotationOffset})");
        });
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestIsInPositionServerRpc(bool inPosition)
    {
        ServerSetIsInPosition(inPosition);
    }

    private void ServerSetIsInPosition(bool inPosition)
    {
        _syncIsInPosition.Value = inPosition;
    }
}
