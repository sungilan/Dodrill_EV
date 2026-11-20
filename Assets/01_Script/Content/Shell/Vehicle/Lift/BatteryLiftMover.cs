using System.Collections;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  BatteryLiftMover.cs
//  배터리 잭 자체 이동 — PC/Mobile/VR 통합
//
//  BatteryLiftController와 같은 루트 GO에 부착.
//  이동은 반드시 LNT(LocalNetworkTransform)를 통해 동기화.
//
//  씬 세팅:
//    BatteryJack (BatteryLiftController + BatteryLiftMover)
//      └── LNT 컴포넌트 필수 (ownerSendOnly = false)
//
//  PC:   MoveInput(Vector2) 호출 — FreeLookController에서 특정 키
//  Mobile: 이동 전용 조이스틱 → MoveInput(Vector2)
//  VR:   SyncGrab으로 잡고 직접 밀기 — 별도 코드 없음
// ============================================================

// ── 이동 요청 Broadcast ──────────────────────────────────────
public struct LiftMoveRequestBroadcast : IBroadcast
{
    public string  id;
    public Vector3 delta;   // 클라이언트가 요청한 이동량 (월드)
}

// ── 위치 동기화 Broadcast ────────────────────────────────────
public struct LiftPositionBroadcast : IBroadcast
{
    public string  id;
    public Vector3 position;
    public float   timestamp;
}

public class BatteryLiftMover : MonoBehaviour
{
    [Header("식별 (BatteryLiftController.uniqueId와 동일)")]
    public string uniqueId = "BatteryJack_01";

    [Header("이동 설정")]
    [Tooltip("수평 이동 속도 (m/s)")]
    public float moveSpeed = 1.5f;

    [Tooltip("이동 가능 범위 (잭 기준 중심에서 반경, m). 0 = 무제한")]
    public float moveRadius = 3f;

    [Header("LNT 참조 (자동 탐색)")]
    public LocalNetworkTransform lnt;

    // ── 내부 상태 ─────────────────────────────────────────────
    private Vector2 _moveInput;
    private bool    _isMoving;
    private Vector3 _origin;          // 스폰 기준 위치 (범위 제한용)

    // ── 생명주기 ──────────────────────────────────────────────

    private void Awake()
    {
        if (lnt == null) lnt = GetComponent<LocalNetworkTransform>();
        if (lnt == null) Debug.LogWarning("[BatteryLiftMover] LNT 없음 — 이동 동기화 불가");
    }

    private void Start()
    {
        _origin = transform.position;
        RegisterBroadcast();
    }

    private void OnDestroy()
    {
        UnregisterBroadcast();
    }

    private void Update()
    {
        if (!_isMoving || _moveInput.sqrMagnitude < 0.001f) return;

        // 클라이언트 로컬 예측 이동 (LNT가 서버 보정)
        Vector3 dir = new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;
        Vector3 delta = dir * moveSpeed * Time.deltaTime;

        // 범위 제한
        Vector3 next = transform.position + delta;
        if (moveRadius > 0f &&
            Vector3.Distance(new Vector3(next.x, _origin.y, next.z),
                             new Vector3(_origin.x, _origin.y, _origin.z)) > moveRadius)
            return;

        // LNT로 이동 요청
        if (lnt != null)
            lnt.SetTargetPosition(next, transform.rotation);

        // 서버에 delta 전송
        SendMoveRequest(delta);
    }

    // ── Broadcast 등록 ────────────────────────────────────────

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<LiftMoveRequestBroadcast>(OnServerReceiveMove);
        InstanceFinder.ClientManager?.RegisterBroadcast<LiftPositionBroadcast>(OnClientReceivePosition);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<LiftMoveRequestBroadcast>(OnServerReceiveMove);
        InstanceFinder.ClientManager?.UnregisterBroadcast<LiftPositionBroadcast>(OnClientReceivePosition);
    }

    // ── 서버: 이동 요청 수신 → 위치 계산 → 전파 ──────────────

    private void OnServerReceiveMove(FishNet.Connection.NetworkConnection conn,
                                     LiftMoveRequestBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;

        Vector3 next = transform.position + msg.delta;

        // 서버에서 범위 제한
        if (moveRadius > 0f &&
            Vector3.Distance(new Vector3(next.x, _origin.y, next.z),
                             new Vector3(_origin.x, _origin.y, _origin.z)) > moveRadius)
            return;

        // 서버 위치 갱신
        transform.position = next;

        // 전체 클라이언트에 위치 전파
        InstanceFinder.ServerManager?.Broadcast(
            new LiftPositionBroadcast
            {
                id        = uniqueId,
                position  = transform.position,
                timestamp = Time.time
            });
    }

    // ── 클라이언트: 위치 수신 → LNT로 보정 ───────────────────

    private void OnClientReceivePosition(LiftPositionBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        if (InstanceFinder.IsServerStarted) return; // 호스트는 이미 처리

        if (lnt != null)
            lnt.SetTargetPosition(msg.position, transform.rotation);
        else
            transform.position = msg.position;
    }

    // ── 이동 요청 전송 ────────────────────────────────────────

    private void SendMoveRequest(Vector3 delta)
    {
        if (InstanceFinder.IsServerStarted)
        {
            // 호스트: 직접 처리
            transform.position += delta;
            InstanceFinder.ServerManager?.Broadcast(
                new LiftPositionBroadcast
                {
                    id        = uniqueId,
                    position  = transform.position,
                    timestamp = Time.time
                });
        }
        else
        {
            InstanceFinder.ClientManager?.Broadcast(
                new LiftMoveRequestBroadcast { id = uniqueId, delta = delta });
        }
    }

    // ── 외부 API ──────────────────────────────────────────────

    /// <summary>
    /// PC/Mobile: FreeLookController 또는 Mobile 조이스틱에서 호출.
    /// input.x = 좌우, input.y = 앞뒤
    /// </summary>
    public void MoveInput(Vector2 input)
    {
        _moveInput = input;
        _isMoving  = input.sqrMagnitude > 0.01f;
    }

    /// <summary>이동 중단</summary>
    public void StopMove() => MoveInput(Vector2.zero);

    /// <summary>기준 위치 재설정 (스폰 위치 변경 시)</summary>
    public void ResetOrigin() => _origin = transform.position;

    public bool IsMoving => _isMoving;
}
