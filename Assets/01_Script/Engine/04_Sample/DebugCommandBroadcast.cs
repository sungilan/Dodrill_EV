using FishNet.Broadcast;

// ============================================================
//  DebugCommandBroadcast.cs
//  FishNet 메시지: 클라이언트 → 서버 커맨드 전달
// ============================================================

    public struct DebugCommandBroadcast : IBroadcast
    {
        public string commandKey; // "{Category}/{Name}" 형식
        public string argsJson;  // args 배열을 JSON으로 직렬화
        public int    callerId;  // 호출한 클라이언트 Connection ID
    }