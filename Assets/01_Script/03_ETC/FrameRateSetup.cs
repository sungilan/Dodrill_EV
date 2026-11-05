using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 모바일(스마트폰) 프레임 제한 설정.
/// Application.targetFrameRate 를 지정하지 않고 vSync 도 0 이면 안드로이드는 기본 30fps 로 동작하는 경우가 많아
/// 모바일만 눈에 띄게 끊겨 보이고, 프레임 의존 보간(VRPlayer)의 수렴 속도도 기기마다 달라진다.
/// VR 헤드셋(XR 장치 활성)에서는 런타임의 주사율에 맡기고 건드리지 않는다.
/// </summary>
public static class FrameRateSetup
{
    // 60 → 30: 발열의 가장 큰 원인이었다(공군 프로젝트에서 확인). 실제 고정은 MobileRuntimeOptimizer 가 매 프레임 보장한다.
    private const int MobileTargetFps = 30;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        if (!Application.isMobilePlatform) return;
        if (XRSettings.isDeviceActive) return;           // Quest 등 헤드셋은 XR 런타임이 주사율 결정

        if (Application.targetFrameRate <= 0)
            Application.targetFrameRate = MobileTargetFps;
    }
}
