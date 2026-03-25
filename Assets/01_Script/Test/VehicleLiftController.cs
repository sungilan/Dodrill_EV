using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using System.Collections;
using TMPro;
using DG.Tweening;

// ============================================================
//  VehicleLiftController.cs
//  2주식 차량 리프트 — 버튼으로 올리기/내리기/정지.
//
//  구조:
//    LiftRoot (이 스크립트)
//      ├── LeftPost  (왼쪽 기둥)
//      ├── RightPost (오른쪽 기둥)
//      ├── LeftArm   (왼쪽 암, 기둥에 따라 이동)
//      ├── RightArm  (오른쪽 암)
//      └── ButtonPanel (버튼 UI 패널)
//
//  차량 연동:
//    차량이 암 위에 올려져 있으면 차량도 함께 이동.
//    VehicleTransform 필드에 차량 Transform 연결.
//
//  네트워크:
//    NetworkObject 없이 Broadcast 방식으로 전체 동기화.
//    uniqueId로 씬의 여러 리프트 구분.
//
//  씬 세팅:
//    1. 리프트 루트 오브젝트에 이 스크립트 부착
//    2. leftArm, rightArm: 올라가는 암 Transform 연결
//    3. vehicleTransform: 차량 루트 Transform 연결
//    4. ButtonPanel의 버튼들을 OnUpButton(), OnDownButton(), OnStopButton()에 연결
// ============================================================

// Broadcast 구조체
public struct LiftStateBroadcast : IBroadcast
{
    public string id;
    public float  height;      // 현재 높이 (0~maxHeight)
    public bool   isMoving;
    public int    direction;   // 1=올리기, -1=내리기, 0=정지
}

public class VehicleLiftController : MonoBehaviour
{
    // ── 인스펙터 ──────────────────────────────

    [Header("식별")]
    public string uniqueId = "VehicleLift_01";

    [Header("이동 대상 Transform")]
    [Tooltip("올라가는 암(좌)")]
    public Transform leftArm;
    [Tooltip("올라가는 암(우)")]
    public Transform rightArm;
    [Tooltip("차량 루트 Transform (연결 시 함께 이동)")]
    public Transform vehicleTransform;

    [Header("리프트 설정")]
    public float maxHeight     = 2.0f;   // 최대 올라가는 높이 (m)
    public float liftSpeed     = 0.4f;   // 초당 이동 속도 (m/s)
    public float slowZone      = 0.15f;  // 상하단 근처 감속 구간

    [Header("버튼 패널 UI (선택)")]
    public TextMeshProUGUI heightDisplay;  // 현재 높이 수치 표시
    public GameObject upIndicator;         // 올라가는 중 점등
    public GameObject downIndicator;       // 내려가는 중 점등
    public AudioSource liftAudio;          // 리프트 작동음

    [Header("상태 (읽기 전용)")]
    [SerializeField] private float _currentHeight  = 0f;
    [SerializeField] private int   _direction      = 0;   // 1, -1, 0
    [SerializeField] private bool  _isMoving       = false;

    // 초기 위치 캐시
    private Vector3 _leftArmOrigin;
    private Vector3 _rightArmOrigin;
    private Vector3 _vehicleOrigin;
    private bool    _vehicleAttached = false;

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Start()
    {
        // 초기 위치 저장
        if (leftArm  != null) _leftArmOrigin  = leftArm.localPosition;
        if (rightArm != null) _rightArmOrigin = rightArm.localPosition;
        if (vehicleTransform != null) _vehicleOrigin = vehicleTransform.position;

        RegisterBroadcast();
        UpdateUI();
    }

    private void OnDestroy()
    {
        UnregisterBroadcast();
        DOTween.Kill(transform);
    }

    private void Update()
    {
        if (!_isMoving) return;
        if (!InstanceFinder.IsServerStarted) return; // 서버만 위치 계산

        float speed = CalculateSpeed();
        float delta = _direction * speed * Time.deltaTime;
        float newHeight = Mathf.Clamp(_currentHeight + delta, 0f, maxHeight);

        // 끝에 도달하면 자동 정지
        if (newHeight <= 0f || newHeight >= maxHeight)
        {
            _direction  = 0;
            _isMoving   = false;
            newHeight   = Mathf.Clamp(newHeight, 0f, maxHeight);
        }

        _currentHeight = newHeight;
        ApplyHeight(_currentHeight);
        BroadcastState();
        UpdateUI();

        ///테스트용
        if(Input.GetKeyDown(KeyCode.Z))
        {
            Debug.Log("키 입력: Z (올리기)");
            OnUpButton();
        }
        if(Input.GetKeyDown(KeyCode.X))
        {
            Debug.Log("키 입력: X (내리기)");
            OnDownButton();
        }
        if(Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("키 입력: C (멈추기)");
            OnStopButton();
        }
    }

    // ═══════════════════════════════════════════
    //  버튼 이벤트 (인스펙터 OnClick에 연결)
    // ═══════════════════════════════════════════

    /// <summary>▲ 올리기 버튼</summary>
    public void OnUpButton()
    {
        if (_currentHeight >= maxHeight) return;
        RequestMove(1);
    }

    /// <summary>▼ 내리기 버튼</summary>
    public void OnDownButton()
    {
        if (_currentHeight <= 0f) return;
        RequestMove(-1);
    }

    /// <summary>■ 정지 버튼</summary>
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
        {
            ServerSetDirection(dir);
        }
        else if (InstanceFinder.IsClientStarted)
        {
            InstanceFinder.ClientManager.Broadcast(
                new LiftStateBroadcast { id = uniqueId, direction = dir,
                                         height = _currentHeight, isMoving = dir != 0 });
        }
    }

    // ═══════════════════════════════════════════
    //  서버 로직
    // ═══════════════════════════════════════════

    private void ServerSetDirection(int dir)
    {
        _direction = dir;
        _isMoving  = dir != 0;

        if (!_isMoving)
        {
            // 정지 시 음향 중단
            if (liftAudio != null) liftAudio.Stop();
        }
        else
        {
            // 이동 시 음향 시작
            if (liftAudio != null && !liftAudio.isPlaying) liftAudio.Play();
        }

        BroadcastState();
    }

    // ═══════════════════════════════════════════
    //  네트워크 Broadcast
    // ═══════════════════════════════════════════

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<LiftStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.RegisterBroadcast<LiftStateBroadcast>(OnClientReceive);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<LiftStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<LiftStateBroadcast>(OnClientReceive);
    }

    // 서버 수신: 클라이언트 버튼 입력 → 검증 후 전파
    private void OnServerReceive(FishNet.Connection.NetworkConnection conn,
                                  LiftStateBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        ServerSetDirection(msg.direction);
    }

    // 클라이언트 수신: 서버 상태 적용
    private void OnClientReceive(LiftStateBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        if (InstanceFinder.IsServerStarted) return; // 호스트는 이미 적용됨

        _currentHeight = msg.height;
        _direction     = msg.direction;
        _isMoving      = msg.isMoving;
        ApplyHeight(_currentHeight);
        UpdateUI();
    }

    private void BroadcastState()
    {
        InstanceFinder.ServerManager?.Broadcast(
            new LiftStateBroadcast { id = uniqueId, height = _currentHeight,
                                     isMoving = _isMoving, direction = _direction });
    }

    // ═══════════════════════════════════════════
    //  물리 적용
    // ═══════════════════════════════════════════

    private void ApplyHeight(float h)
    {
        Vector3 offset = Vector3.up * h;

        if (leftArm  != null) leftArm.localPosition  = _leftArmOrigin  + offset;
        if (rightArm != null) rightArm.localPosition = _rightArmOrigin + offset;

        // 차량 함께 이동
        if (vehicleTransform != null)
            vehicleTransform.position = _vehicleOrigin + offset;
    }

    private float CalculateSpeed()
    {
        // 끝 부분에서 감속
        float distFromBottom = _currentHeight;
        float distFromTop    = maxHeight - _currentHeight;
        float minDist = Mathf.Min(distFromBottom, distFromTop);

        if (minDist < slowZone)
            return liftSpeed * Mathf.Lerp(0.25f, 1f, minDist / slowZone);

        return liftSpeed;
    }

    // ═══════════════════════════════════════════
    //  UI 업데이트
    // ═══════════════════════════════════════════

    private void UpdateUI()
    {
        if (heightDisplay != null)
            heightDisplay.text = $"{_currentHeight:F2} m";

        if (upIndicator   != null) upIndicator.SetActive(_direction > 0);
        if (downIndicator != null) downIndicator.SetActive(_direction < 0);
    }

    // ═══════════════════════════════════════════
    //  공개 유틸
    // ═══════════════════════════════════════════

    public float CurrentHeight => _currentHeight;
    public bool  IsRaised      => _currentHeight > 0.05f;

    /// <summary>외부(ScenarioRunner 등)에서 차량을 재연결할 때 호출</summary>
    public void AttachVehicle(Transform vehicle)
    {
        vehicleTransform = vehicle;
        _vehicleOrigin   = vehicle.position - Vector3.up * _currentHeight;
    }
}
