using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// [서버] 사람별 평가 결과를 CSV 한 줄씩 누적 저장한다 (UTF-8 BOM, 엑셀에서 바로 열림).
/// 파일: Application.persistentDataPath/evaluation_results.csv
///   (Windows 서버: C:\Users\사용자\AppData\LocalLow\회사\프로젝트\ / Linux 서버: ~/.config/unity3d/회사/프로젝트/)
/// 나중에 백엔드가 생기면 AppendRow 호출부에 전송 코드만 더하면 된다.
/// </summary>
public static class EvaluationResultExporter
{
    private const string FileName = "evaluation_results.csv";
    private static readonly string[] Header =
        { "기록시각", "시나리오", "플레이어", "ClientId", "점수", "등급", "탈락", "소요시간(초)", "감점건수", "감점내역" };

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static void AppendRow(string scenarioId, string playerName, PlayerEval player, float elapsedSeconds)
    {
        try
        {
            bool isNew = !File.Exists(FilePath);
            using (var writer = new StreamWriter(FilePath, true, new UTF8Encoding(true)))
            {
                if (isNew) writer.WriteLine(string.Join(",", Header));

                var detail = new StringBuilder();
                foreach (var e in player.entries)
                    detail.Append($"[Task{e.taskIndex} {e.ruleId} -{e.points}];");

                writer.WriteLine(string.Join(",", new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Cell(scenarioId),
                    Cell(playerName),
                    player.clientId.ToString(),
                    player.score.ToString(),
                    EvaluationService.GradeOf(player.score, player.fatal),
                    player.fatal ? "Y" : "N",
                    elapsedSeconds.ToString("F0"),
                    player.entries.Count.ToString(),
                    Cell(detail.ToString()),
                }));
            }
            Debug.Log($"[Eval] 결과 저장: {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Eval] 결과 저장 실패: {e.Message}");
        }
    }

    // CSV 셀: 쉼표·따옴표·줄바꿈이 있어도 열이 깨지지 않게 감싼다
    private static string Cell(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
