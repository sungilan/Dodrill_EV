// ============================================================

//  GameScenePlatformManager.cs

//  게임씬 전용 플랫폼 매니저.

//  PlatformDetector(클라이언트 씬)와 동일한 역할을 게임씬에서 수행.

//

//  씬 배치: 게임씬 빈 GO에 부착 (_PlatformManager 등)

//  Inspector에서 플랫폼별 항목 연결:

//    - canvases       : 인벤토리 등 Canvas 목록 (renderMode 자동 설정)

//    - pcOnlyObjects  : PC에서만 활성화할 GO (크로스헤어 등)

//    - mobileOnlyObjects : 모바일에서만 활성화할 GO (조이스틱 UI 등)

//    - vrOnlyObjects  : VR에서만 활성화할 GO (VR 전용 UI 등)

//    - vrWorldSpaceCanvasDistance : VR Canvas 배치 거리

// ============================================================

using UnityEngine;
using XRAirpotrSecurity;

public class GameScenePlatformManager : MonoBehaviour

{

    public static GameScenePlatformManager Instance { get; private set; }

    public static PlatformType CurrentPlatform { get; private set; }



    [Header("Canvas 목록 (renderMode 자동 설정)")]

    [Tooltip("게임씬의 모든 UI Canvas. VR=WorldSpace, PC/Mobile=Overlay로 자동 전환")]

    public Canvas[] canvases;



    [Header("플랫폼별 전용 오브젝트")]

    [Tooltip("PC에서만 켤 GO (크로스헤어, PC 단축키 가이드 등)")]

    public GameObject[] pcOnlyObjects;



    [Tooltip("모바일에서만 켤 GO (가상 조이스틱 UI, 터치 버튼 등)")]

    public GameObject[] mobileOnlyObjects;



    [Tooltip("VR에서만 켤 GO (VR 전용 메뉴, 손 UI 등)")]

    public GameObject[] vrOnlyObjects;



    [Header("VR Follow 설정")]

    public float vrCanvasDistance = 1.2f;      // 플레이어 정면 거리

    public float vrCanvasHeightOffset = -0.2f; // 눈높이 보정

    public float vrCanvasScale = 0.001f;

    public float followSpeed = 5f;



    private bool _isVR;

    // ── 생명주기 ───────────────────────────────────────────



    private void Awake()

    {

        if(Instance != null) { Destroy(gameObject); return; }

        Instance = this;



        CurrentPlatform = Utils.GetPlatformType();

        UserInfo.PlatformType = CurrentPlatform;

        _isVR = (CurrentPlatform == PlatformType.VR);

        Apply();



        //Managers.Sound.Play("GameTheme", Define.Sound.Bgm);

    }



    // ── 플랫폼 설정 적용 ───────────────────────────────────



    private void Apply()

    {

        SetupCanvases();

        SetupPlatformObjects();

        Debug.Log($"[PlatformManager] 플랫폼: {CurrentPlatform}");

    }



    // ── 매 프레임 플레이어를 따라가는 로직 ──────────────────────

    private void LateUpdate()

    {

        if(!_isVR || canvases == null) return;



        foreach(var canvas in canvases)

        {

            // 캔버스가 활성화되어 있을 때만 따라오게 함

            if(canvas != null && canvas.gameObject.activeInHierarchy)

            {

                FollowPlayer(canvas);

            }

        }

    }



    private void FollowPlayer(Canvas canvas)

    {

        Transform cam = Camera.main?.transform;

        if(cam == null) return;



        // 1. 목표 위치 계산 (수평 평면 유지)

        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;

        if(forward == Vector3.zero) forward = cam.forward;



        Vector3 targetPos = cam.position + (forward * vrCanvasDistance) + (Vector3.up * vrCanvasHeightOffset);



        // 2. 플레이어를 바라보는 회전 (빌보드 효과)

        Quaternion targetRot = Quaternion.LookRotation(canvas.transform.position - cam.position);



        // 3. 부드럽게 이동 및 회전

        canvas.transform.position = Vector3.Lerp(canvas.transform.position, targetPos, Time.deltaTime * followSpeed);

        canvas.transform.rotation = Quaternion.Slerp(canvas.transform.rotation, targetRot, Time.deltaTime * followSpeed);

    }

    // ── Canvas renderMode 설정 ─────────────────────────────



    private void SetupCanvases()

    {

        if(canvases == null) return;

        foreach(var canvas in canvases)

        {

            if(canvas == null) continue;



            if(_isVR)

            {

                canvas.renderMode = RenderMode.WorldSpace;

                canvas.worldCamera = Camera.main;

                canvas.transform.localScale = Vector3.one * vrCanvasScale;



                // 처음 켤 때는 즉시 플레이어 앞으로 강제 이동 (순간이동)

                ForcePositionInFront(canvas);

            }

            else

            {

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                canvas.sortingOrder = 10;

                canvas.transform.localScale = Vector3.one;

            }

        }

    }



    public void ForcePositionInFront(Canvas canvas)

    {

        Transform cam = Camera.main?.transform;

        if(cam == null) return;



        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;

        canvas.transform.position = cam.position + (forward * vrCanvasDistance) + (Vector3.up * vrCanvasHeightOffset);

        canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - cam.position);

    }



    //// VR Canvas를 카메라 앞에 배치

    //public void PositionCanvasInFrontOfCamera(Canvas canvas)

    //{

    //    if (canvas == null || CurrentPlatform != PlatformType.VR) return;



    //    Transform cam = Camera.main?.transform;

    //    if (cam == null) return;



    //    Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;

    //    if (forward == Vector3.zero) forward = cam.forward;



    //    canvas.transform.position = cam.position

    //                              + forward * vrCanvasDistance

    //                              + Vector3.up * vrCanvasHeightOffset;

    //    canvas.transform.rotation = Quaternion.LookRotation(

    //        canvas.transform.position - cam.position);

    //}



    //// VR Canvas 전체 재배치 (인벤토리 패널 열릴 때 호출)

    //public void RepositionAllVRCanvases()

    //{

    //    if (CurrentPlatform != PlatformType.VR || canvases == null) return;

    //    foreach (var canvas in canvases)

    //        PositionCanvasInFrontOfCamera(canvas);

    //}



    // ── 플랫폼별 오브젝트 ON/OFF ───────────────────────────



    private void SetupPlatformObjects()

    {

        bool isPC = CurrentPlatform == PlatformType.PC;

        bool isMobile = CurrentPlatform == PlatformType.Mobile;

        bool isVR = CurrentPlatform == PlatformType.VR;



        SetActive(pcOnlyObjects, isPC);

        SetActive(mobileOnlyObjects, isMobile);

        SetActive(vrOnlyObjects, isVR);

    }



    private static void SetActive(GameObject[] objects, bool active)

    {

        if(objects == null) return;

        foreach(var go in objects)

            if(go != null) go.SetActive(active);

    }



    // ── 외부 API ───────────────────────────────────────────



    public static bool IsVR => CurrentPlatform == PlatformType.VR;

    public static bool IsMobile => CurrentPlatform == PlatformType.Mobile;

    public static bool IsPC => CurrentPlatform == PlatformType.PC;

}