using FishNet.Broadcast;

namespace DoDrill
{
    // ============================================================
    //  ScenarioEndBroadcast — 서버 → 클라이언트 종료 신호
    //  (모듈화 2단계: 05_UI/ScenarioExitHandler.cs 에서 엔진으로 옮김 — 서버의 ScenarioRunner 가 보낸다)
    // ============================================================
    public struct ScenarioEndBroadcast : IBroadcast { }

    /// <summary>클라이언트 → 서버: 시나리오를 마친 뒤 '종료'를 눌렀다 — 방 전원을 메인 화면으로 돌려보내 달라.</summary>
    public struct ScenarioExitAllRequest : IBroadcast { }

    /// <summary>서버 → 클라이언트 전원: 지금 메인 화면으로 돌아간다.</summary>
    public struct ScenarioExitAllBroadcast : IBroadcast { }
}
