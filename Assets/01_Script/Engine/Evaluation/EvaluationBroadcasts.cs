using FishNet.Broadcast;

/// <summary>
/// 서버 → 해당 클라이언트(한 사람에게만): 방금 감점이 기록됐다. 점수판·알림용.
/// 문구는 보내지 않는다 — 클라이언트가 규칙표(evaluation_rules.json)에서 자기 언어로 찾는다.
/// </summary>
public struct EvalPenaltyBroadcast : IBroadcast
{
    public string ruleId;
    public int points;     // 이번에 깎인 점수
    public int score;      // 깎인 뒤 남은 점수
    public bool fatal;     // 치명 위반(탈락)
}

/// <summary>
/// 클라이언트 → 서버: 이 사람 화면에서 안전 사고(감전·열폭주)가 났다(InteractablePart.CheckSafetyAndTriggerAccident).
/// 서버는 이 신호를 그대로 믿지 않고, 자기가 진행시킨 단계(MSD 차단·보호구 착용)로 다시 판정해서 기록한다.
/// </summary>
public struct SafetyAccidentSignal : IBroadcast
{
    public int accidentType;   // EmergencyEventManager.AccidentType
    public string partName;    // 잡으려던 부품 (로그용)
}

/// <summary>
/// 클라이언트 → 서버: 평가 모드에서 힌트 버튼을 눌렀다. 서버가 남은 횟수를 확인하고 HINT_USED 로 감점하면
/// 그 감점 알림(EvalPenaltyBroadcast)을 받은 클라이언트가 현재 단계 가이드를 보여 준다.
/// </summary>
public struct EvalHintRequest : IBroadcast
{
    public int taskIndex;   // 누른 시점의 단계 (로그용 — 서버는 자기 진행 단계로 기록)
}

/// <summary>
/// 서버 → 해당 클라이언트(한 사람에게만): 시나리오 끝, 이 사람의 최종 결과.
/// lines = 감점 내역을 "규칙ID|감점|단계번호" 로 한 줄씩 '\n' 으로 이은 문자열.
/// </summary>
public struct EvalResultBroadcast : IBroadcast
{
    public int score;
    public string grade;   // S / A / B / F
    public bool fatal;
    public string lines;
    public int totalSeconds;
}
