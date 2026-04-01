using UnityEngine;
using UnityEngine.XR;

// ============================================================
//  InventoryCanvasAdapter.cs (수정본)
//  PC/Mobile -> Screen Space Overlay
//  VR         -> World Space (플레이어를 부드럽게 따라다님)
// ============================================================
[RequireComponent(typeof(Canvas))]
public class InventoryCanvasAdapter : MonoBehaviour
{
    [Header("VR Follow 설정")]
    public float vrDistance = 1.2f;      // 플레이어 정면 거리
    public float vrHeightOffset = -0.2f; // 눈높이 보정
    public float followSpeed = 5f;       // 따라오는 속도 (낮을수록 쫀득하고 부드러움)
    public float vrCanvasScale = 0.001f;

    [Header("참조")]
    public Transform vrAnchor;           // VR 카메라 (Main Camera)

    private Canvas _canvas;
    private bool _isVR;
    private bool _isPanelActive;         // 패널 활성화 상태 체크

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _isVR = IsVRActive();
        Apply();
    }

    private void Apply()
    {
        if(_isVR)
            SetupWorldSpace();
        else
            SetupOverlay();
    }

    private void SetupOverlay()
    {
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        transform.localScale = Vector3.one;
    }

    private void SetupWorldSpace()
    {
        _canvas.renderMode = RenderMode.WorldSpace;
        // 월드 스페이스 캔버스는 이벤트 카메라가 할당되어야 레이캐스트(클릭)가 잘 작동합니다.
        _canvas.worldCamera = Camera.main;
        transform.localScale = Vector3.one * vrCanvasScale;
    }

    // ── 핵심: 부드럽게 따라오는 로직 ──────────────────────

    private void LateUpdate()
    {
        // VR이고 패널이 켜져 있을 때만 따라오기 수행
        if(_isVR && _isPanelActive)
        {
            UpdateVRPosition(false); // 부드럽게 따라가기
        }
    }

    private void UpdateVRPosition(bool instant)
    {
        Transform cam = vrAnchor != null ? vrAnchor : Camera.main?.transform;
        if(cam == null) return;

        // 1. 목표 위치 계산 (수평 방향 유지)
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        if(forward == Vector3.zero) forward = cam.forward;

        Vector3 targetPos = cam.position + (forward * vrDistance) + (Vector3.up * vrHeightOffset);

        // 2. 플레이어를 바라보는 회전값 (빌보드 효과)
        Quaternion targetRot = Quaternion.LookRotation(transform.position - cam.position);

        if(instant)
        {
            // 열리는 순간에는 즉시 이동
            transform.position = targetPos;
            transform.rotation = targetRot;
        }
        else
        {
            // 실행 중에는 Lerp/Slerp로 부드럽게 추종
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * followSpeed);
        }
    }

    // ── 외부 연동 (EVInventoryUI에서 호출) ──────────────

    public void OnPanelOpened(bool isActive)
    {
        _isPanelActive = isActive;

        if(_isVR && _isPanelActive)
        {
            // 패널이 켜지는 순간에는 즉시 플레이어 앞으로 "순간이동" 시킴
            UpdateVRPosition(true);
        }
    }

    private static bool IsVRActive()
    {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
        return XRSettings.isDeviceActive;
#else
        return false;
#endif
    }
}