using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>평가 규칙 등급 — 기획서의 치명 / 중결점 / 경결점</summary>
public enum EvalGrade { Minor, Major, Fatal }

/// <summary>감점 규칙 한 줄. Resources/Evaluation/evaluation_rules.json 에서 읽는다(기획에서 숫자를 바꿀 때 코드를 건드리지 않게).</summary>
[Serializable]
public class EvaluationRule
{
    public string id;           // 코드가 부르는 이름 (예: SAFE_DISCHARGE_WAIT)
    public string category;     // safety / procedure / technique / environment
    public string grade;        // fatal / major / minor
    public int points;          // 감점 (치명은 사실상 전부 — 점수를 0으로 만든다)
    public bool oncePerTask;    // true: 같은 사람이 한 단계에서 같은 위반을 여러 번 해도 한 번만 감점
    public bool enabled;        // false: 규칙표에는 있지만 아직 감지하는 코드가 없음
    public string ko;
    public string en;
    public string vi;

    public EvalGrade Grade
    {
        get
        {
            switch ((grade ?? "").ToLowerInvariant())
            {
                case "fatal": return EvalGrade.Fatal;
                case "major": return EvalGrade.Major;
                default: return EvalGrade.Minor;
            }
        }
    }

    /// <summary>언어별 문구(없으면 한국어)</summary>
    public string Message(string lang)
    {
        string m = lang == "en" ? en : lang == "vi" ? vi : ko;
        return string.IsNullOrEmpty(m) ? ko : m;
    }
}

[Serializable]
public class EvaluationRuleSet
{
    public int startScore = 100;
    public List<EvaluationRule> rules = new List<EvaluationRule>();
}

public static class EvaluationRules
{
    private const string ResourcePath = "Evaluation/evaluation_rules";

    private static EvaluationRuleSet s_set;
    private static Dictionary<string, EvaluationRule> s_byId;

    public static EvaluationRuleSet Set { get { EnsureLoaded(); return s_set; } }

    public static EvaluationRule Get(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return null;
        s_byId.TryGetValue(id, out var rule);
        return rule;
    }

    /// <summary>규칙표를 다시 읽는다(시나리오 시작 때)</summary>
    public static void Reload()
    {
        s_set = null;
        s_byId = null;
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        if (s_set != null) return;

        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset != null)
        {
            try { s_set = JsonUtility.FromJson<EvaluationRuleSet>(asset.text); }
            catch (Exception e) { Debug.LogError($"[Eval] 규칙표 해석 실패: {e.Message}"); }
        }
        else
        {
            Debug.LogWarning($"[Eval] Resources/{ResourcePath}.json 이 없어 기본 규칙을 씁니다.");
        }

        if (s_set == null || s_set.rules == null || s_set.rules.Count == 0)
            s_set = Fallback();

        s_byId = new Dictionary<string, EvaluationRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in s_set.rules)
            if (r != null && !string.IsNullOrEmpty(r.id)) s_byId[r.id] = r;

        Debug.Log($"[Eval] 규칙 {s_byId.Count}개 로드 (감지 켜짐 {CountEnabled()}개), 시작 점수 {s_set.startScore}");
    }

    private static int CountEnabled()
    {
        int n = 0;
        foreach (var r in s_set.rules) if (r != null && r.enabled) n++;
        return n;
    }

    /// <summary>규칙표 파일이 없을 때 최소한의 기본값 (지금 감지하는 규칙들)</summary>
    private static EvaluationRuleSet Fallback()
    {
        return new EvaluationRuleSet
        {
            startScore = 100,
            rules = new List<EvaluationRule>
            {
                new EvaluationRule { id = "SAFE_HV_BEFORE_CUTOFF", category = "safety", grade = "fatal", points = 100, oncePerTask = true, enabled = true,
                    ko = "고전압 차단 전에 커넥터를 분리하거나 접촉했습니다.", en = "Touched a high-voltage part before the system was shut off." },
                new EvaluationRule { id = "SAFE_NO_PPE", category = "safety", grade = "major", points = 20, oncePerTask = true, enabled = true,
                    ko = "보호구(절연 장갑 등)를 착용하지 않고 작업했습니다.", en = "Worked without protective equipment." },
                new EvaluationRule { id = "SAFE_DISCHARGE_WAIT", category = "safety", grade = "major", points = 15, oncePerTask = true, enabled = true,
                    ko = "고전압 차단 후 방전 대기 시간을 지키지 않았습니다.", en = "Did not wait the required discharge time." },
                new EvaluationRule { id = "PROC_ORDER", category = "procedure", grade = "minor", points = 10, oncePerTask = true, enabled = true,
                    ko = "정비 순서를 지키지 않았습니다.", en = "Did not follow the service procedure order." },
                new EvaluationRule { id = "HINT_USED", category = "procedure", grade = "minor", points = 5, oncePerTask = true, enabled = true,
                    ko = "힌트를 사용했습니다.", en = "Used a hint." },
            }
        };
    }
}
