using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

public class DataPersistenceManager : MonoBehaviour
{
    public static DataPersistenceManager Instance;
    private string filePath;

    void Awake()
    {
        Instance = this;
        // 저장 경로: C:/Users/사용자/AppData/LocalLow/회사명/프로젝트명/EvaluationResults.csv
        filePath = Path.Combine(Application.persistentDataPath, "EvaluationResults.csv");

        // 파일이 없으면 헤더 생성
        if(!File.Exists(filePath))
        {
            File.WriteAllText(filePath, "날짜,닉네임,최종점수,등급,감점사유\n");
        }
    }

    // CSV 셀: 쉼표·따옴표·줄바꿈이 들어 있어도 열이 깨지지 않게 따옴표로 감싸고 내부 따옴표는 두 번 쓴다
    private static string CsvCell(string s)
    {
        if(s == null) return "";
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    public void SaveResult(int score, string grade, List<PenaltyLog> logs)
    {
        string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        // 예전에는 모든 기록에 같은 이름이 고정으로 들어갔다 -> 로비에서 정한 실제 닉네임을 쓴다
        string nickname = string.IsNullOrEmpty(UserInfo.UserName) ? "Unknown" : UserInfo.UserName;

        // 감점 사유들을 하나의 문자열로 결합 (엑셀 셀 구분 방지를 위해 세미콜론 사용)
        string reasonSummary = "";
        foreach(var log in logs)
        {
            reasonSummary += $"[{log.reason}:-{log.score}];";
        }

        // CSV 한 줄 생성
        string newLine = $"{date},{CsvCell(nickname)},{score},{CsvCell(grade)},{CsvCell(reasonSummary)}\n";

        // 파일 끝에 추가 (경로 권한/잠금으로 실패해도 앱이 멈추지 않게)
        try
        {
            File.AppendAllText(filePath, newLine);
            Debug.Log($"성적 저장 완료: {filePath}");
        }
        catch(Exception e)
        {
            Debug.LogError($"성적 저장 실패: {filePath}\n{e.Message}");
        }
    }
}