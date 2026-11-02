using FishNet.Connection;
using FishNet.Object;
using System.Collections;
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

    // ── Anchor 처리 ──
    // Anchor(태그 'Anchor')가 아직 로드되지 않았거나 씬에 없으면 예전에는 동기화 전체가 '조용히' 멈췄다
    // (접속은 되는데 서로 안 보이거나 안 움직임). 이제는 ① 0.5초 간격으로 재탐색하고
    // ② AnchorWaitSeconds 가 지나도 없으면 경고를 한 번 남기고 월드 좌표(앵커 = 원점)로 동기화를 계속한다.
    //    (Anchor 가 없는 씬에서는 모든 피어가 같은 방식으로 폴백하므로 서로 좌표가 일치한다.)
    private const float AnchorWaitSeconds = 3f;
    private const float AnchorSearchInterval = 0.5f;
    private float _anchorWaitStart = -1f;
    private float _nextAnchorSearch;
    private bool _anchorWarned;

    private bool AnchorReady => anchor != null || (_anchorWaitStart >= 0f && Time.time - _anchorWaitStart > AnchorWaitSeconds);
    private Vector3 AnchorPos => anchor != null ? anchor.position : Vector3.zero;
    private Quaternion AnchorRot => anchor != null ? anchor.rotation : Quaternion.identity;

    private void TryFindAnchor()
    {
        if(anchor != null) return;
        if(_anchorWaitStart < 0f) _anchorWaitStart = Time.time;
        if(Time.time < _nextAnchorSearch) return;
        _nextAnchorSearch = Time.time + AnchorSearchInterval;

        GameObject anchorObj = GameObject.FindGameObjectWithTag("Anchor");
        if(anchorObj != null)
        {
            anchor = anchorObj.transform;
            return;
        }

        if(!_anchorWarned && Time.time - _anchorWaitStart > AnchorWaitSeconds)
        {
            _anchorWarned = true;
            Debug.LogWarning($"[LocalNetworkTransform] '{name}': 태그 'Anchor' 오브젝트를 {AnchorWaitSeconds:F0}초 동안 찾지 못했습니다. 월드 좌표로 동기화를 계속합니다. (Game 씬의 AnchoringObj 태그를 확인하세요)");
        }
    }

    private void Update()
    {
        TryFindAnchor();
        if(!AnchorReady)
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
                Vector3 localPos = Quaternion.Inverse(AnchorRot) * (transform.position - AnchorPos);
                Quaternion localRot = Quaternion.Inverse(AnchorRot) * transform.rotation;

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
                Vector3 targetPos = AnchorPos + AnchorRot * networkLocalPos;
                Quaternion targetRot = AnchorRot * networkLocalRot;

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
        if(!AnchorReady) return;

        Vector3 localPos = Quaternion.Inverse(AnchorRot) * (transform.position - AnchorPos);
        Quaternion localRot = Quaternion.Inverse(AnchorRot) * transform.rotation;

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
        if(!IsServerInitialized && AnchorReady)
        {
            Vector3 localPos = Quaternion.Inverse(AnchorRot) * (transform.position - AnchorPos);
            Quaternion localRot = Quaternion.Inverse(AnchorRot) * transform.rotation;

            lastSentPos = localPos;
            lastSentRot = localRot;
            networkLocalPos = localPos;
            networkLocalRot = localRot;

            SendLocalTransformServerRpc(localPos, localRot);
        }
        // 대신 코루틴을 통해 안전하게 전송합니다.
        StartCoroutine(SafeInitialSync());
    }
    private IEnumerator SafeInitialSync()
    {
        // 소유권이 완전히 물리적으로 내부 반영될 때까지 한 프레임 대기
        yield return null;

        if(IsOwner && AnchorReady)
        {
            Vector3 localPos = Quaternion.Inverse(AnchorRot) * (transform.position - AnchorPos);
            Quaternion localRot = Quaternion.Inverse(AnchorRot) * transform.rotation;

            lastSentPos = localPos;
            lastSentRot = localRot;

            // 이제 IsOwner가 확실하므로 에러가 발생하지 않습니다.
            SendLocalTransformServerRpc(localPos, localRot);
        }
    }

    #region RPCs
    [ServerRpc(RequireOwnership = true)]
    private void SendLocalTransformServerRpc(Vector3 localPos, Quaternion localRot)
    {
        // 서버도 클라이언트와 같은 규칙(앵커 없으면 월드 좌표)으로 해석해야 좌표가 일치한다
        TryFindAnchor();

        // 서버 transform 적용
        Vector3 worldPos = AnchorPos + AnchorRot * localPos;
        Quaternion worldRot = AnchorRot * localRot;

        transform.position = worldPos;
        transform.rotation = worldRot;

        // 서버 기준 로컬 변환 업데이트
        networkLocalPos = Quaternion.Inverse(AnchorRot) * (transform.position - AnchorPos);
        networkLocalRot = Quaternion.Inverse(AnchorRot) * transform.rotation;

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
        if(!AnchorReady) return;

        Vector3 target = AnchorPos + AnchorRot * networkLocalPos;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, target);
        Gizmos.DrawSphere(target, 0.02f);
    }
#endif
}