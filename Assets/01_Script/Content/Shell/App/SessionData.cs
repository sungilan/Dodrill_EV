// ============================================================
//  SessionData.cs
//  씬 전환 시에도 유지되는 세션 전역 데이터 (static)
//  DontDestroyOnLoad 없이 static으로 관리 — FishNet SyncVar 불필요
//  (방장이 로비에서 설정 → Game 씬에서 읽기)
// ============================================================
public static class SessionData
{
    public enum GameMode { Scenario, FreeMode }

    /// <summary>로비에서 방장이 선택한 게임 모드</summary>
    public static GameMode gameMode = GameMode.Scenario;

    /// <summary>초기화 (씬 재로드 시 호출)</summary>
    public static void Reset()
    {
        gameMode = GameMode.Scenario;
    }
}
