using System;

// ============================================================
//  UiHooks.cs — UI 모듈(DoDrill.Ui)의 "확장 지점" (모듈화 ⑤, 2026-11-20)
//
//  UI 모듈(위젯·VR 창 따라오기·진행 패널·토스트·경고창·이름표)은 전기차 문구 사전을 알지 못한다.
//  콘텐츠(EV)가 시작할 때(Content/Features/App/Bindings/EvUiBinding.cs) 여기에 자기 기능을 등록한다.
//  등록이 없으면 받은 이름을 그대로 보여 준다.
//  ※ VR 여부는 Common 의 VrUi.IsVR 을 쓴다(EvAvatarBinding 에서 등록).
// ============================================================
public static class UiHooks
{
    /// <summary>내부 ID(Hood_Latch_Point 등) → 사람이 읽는 이름</summary>
    public static Func<string, string> DisplayName;
    public static string NameOf(string rawId) => DisplayName != null ? DisplayName(rawId) : rawId;
}
