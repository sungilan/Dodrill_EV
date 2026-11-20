using FishNet.Broadcast;

namespace DoDrill
{
    // ============================================================
    //  ScenarioEndBroadcast — 서버 → 클라이언트 종료 신호
    //  (모듈화 2단계: 05_UI/ScenarioExitHandler.cs 에서 엔진으로 옮김 — 서버의 ScenarioRunner 가 보낸다)
    // ============================================================
    public struct ScenarioEndBroadcast : IBroadcast { }
}
