using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

public class LocalNetworkTransform : NetworkBehaviour
{
    [Header("References")]
    public Transform anchor; // 기준 앵커

    [Header("Sync Settings")]
    [Tooltip("위치/회전 정보를 전송하는 주기 (초)")]
    public float syncInterval = 0.025f;

    [Tooltip("위치 보간 속도")]
    public float lerpSpeed = 12f;

    [Tooltip("회전 보간 속도")]
    public float rotationLerpSpeed = 12f;

    [Header("Thresholds")]
    public float positionThreshold = 0.001f;
    public float rotationThreshold = 0.5f; // degrees

    [Header("Teleport Settings")]
    public float teleportDistanceThreshold = 2f; // 이 거리 이상이면 순간이동

    private float lastSyncTime;
    private Vector3 networkLocalPos;
    private Quaternion networkLocalRot;
    private Vector3 lastSentPos;
    private Quaternion lastSentRot;
    private bool hasReceivedNetworkTransform = false;

    public bool sendAuthorityInServer = true;

    [Tooltip("true: Owner는 서버 전송만, Observer 보간 수신 없음")]
    public bool ownerSendOnly = false;

    private bool _hasExternalTarget;
    private Vector3 _targetPos;
    private Quaternion _targetRot;

    public void SetTargetPosition(Vector3 worldPos, Quaternion worldRot)
    {
        _hasExternalTarget = true;
        _targetPos = worldPos;
        _targetRot = worldRot;
    }

    public void ClearTargetPosition()
    {
        _hasExternalTarget = false;
    }


    private void Awake()
    {
        TryFindAnchor();
    }

    private void TryFindAnchor()
    {
        if(anchor == null)
        {
            GameObject anchorObj = GameObject.FindGameObjectWithTag("Anchor");
            if(anchorObj != null)
                anchor = anchorObj.transform;
        }
    }

    private void Update()
    {
        TryFindAnchor();
        if(anchor == null)
            return;

        float deltaTime = Time.deltaTime;

        // ── 외부 목표 주입 모드 (SyncGrab holdPoint 추적) ──
        if(_hasExternalTarget && IsOwner)
        {
            transform.position = Vector3.Lerp(transform.position, _targetPos, lerpSpeed * deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, rotationLerpSpeed * deltaTime);
            if(Time.time - lastSyncTime > syncInterval)
            {
                SendTransformIfNeeded();
                lastSyncTime = Time.time;
            }
            return;
        }

        if(IsServerInitialized)
        {
            // 서버 authority
            if(Time.time - lastSyncTime > syncInterval)
            {
                Vector3 localPos = Quaternion.Inverse(anchor.rotation) * (transform.position - anchor.position);
                Quaternion localRot = Quaternion.Inverse(anchor.rotation) * transform.rotation;

                if((localPos - lastSentPos).sqrMagnitude > positionThreshold * positionThreshold ||
                    Quaternion.Angle(localRot, lastSentRot) > rotationThreshold)
                {
                    lastSentPos = localPos;
                    lastSentRot = localRot;
                    UpdateLocalTransformObserversRpc(localPos, localRot);
                }

                lastSyncTime = Time.time;
            }
        }
        else
        {
            // Owner → Server 전송
            if(IsOwner && Time.time - lastSyncTime > syncInterval)
            {
                SendTransformIfNeeded();
            }

            // Observer 보간
            if(!IsOwner && hasReceivedNetworkTransform && !ownerSendOnly)
            {
                Vector3 targetPos = anchor.position + anchor.rotation * networkLocalPos;
                Quaternion targetRot = anchor.rotation * networkLocalRot;

                float distance = Vector3.Distance(transform.position, targetPos);

                // 순간이동 처리
                if(distance > teleportDistanceThreshold)
                {
                    transform.position = targetPos;
                    transform.rotation = targetRot;
                }
                else
                {
                    // 보간 비율 계산 (지연 상황에서도 부드럽게)
                    float t = Mathf.Clamp01(lerpSpeed * deltaTime);
                    float r = Mathf.Clamp01(rotationLerpSpeed * deltaTime);

                    transform.position = Vector3.Lerp(transform.position, targetPos, t);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, r);

                    // 근접 시 스냅 보정 (미세 오차 제거)
                    if(Vector3.Distance(transform.position, targetPos) < 0.001f)
                        transform.position = targetPos;
                    if(Quaternion.Angle(transform.rotation, targetRot) < 0.1f)
                        transform.rotation = targetRot;
                }
            }
        }
    }

    private void SendTransformIfNeeded()
    {
        if(anchor == null) return;

        Vector3 localPos = Quaternion.Inverse(anchor.rotation) * (transform.position - anchor.position);
        Quaternion localRot = Quaternion.Inverse(anchor.rotation) * transform.rotation;

        if((localPos - lastSentPos).sqrMagnitude > positionThreshold * positionThreshold ||
            Quaternion.Angle(localRot, lastSentRot) > rotationThreshold)
        {
            SendLocalTransformServerRpc(localPos, localRot);

            lastSentPos = localPos;
            lastSentRot = localRot;
        }

        lastSyncTime = Time.time;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        TryFindAnchor();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        TryFindAnchor();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        if(!IsServerInitialized && anchor != null)
        {
            Vector3 localPos = Quaternion.Inverse(anchor.rotation) * (transform.position - anchor.position);
            Quaternion localRot = Quaternion.Inverse(anchor.rotation) * transform.rotation;

            lastSentPos = localPos;
            lastSentRot = localRot;
            networkLocalPos = localPos;
            networkLocalRot = localRot;

            SendLocalTransformServerRpc(localPos, localRot);
        }
    }

    #region RPCs
    [ServerRpc]
    private void SendLocalTransformServerRpc(Vector3 localPos, Quaternion localRot)
    {
        if(anchor == null)
        {
            Debug.LogWarning($"[{name}] Anchor is null in ServerRpc");
            return;
        }

        // 서버 transform 적용
        Vector3 worldPos = anchor.position + anchor.rotation * localPos;
        Quaternion worldRot = anchor.rotation * localRot;

        transform.position = worldPos;
        transform.rotation = worldRot;

        // 서버 기준 로컬 변환 업데이트
        networkLocalPos = Quaternion.Inverse(anchor.rotation) * (transform.position - anchor.position);
        networkLocalRot = Quaternion.Inverse(anchor.rotation) * transform.rotation;

        lastSentPos = networkLocalPos;
        lastSentRot = networkLocalRot;

        UpdateLocalTransformObserversRpc(networkLocalPos, networkLocalRot);
    }

    [ObserversRpc(BufferLast = true)]
    private void UpdateLocalTransformObserversRpc(Vector3 localPos, Quaternion localRot)
    {
        if(IsOwner) return;

        networkLocalPos = localPos;
        networkLocalRot = localRot;
        hasReceivedNetworkTransform = true;
    }
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if(anchor == null) return;

        Vector3 target = anchor.position + anchor.rotation * networkLocalPos;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, target);
        Gizmos.DrawSphere(target, 0.02f);
    }
#endif
}