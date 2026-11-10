using System;

/// <summary>
/// 훈련 / 평가 모드 구분.
/// 시나리오 ID 가 "_eval" 로 끝나면 평가 모드다 (예: ev_p0aa6_battery_insulation_eval).
/// 방을 만들 때 콘텐츠 목록에서 평가용 시나리오를 고르면 되고, 시나리오 데이터는 서버·클라이언트가 모두 받으므로
/// 모드 값을 따로 주고받지 않는다.
///
///  - 훈련: 가이드(마커·아웃라인·안내 문구·음성)가 다 나온다. 위반하면 사유만 알려 주고 점수·결과 화면은 없다.
///  - 평가: 가이드를 숨기고 단계 제목만 보여 준다. 점수판·결과 화면·CSV 기록. 힌트를 쓰면 그 단계 가이드를 보여 주고 감점.
/// </summary>
public static class EvaluationMode
{
    public const string Suffix = "_eval";

    /// <summary>힌트 감점 규칙 ID (evaluation_rules.json)</summary>
    public const string HintRuleId = "HINT_USED";

    /// <summary>한 사람이 시나리오 전체에서 쓸 수 있는 힌트 수</summary>
    public const int MaxHints = 3;

    public static bool IsEvaluation(string scenarioId)
        => !string.IsNullOrEmpty(scenarioId) && scenarioId.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    public static bool IsEvaluation(ScenarioData data) => data != null && IsEvaluation(data.scenarioId);

    /// <summary>[클라이언트] 지금 받은 시나리오가 평가 모드인지</summary>
    public static bool ClientIsEvaluation
    {
        get
        {
            var receiver = ScenarioStateReceiver.Instance;
            return receiver != null && IsEvaluation(receiver.CurrentScenario);
        }
    }
}
