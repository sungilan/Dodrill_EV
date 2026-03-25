using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using System.Collections;
using TMPro;
using DG.Tweening;

// ============================================================
//  BatteryLiftController.cs
//  배터리팩 전용 잭(초록 배터리 리프트).
//
//  역할:
//    - 배터리팩 하부에 밀착 → 상승 → 배터리팩 지지
//    - 하강 → 배터리팩 분리
//    - TrainingFlowManager / InteractionEvents 와 연동
//      BatteryPack_Disassemble, BatteryPack_Assemble Task 완료 판정
//
//  구조:
//    BatteryLiftRoot (이 스크립트)
//      ├── LiftPlate  — 실제 올라가는 플랫폼
//      └── ButtonPanel
//            ├── UpButton    → OnUpButton()
//            ├── DownButton  → OnDownButton()
//            └── StopButton  → OnStopButton()
//
//  배터리팩 연동:
//    batteryPackTransform 에 배터리팩 Transform 연결.
//    잭이 배터리 접촉 높이에 도달하면 자동으로 배터리팩과 연결됨.
//
//  씬 세팅:
//    1. 배터리 잭 루트에 BatteryLiftController 부착
//    2. liftPlate: 올라가는 플랫폼 Transform
//    3. batteryPackTransform: 배터리팩 Transform (BatteryPack_BoltGroup 오브젝트)
//    4. contactHeight: 잭이 배터리팩에 닿는 높이 (m)
//    5. 버튼 OnClick 연결
// ============================================================

// 배터리 리프트 Broadcast (VehicleLift와 별도 구조체)
public struct BatteryLiftBroadcast : IBroadcast
{
    public string id;
    public float  height;
    public bool   isMoving;
    public int    direction;
    public bool   isBatteryAttached;
}

public class BatteryLiftController : MonoBehaviour
{
    // ── 인스펙터 ──────────────────────────────

    [Header("식별")]
    public string uniqueId = "BatteryLift_01";

    [Header("이동 Transform")]
    [Tooltip("올라가는 플랫폼")]
    public Transform liftPlate;
    [Tooltip("배터리팩 오브젝트 (탈거/조립 시 함께 이동)")]
    public Transform batteryPackTransform;

    [Header("리프트 설정")]
    public float maxHeight     = 0.5f;    // 최대 높이 (m) — 배터리팩 여유분
    public float liftSpeed     = 0.15f;   // 속도 (m/s)
    public float contactHeight = 0.08f;   // 이 높이에서 배터리팩과 접촉

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public AudioSource     liftAudio;

    [Header("상태 (읽기 전용)")]
    [SerializeField] private float _currentHeight      = 0f;
    [SerializeField] private int   _direction          = 0;
    [SerializeField] private bool  _isMoving           = false;
    [SerializeField] private bool  _isBatteryAttached  = false;

    // 초기 위치 캐시
    private Vector3 _plateOrigin;
    private Vector3 _batteryOrigin;

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Start()
    {
        if (liftPlate != null)
            _plateOrigin = liftPlate.localPosition;

        if (batteryPackTransform != null)
            _batteryOrigin = batteryPackTransform.position;

        RegisterBroadcast();
        UpdateUI();
    }

    private void OnDestroy()
    {
        UnregisterBroadcast();
    }

    private void Update()
    {
        if (!_isMoving) return;
        if (!InstanceFinder.IsServerStarted) return;

        float delta     = _direction * liftSpeed * Time.deltaTime;
        float newHeight = Mathf.Clamp(_currentHeight + delta, 0f, maxHeight);

        // 끝 도달 시 정지
        if (newHeight <= 0f || newHeight >= maxHeight)
        {
            _direction = 0;
            _isMoving  = false;
            newHeight  = Mathf.Clamp(newHeight, 0f, maxHeight);

            if (liftAudio != null) liftAudio.Stop();

            // 완전히 내려왔을 때 배터리팩 분리
            if (newHeight <= 0f && _isBatteryAttached)
                DetachBattery();
        }

        _currentHeight = newHeight;
        ApplyHeight(_currentHeight);
        CheckBatteryContact();
        BroadcastState();
        UpdateUI();
    }

    // ═══════════════════════════════════════════
    //  버튼 이벤트
    // ═══════════════════════════════════════════

    public void OnUpButton()
    {
        if (_currentHeight >= maxHeight) return;
        RequestMove(1);
    }

    public void OnDownButton()
    {
        if (_currentHeight <= 0f) return;
        RequestMove(-1);
    }

    public void OnStopButton()
    {
        RequestMove(0);
    }

    // ═══════════════════════════════════════════
    //  요청 전송
    // ═══════════════════════════════════════════

    private void RequestMove(int dir)
    {
        if (InstanceFinder.IsServerStarted)
            ServerSetDirection(dir);
        else if (InstanceFinder.IsClientStarted)
            InstanceFinder.ClientManager.Broadcast(
                new BatteryLiftBroadcast { id = uniqueId, direction = dir });
    }

    // ═══════════════════════════════════════════
    //  서버 로직
    // ═══════════════════════════════════════════

    private void ServerSetDirection(int dir)
    {
        _direction = dir;
        _isMoving  = dir != 0;

        if (_isMoving && liftAudio != null && !liftAudio.isPlaying)
            liftAudio.Play();
        if (!_isMoving && liftAudio != null)
            liftAudio.Stop();

        BroadcastState();
    }

    /// <summary>
    /// 잭이 배터리팩 접촉 높이에 도달하면 자동 연결.
    /// 올리는 방향일 때만 체크.
    /// </summary>
    private void CheckBatteryContact()
    {
        if (_isBatteryAttached) return;
        if (_direction != 1) return;
        if (_currentHeight < contactHeight) return;

        AttachBattery();
    }

    private void AttachBattery()
    {
        _isBatteryAttached = true;
        if (batteryPackTransform != null)
            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;

        Debug.Log("[BatteryLift] 배터리팩 연결됨");

        // TrainingFlowManager에 잭 상승 완료 보고
        // (BatteryPack_Disassemble Task의 일부 조건)
        InteractionEvents.FireZoneActivated("BatteryJack_Contact", "BatteryJack");
    }

    private void DetachBattery()
    {
        _isBatteryAttached = false;
        Debug.Log("[BatteryLift] 배터리팩 분리됨");

        // 배터리팩이 내려온 후 분해 완료 보고
        InteractionEvents.FireZoneActivated("BatteryPack_BoltGroup", "BatteryJack");
    }

    // ═══════════════════════════════════════════
    //  네트워크
    // ═══════════════════════════════════════════

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<BatteryLiftBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.RegisterBroadcast<BatteryLiftBroadcast>(OnClientReceive);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<BatteryLiftBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<BatteryLiftBroadcast>(OnClientReceive);
    }

    private void OnServerReceive(FishNet.Connection.NetworkConnection conn,
                                  BatteryLiftBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        ServerSetDirection(msg.direction);
    }

    private void OnClientReceive(BatteryLiftBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        if (InstanceFinder.IsServerStarted) return;

        _currentHeight     = msg.height;
        _direction         = msg.direction;
        _isMoving          = msg.isMoving;
        _isBatteryAttached = msg.isBatteryAttached;
        ApplyHeight(_currentHeight);
        UpdateUI();
    }

    private void BroadcastState()
    {
        InstanceFinder.ServerManager?.Broadcast(new BatteryLiftBroadcast
        {
            id = uniqueId, height = _currentHeight,
            isMoving = _isMoving, direction = _direction,
            isBatteryAttached = _isBatteryAttached
        });
    }

    // ═══════════════════════════════════════════
    //  물리 적용
    // ═══════════════════════════════════════════

    private void ApplyHeight(float h)
    {
        if (liftPlate != null)
            liftPlate.localPosition = _plateOrigin + Vector3.up * h;

        // 배터리팩 연결 시 함께 이동
        if (_isBatteryAttached && batteryPackTransform != null)
            batteryPackTransform.position = _batteryOrigin + Vector3.up * h;
    }

    // ═══════════════════════════════════════════
    //  UI
    // ═══════════════════════════════════════════

    private void UpdateUI()
    {
        if (statusText == null) return;

        string state = _isMoving
            ? (_direction > 0 ? "▲ 상승 중" : "▼ 하강 중")
            : (_isBatteryAttached ? "■ 배터리 지지 중" : "■ 대기");

        statusText.text = $"{state}\n{_currentHeight * 100:F0}cm";
    }

    // ═══════════════════════════════════════════
    //  공개 유틸
    // ═══════════════════════════════════════════

    public float CurrentHeight      => _currentHeight;
    public bool  IsBatteryAttached  => _isBatteryAttached;

    /// <summary>배터리팩 오브젝트를 외부에서 연결 (ScenarioSpawner 스폰 후 호출)</summary>
    public void SetBatteryPack(Transform pack)
    {
        batteryPackTransform = pack;
        _batteryOrigin = pack.position;
    }

    /// <summary>디버그: 즉시 최대 높이로 이동</summary>
    public void DebugRaiseImmediate()
    {
        _currentHeight     = maxHeight;
        _isBatteryAttached = true;
        _isMoving          = false;
        ApplyHeight(_currentHeight);
        BroadcastState();
    }

    /// <summary>디버그: 즉시 원위치</summary>
    public void DebugLowerImmediate()
    {
        _currentHeight     = 0f;
        _isBatteryAttached = false;
        _isMoving          = false;
        ApplyHeight(_currentHeight);
        BroadcastState();
    }
}
