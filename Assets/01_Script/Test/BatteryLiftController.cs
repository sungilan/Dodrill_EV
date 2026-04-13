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
    public Transform batteryPackTransform;

    [Header("배터리 상태")]
    private readonly SyncVar<bool> _syncBatteryAttached = new SyncVar<bool>();
    public bool IsBatteryAttached => _syncBatteryAttached.Value;

    private Vector3 _plateOrigin;
    private Vector3 _batteryOriginWorldPos;

    protected override float GetMaxHeight() => maxHeight;

    public override void OnStartClient()
    {
        base.OnStartClient();
        _syncBatteryAttached.OnChange += OnBatteryAttachmentChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _syncBatteryAttached.OnChange -= OnBatteryAttachmentChanged;
    }

    private void OnBatteryAttachmentChanged(bool prev, bool next, bool asServer)
    {
        string status = next ? "<color=green>강제 부착됨</color>" : "<color=red>분리됨</color>";
        Debug.Log($"<color=lime>[BatteryLift]</color> 배터리 상태 동기화: {status}");
    }

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null) liftAnimator.enabled = false;
            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
        }

        Debug.Log("<color=cyan>[BatteryLift]</color> ✓ OnLiftStart: 배터리 자동 탐색 시작");

        // 배터리 자동 탐색 코루틴 시작
        StartCoroutine(AutoSearchAndAttachBattery());
    }

    private IEnumerator AutoSearchAndAttachBattery()
    {
        var wait = new WaitForSeconds(0.5f);
        Debug.Log("<color=yellow>[BatteryLift]</color> 배터리 팩 자동 탐색 코루틴 시작... | IsServer: " + IsServerInitialized);

        int attemptCount = 0;
        while(true)
        {
            attemptCount++;

            // 배터리 탐색
            var battery = Object.FindFirstObjectByType<Battery>();
            if(battery != null)
            {
                batteryPackTransform = battery.transform;
                Debug.Log($"<color=lime>[BatteryLift]</color> ✓ 배터리 발견: {battery.name}. IsServer: {IsServerInitialized}");

                // NetworkObject 가져오기
                NetworkObject batteryNetObj = battery.GetComponent<NetworkObject>();
                if(batteryNetObj != null)
                {
                    Debug.Log($"<color=yellow>[BatteryLift]</color> NetworkObject 발견. ServerAttachBatteryRpc() 호출 중...");
                    ServerAttachBatteryRpc(batteryNetObj);
                }
                else
                {
                    Debug.LogError($"<color=red>[BatteryLift]</color> 배터리 Transform에 NetworkObject가 없습니다!");
                }

                yield break;
            }

            Debug.Log($"<color=yellow>[BatteryLift]</color> 배터리 탐색 시도 #{attemptCount}: 아직 찾지 못함");
            yield return wait;
        }
    }

    protected override void ApplyHeightTransform(float h)
    {
        // 1. 리프트 상판 이동 (로컬 Y축)
        if(liftPlate != null)
        {
            liftPlate.localPosition = _plateOrigin + Vector3.up * h;
        }

        // 2. 부착된 배터리는 부모(liftPlate)를 따라가므로 별도 이동 불필요
    }

    protected override void SyncPayloadLateUpdate()
    {
        // 필요시 로직 추가
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        // 자식 부착 방식이므로 별도 처리 불필요
    }

    protected override void OnReachedBottom()
    {
        if(IsServerInitialized && IsBatteryAttached)
        {
            Debug.Log("<color=orange>[BatteryLift]</color> 최하단 도달: 배터리 분리");
            ServerDetachBattery();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerAttachBatteryRpc(NetworkObject batteryNetObj)
    {
        Debug.Log($"<color=cyan>[BatteryLift]</color> ServerAttachBatteryRpc() 서버 실행됨");

        if(batteryNetObj == null)
        {
            Debug.LogError("[BatteryLift] ServerAttachBatteryRpc: batteryNetObj가 null입니다!");
            return;
        }

        Transform batteryTransform = batteryNetObj.transform;
        Debug.Log($"<color=green>[BatteryLift]</color> 서버에서 배터리 수신: {batteryTransform.name}");

        // 현재 월드 위치 저장
        _batteryOriginWorldPos = batteryTransform.position;
        Debug.Log($"<color=green>[BatteryLift]</color> 배터리 월드 위치 저장: {_batteryOriginWorldPos}");

        // 물리 엔진 비활성화
        var rb = batteryTransform.GetComponent<Rigidbody>();
        if(rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log($"<color=green>[BatteryLift]</color> ✓ Rigidbody isKinematic = true");
        }

        // LNT 비활성화
        var lnt = batteryTransform.GetComponent<LocalNetworkTransform>();
        if(lnt != null)
        {
            lnt.enabled = false;
            Debug.Log($"<color=green>[BatteryLift]</color> ✓ LocalNetworkTransform 비활성화");
        }

        // 자식으로 설정
        batteryTransform.SetParent(liftPlate);
        Debug.Log($"<color=green>[BatteryLift]</color> ✓ 배터리를 liftPlate의 자식으로 설정 | Parent: {batteryTransform.parent.name}");

        // 로컬 위치 조정 (월드 좌표 유지)
        batteryTransform.localPosition = liftPlate.InverseTransformPoint(_batteryOriginWorldPos);
        Debug.Log($"<color=green>[BatteryLift]</color> ✓ 배터리 로컬 위치 설정: {batteryTransform.localPosition}");

        // 로컬 변수에도 저장
        batteryPackTransform = batteryTransform;

        _syncBatteryAttached.Value = true;
        InteractionEvents.FireZoneActivated("BatteryJack_Attached", "BatteryJack");

        Debug.Log($"<color=lime>[BatteryLift]</color> ★ 배터리 부착 완료!");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerDetachBattery()
    {
        Debug.Log($"<color=orange>[BatteryLift]</color> ServerDetachBattery() 실행");

        if(batteryPackTransform == null)
        {
            Debug.LogWarning("[BatteryLift] ServerDetachBattery: 배터리가 null입니다");
            return;
        }

        // 월드 위치 저장
        Vector3 detachWorldPos = batteryPackTransform.position;
        Debug.Log($"<color=orange>[BatteryLift]</color> 배터리 분리 월드 위치: {detachWorldPos}");

        // 부모 해제
        batteryPackTransform.SetParent(null);
        Debug.Log($"<color=orange>[BatteryLift]</color> ✓ 배터리 부모 해제");

        // 월드 위치 복원
        batteryPackTransform.position = detachWorldPos;

        // 물리 다시 활성화
        var rb = batteryPackTransform.GetComponent<Rigidbody>();
        if(rb != null)
        {
            rb.isKinematic = false;
            Debug.Log($"<color=orange>[BatteryLift]</color> ✓ Rigidbody isKinematic = false");
        }

        // LNT 다시 활성화
        var lnt = batteryPackTransform.GetComponent<LocalNetworkTransform>();
        if(lnt != null)
        {
            lnt.enabled = true;
            Debug.Log($"<color=orange>[BatteryLift]</color> ✓ LocalNetworkTransform 활성화");
        }

        _syncBatteryAttached.Value = false;
        InteractionEvents.FireZoneActivated("BatteryJack_Detached", "BatteryJack");
    }
}
