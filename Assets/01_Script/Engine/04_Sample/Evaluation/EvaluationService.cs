using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>한 번의 감점 기록</summary>
[Serializable]
public class EvalEntry
{
    public int taskIndex;
    public string ruleId;
    public string category;
    public EvalGrade grade;
    public int points;       // 실제로 깎인 점수 (0 아래로는 안 깎이므로 남은 점수보다 클 수 없다)
    public string message;   // 한국어 문구
    public string detail;    // 상황 설명(어느 존, 어느 아이템 등)
    public float time;       // 시나리오 시작 후 경과 시간(초)
}

/// <summary>한 사람의 평가 상태</summary>
[Serializable]
public class PlayerEval
{
    public int clientId;
    public int score;
    public bool fatal;       // 치명 위반으로 탈락
    public List<EvalEntry> entries = new List<EvalEntry>();

    /// <summary>분류별 감점 합계 (결과 화면의 영역별 내역용)</summary>
    public int PenaltyOf(string category)
    {
        int sum = 0;
        foreach (var e in entries) if (e.category == category) sum += e.points;
        return sum;
    }
}

/// <summary>
/// [서버 전용] 사람(clientId)별 평가 점수와 감점 기록.
///
/// 판정은 모두 서버가 한다 — 클라이언트가 점수를 보내오는 방식이 아니다(공군 프로젝트는 클라이언트 값을 서버가 믿었다).
/// 감점 규칙은 Resources/Evaluation/evaluation_rules.json. 코드에서는 규칙 ID 로 Record 만 부르면 된다.
///
/// 지금(1단계)은 기록과 로그까지. 점수판 표시·결과 화면·저장은 다음 단계에서 OnRecorded 와 Players 를 읽어 붙인다.
/// </summary>
public class EvaluationService
{
    private readonly Dictionary<int, PlayerEval> _players = new Dictionary<int, PlayerEval>();
    private readonly HashSet<string> _onceKeys = new HashSet<string>();
    private readonly HashSet<string> _unknownWarned = new HashSet<string>();
    private float _startTime;

    public IReadOnlyDictionary<int, PlayerEval> Players => _players;

    /// <summary>감점이 기록될 때마다(clientId, 기록, 그 사람의 현재 상태). 점수판 표시 등에서 구독한다.</summary>
    public event Action<int, EvalEntry, PlayerEval> OnRecorded;

    /// <summary>새 시나리오가 시작될 때</summary>
    public void Reset()
    {
        _players.Clear();
        _onceKeys.Clear();
        _unknownWarned.Clear();
        _startTime = Time.time;
        EvaluationRules.Reload();
    }

    public PlayerEval Get(int clientId)
    {
        if (!_players.TryGetValue(clientId, out var p))
        {
            p = new PlayerEval { clientId = clientId, score = EvaluationRules.Set.startScore };
            _players[clientId] = p;
        }
        return p;
    }

    /// <summary>
    /// clientId 가 ruleId 위반을 했다고 기록한다. 기록하지 않으면 null
    /// (모르는 규칙 / 감지가 꺼진 규칙 / 이미 탈락한 사람 / 같은 단계에서 이미 감점한 규칙).
    /// </summary>
    public EvalEntry Record(int clientId, string ruleId, int taskIndex, string detail = "")
    {
        var rule = EvaluationRules.Get(ruleId);
        if (rule == null)
        {
            if (_unknownWarned.Add(ruleId ?? "null"))
                Debug.LogWarning($"[Eval] 규칙표에 없는 규칙: {ruleId}");
            return null;
        }
        if (!rule.enabled) return null;

        var player = Get(clientId);
        if (player.fatal) return null;   // 이미 탈락

        if (rule.oncePerTask && !_onceKeys.Add($"{clientId}|{rule.id}|{taskIndex}"))
            return null;

        bool fatal = rule.Grade == EvalGrade.Fatal;
        int points = fatal ? player.score : Mathf.Min(rule.points, player.score);

        var entry = new EvalEntry
        {
            taskIndex = taskIndex,
            ruleId = rule.id,
            category = rule.category,
            grade = rule.Grade,
            points = points,
            message = rule.ko,
            detail = detail ?? string.Empty,
            time = Time.time - _startTime,
        };

        player.score = Mathf.Max(0, player.score - points);
        if (fatal) player.fatal = true;
        player.entries.Add(entry);

        Debug.Log($"<color=orange>[Eval]</color> Client#{clientId} {rule.id} ({rule.grade}) -{points} → {player.score}점" +
                  (fatal ? " [치명: 탈락]" : "") + $" | Task[{taskIndex}] {detail}");

        OnRecorded?.Invoke(clientId, entry, player);
        return entry;
    }

    /// <summary>시나리오 시작 후 지금까지 걸린 시간(초)</summary>
    public float ElapsedSeconds => Time.time - _startTime;

    /// <summary>등급: 90 이상 S, 80 이상 A, 70 이상 B, 그 아래와 치명 탈락은 F (초안 EvaluationReport 와 같은 기준)</summary>
    public static string GradeOf(int score, bool fatal)
    {
        if (fatal) return "F";
        if (score >= 90) return "S";
        if (score >= 80) return "A";
        if (score >= 70) return "B";
        return "F";
    }

    /// <summary>감점 내역을 "규칙ID|감점|단계번호" 줄로 만든다 (EvalResultBroadcast.lines)</summary>
    public static string BuildLines(PlayerEval player)
    {
        if (player == null || player.entries.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < player.entries.Count; i++)
        {
            var e = player.entries[i];
            if (i > 0) sb.Append('\n');
            sb.Append(e.ruleId).Append('|').Append(e.points).Append('|').Append(e.taskIndex);
        }
        return sb.ToString();
    }

    /// <summary>시나리오가 끝났을 때 로그에 남길 사람별 요약</summary>
    public string BuildSummary()
    {
        if (_players.Count == 0) return "[Eval] 기록된 감점 없음";

        var sb = new StringBuilder("[Eval] ── 평가 요약 ──");
        foreach (var p in _players.Values)
        {
            sb.Append($"\n  Client#{p.clientId}: {p.score}점{(p.fatal ? " (치명 탈락)" : "")}, 감점 {p.entries.Count}건");
            foreach (var e in p.entries)
                sb.Append($"\n    - Task[{e.taskIndex}] {e.ruleId} -{e.points} ({e.category}/{e.grade}) {e.detail}");
        }
        return sb.ToString();
    }
}
