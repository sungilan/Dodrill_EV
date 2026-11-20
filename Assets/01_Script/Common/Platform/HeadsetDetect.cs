using UnityEngine;
using UnityEngine.XR;
#if ENABLE_VR || ENABLE_XR_MODULE
using UnityEngine.XR.Management;
#endif

/// <summary>
/// 지금 기기가 헤드셋(VR)일 수 있는지 판정한다(모듈 자체 구현 — AutoHand 수정본에 기대지 않는다).
///   ① XRSettings.isDeviceActive
///   ② 로비에서 정한 플랫폼이 VR (UserInfo.PlatformType)
///   ③ XR 로더가 실제로 떠 있음
///   ④ XR 초기화가 아직 안 끝났고 앱 시작 후 얼마 안 됨 → '모른다'는 헤드셋으로 취급(보수적)
/// 확실히 헤드셋이 아닐 때만 <see cref="DefinitelyNotHeadset"/> 이 true.
/// </summary>
public static class HeadsetDetect
{
    // 초기화 결과를 기다려 주는 최대 시간(앱 시작 후 초). PC 에서 로더가 영영 '초기화 완료'가 안 되는 경우를 대비한 상한.
    private const float UnknownGraceSeconds = 10f;

    public static bool MaybeHeadset
    {
        get
        {
            try
            {
                if (XRSettings.isDeviceActive) return true;
                return Check();
            }
            catch { return false; }   // XR 가 아직 준비 전 — 헤드셋 아님으로 본다
        }
    }

    public static bool DefinitelyNotHeadset => !MaybeHeadset;

    /// <summary>XRSettings 외의 신호(로비 플랫폼, XR 로더, 초기화 대기). EV 는 이것을 AutoHand 의 XrDetect 에도 꽂는다.</summary>
    public static bool Check()
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
