using FishNet.Broadcast;

// ============================================================
//  ScenarioDataBroadcast.cs
//  FishNet Broadcast 메시지 구조체
//
//  서버→클라이언트 단방향 전송 전용
//  ScenarioDistributor 가 송신, ScenarioReceiver 가 수신
// ============================================================

    public struct ScenarioDataBroadcast : IBroadcast
    {
        public ScenarioData data;
    }