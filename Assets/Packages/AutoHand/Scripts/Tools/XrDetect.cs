using System;
using UnityEngine.XR;

/// <summary>
/// 지금 기기가 헤드셋(VR)일 수 있는지 판정한다. (AutoHand 어셈블리 안에 둔다 — AutoInputModule/HandCanvasPointer 가 쓴다)
///
/// XRSettings.isDeviceActive 하나만 보면 안 된다 — Quest 에서 XR 초기화가 늦으면 시작 직후에는 false 이다.
/// 그 값으로 'PC 용 EventSystem 정리를 건너뛰기' 같은 분기를 가르면 헤드셋에서도 PC 쪽 코드가 실행돼 VR 손 레이저 UI 가 안 눌린다.
/// 그래서 프로젝트(Assembly-CSharp)가 <see cref="ExtraHeadsetSignal"/> 로 추가 신호를 꽂는다(XR 로더가 떠 있음, 로비 플랫폼=VR, 초기화 대기 중).
/// 확실히 헤드셋이 아닐 때만 <see cref="DefinitelyNotHeadset"/> 이 true.
/// </summary>
public static class XrDetect
{
    /// <summary>프로젝트가 설정하는 추가 판정. true 면 헤드셋이거나 아직 모른다는 뜻.</summary>
    public static Func<bool> ExtraHeadsetSignal;

    public static bool MaybeHeadset
    {
        get
        {
            try
            {
                if (XRSettings.isDeviceActive) return true;
                var f = ExtraHeadsetSignal;
                if (f != null && f()) return true;
            }
            catch { /* XR 가 아직 준비 전 — 헤드셋 아님으로 본다 */ }
            return false;
        }
    }

    public static bool DefinitelyNotHeadset => !MaybeHeadset;
}
