using System;

// ============================================================
//  SafetyHooks.cs — 안전 모듈(DoDrill.Safety)의 "확장 지점" (모듈화 ③, 2026-11-20)
//
//  안전 모듈(고전압 안전 상태·LOTO·사고 연출·보호구)은 EV 콘텐츠의 화면·연습 보드를 직접 알지 못한다.
//  콘텐츠(EV)가 시작할 때(Content/Features/App/Bindings/EvSafetyBinding.cs) 여기에 자기 기능을 등록한다.
//  등록이 없으면 기본 동작(연습 모드 아님 / 결과 화면 없음 / 알림 없음 / 원래 ID 표시)으로 안전하게 동작한다.
// ============================================================
public static class SafetyHooks
{
    /// <summary>단계 없는 연습 시나리오인가(scenarioId) — 맞으면 단계 진행으로 안전 상태를 맞추지 않는다</summary>
    public static Func<string, bool> IsPracticeScenario;
    public static bool PracticeScenario(string scenarioId) => IsPracticeScenario != null && IsPracticeScenario(scenarioId);

    /// <summary>지금 자유 연습 중인가 — 사고 뒤 '다시 시도' 허용 여부</summary>
    public static Func<bool> IsFreePractice;
    public static bool FreePractice => IsFreePractice != null && IsFreePractice();

    /// <summary>평가 결과 화면이 떠 있는가 — 사고 팝업은 결과 화면을 닫은 뒤 띄운다</summary>
    public static Func<bool> IsResultShowing;
    public static bool ResultShowing => IsResultShowing != null && IsResultShowing();

    /// <summary>LOTO 자물쇠가 체결됐을 때(방전 대기 타이머 시작 등)</summary>
    public static Action LotoLocked;
    public static void RaiseLotoLocked() => LotoLocked?.Invoke();

    /// <summary>화면 알림(리치 텍스트) — 보호구 착용 안내 등</summary>
    public static Action<string> Toast;
    public static void ShowToast(string richText) => Toast?.Invoke(richText);

    /// <summary>프리팹 ID → 사람이 읽는 이름</summary>
    public static Func<string, string> DisplayName;
    public static string NameOf(string prefabId) => DisplayName != null ? DisplayName(prefabId) : prefabId;
}
