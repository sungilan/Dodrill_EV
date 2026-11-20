using UnityEngine;

/// <summary>
/// EV 의 수정된 AutoHand(<see cref="XrDetect"/>)에 이 프로젝트의 헤드셋 판정 신호(<see cref="HeadsetDetect.Check"/>)를 꽂는다.
/// 모듈(Common)은 AutoHand 수정본에 기대지 않으므로 이 연결은 EV 콘텐츠에 둔다.
/// </summary>
public static class XrDetectSignals
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install() => XrDetect.ExtraHeadsetSignal = HeadsetDetect.Check;
}
