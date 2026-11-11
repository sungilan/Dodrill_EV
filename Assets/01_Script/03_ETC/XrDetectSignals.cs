using UnityEngine;
#if ENABLE_VR || ENABLE_XR_MODULE
using UnityEngine.XR.Management;
#endif

/// <summary>
/// <see cref="XrDetect"/>(AutoHand 어셈블리)에 이 프로젝트의 헤드셋 판정 신호를 꽂는다.
///   ① XR 로더가 실제로 떠 있음 (XRGeneralSettings.Manager.activeLoader)
///   ② 로비에서 정한 플랫폼이 VR (UserInfo.PlatformType)
///   ③ XR 초기화가 아직 안 끝났고 앱 시작 후 얼마 안 됨 → '모른다'는 헤드셋으로 취급(보수적)
/// </summary>
public static class XrDetectSignals
{
    // 초기화 결과를 기다려 주는 최대 시간(앱 시작 후 초). PC 에서 로더가 영영 '초기화 완료'가 안 되는 경우를 대비한 상한.
    private const float UnknownGraceSeconds = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install() => XrDetect.ExtraHeadsetSignal = Check;

    private static bool Check()
    {
        if (UserInfo.PlatformType == XRAirpotrSecurity.PlatformType.VR) return true;
#if ENABLE_VR || ENABLE_XR_MODULE
        var s = XRGeneralSettings.Instance;
        if (s != null && s.Manager != null)
        {
            if (s.Manager.activeLoader != null) return true;
            if (!s.Manager.isInitializationComplete && Time.realtimeSinceStartup < UnknownGraceSeconds) return true;
        }
#endif
        return false;
    }
}
