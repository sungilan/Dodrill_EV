using System;
using System.Collections.Generic;
using FishNet.Broadcast;

/// <summary>
/// 세션 플레이어 한 명 (서버 기준 ClientId 오름차순 정렬).
/// </summary>
[Serializable]
public struct SessionPlayerEntry
{
    public int clientId;
    public string displayName;
    /// <summary>0=VR, 1=PC, 2=Mobile (<see cref="SessionPlayersListSync.PlatformToByte"/>)</summary>
    public byte platform;
}

/// <summary>서버 → 전체 클라이언트: 접속자 목록 전체 스냅샷</summary>
public struct SessionPlayersListBroadcast : IBroadcast
{
    public List<SessionPlayerEntry> players;
    /// <summary>현재 태스크를 진행 중인 클라이언트 ID (-1 = 진행중 아님)</summary>
    public int activePlayerId;
}

/// <summary>클라이언트 → 서버: 닉네임·플랫폼 보고 (서버는 연결의 ClientId만 신뢰)</summary>
public struct ClientSessionInfoBroadcast : IBroadcast
{
    public string displayName;
    public byte platform;
}
