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

    public void SaveResult(int score, string grade, List<PenaltyLog> logs)
    {
        string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string nickname = "익산시의 방랑자"; // User Summary 반영

        // 감점 사유들을 하나의 문자열로 결합 (엑셀 셀 구분 방지를 위해 세미콜론 사용)
        string reasonSummary = "";
        foreach(var log in logs)
        {
            reasonSummary += $"[{log.reason}:-{log.score}];";
        }

        // CSV 한 줄 생성
        string newLine = $"{date},{nickname},{score},{grade},\"{reasonSummary}\"\n";

        // 파일 끝에 추가
        File.AppendAllText(filePath, newLine);
        Debug.Log($"성적 저장 완료: {filePath}");
    }
}