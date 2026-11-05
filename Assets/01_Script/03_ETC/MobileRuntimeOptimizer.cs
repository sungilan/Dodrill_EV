using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

/// <summary>
/// 모바일(스마트폰·태블릿) 발열·배터리 대책. 공군(AirForce_XR) 프로젝트의 MobileRuntimeOptimizer 에서 가져온 핵심만 적용한다.
/// 씬·프리팹은 건드리지 않고 실행 중에만 적용하며, PC·서버·VR 헤드셋(XR 장치 활성)에서는 아무것도 하지 않는다.
///
/// 공군에서 확인된 발열의 진짜 원인:
///   FishNet ClientManager(Client 씬)가 방에 접속할 때마다 Application.targetFrameRate 를 500 으로 덮어써
///   폰이 프레임 제한 없이 최대 속도로 그렸다. 학습 앱은 화면 변화가 적어 30fps 로 충분하다.
///   → 매 프레임 비교해 다른 값으로 바뀌어 있으면 30 으로 되돌린다(비교 한 번 — 비용 없음).
/// 추가: 렌더 해상도 상한(1080p 급, 저사양 720p 급), HDR·MSAA 끔, 그림자 거리 축소(저사양은 끔).
/// </summary>
public class MobileRuntimeOptimizer : MonoBehaviour
{
    private const int TargetFps = 30;
    private const float MaxRenderHeight = 1080f;
    private const float LowEndRenderHeight = 720f;

    public static bool IsLowEnd { get; private set; }

    private int _lastLoggedOverride = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode) return;
        if (!Application.isMobilePlatform) return;
        if (XRSettings.isDeviceActive) return;              // Quest 등 헤드셋은 XR 런타임이 주사율을 정한다
        if (FindFirstObjectByType<MobileRuntimeOptimizer>() != null) return;

        var go = new GameObject("[MobileRuntimeOptimizer]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<MobileRuntimeOptimizer>();
    }

    private static bool DetectLowEnd()
    {
        string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
        bool oldGpu = gpu.Contains("mali-g71") || gpu.Contains("mali-g72") || gpu.Contains("mali-g52") || gpu.Contains("mali-g51")
                   || gpu.Contains("mali-t") || gpu.Contains("adreno (tm) 5") || gpu.Contains("adreno (tm) 4") || gpu.Contains("powervr");
        return (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 6000) || oldGpu;
    }

    // 헤드셋(Quest 등 안드로이드 VR)이면 건드리지 않는다 — XR 초기화가 늦어 Boot 시점엔 장치가 아직 안 잡혔을 수 있어 Start·Update 에서도 확인한다
    private static bool IsHeadset => XRSettings.isDeviceActive || UserInfo.PlatformType == XRAirpotrSecurity.PlatformType.VR;

    private void Start()
    {
        if (IsHeadset) { Destroy(gameObject); return; }
        IsLowEnd = DetectLowEnd();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps;

        // 화면이 꺼지면 앱이 일시정지되어 방 접속이 끊긴다 — 플레이 중에는 꺼지지 않게
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            int h = Mathf.Min(Screen.width, Screen.height);           // 가로 화면 기준 세로 픽셀
            float maxH = IsLowEnd ? LowEndRenderHeight : MaxRenderHeight;
            if (h > maxH) urp.renderScale = Mathf.Clamp(maxH / h, 0.5f, 1f);
            urp.supportsHDR = false;                                   // HDR 버퍼 불필요 — 대역폭·발열 절감
            urp.msaaSampleCount = 1;                                   // MSAA 끔(타일 GPU 메모리 대역폭)
            urp.shadowDistance = IsLowEnd ? 0f : Mathf.Min(urp.shadowDistance, 15f);
            Debug.Log($"[MobileOptimizer] {SystemInfo.deviceModel} GPU={SystemInfo.graphicsDeviceName} RAM={SystemInfo.systemMemorySize}MB lowEnd={IsLowEnd} " +
                      $"화면 {Screen.width}x{Screen.height} renderScale={urp.renderScale:0.00} targetFps={Application.targetFrameRate}");
        }
    }

    // 다른 코드(FishNet 등)가 목표 프레임을 바꿔도 되돌린다
    private void Update()
    {
        int cur = Application.targetFrameRate;
        if (cur == TargetFps) return;
        if (IsHeadset) { Destroy(gameObject); return; }
        if (cur != _lastLoggedOverride)
        {
            _lastLoggedOverride = cur;
            Debug.Log($"[MobileOptimizer] 목표 프레임이 {cur} 로 바뀜(FishNet 등) → {TargetFps} 로 되돌림");
        }
        Application.targetFrameRate = TargetFps;
    }
}
