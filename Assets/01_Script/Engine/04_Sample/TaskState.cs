using System;
using System.Collections.Generic;
using FishNet.Broadcast;

// ============================================================
//  TaskState.cs
//  런타임 Task 상태 — ScenarioRunner 가 서버에서 관리
//  TaskStateBroadcast 로 전체 클라이언트에 동기화
// ============================================================

public enum TaskStatus
{
    Pending,     // 아직 시작 안 됨
    Running,     // 진행 중
    Completed,   // 완료
    Failed,      // 실패 (타임아웃 or ForceFail)
}

// ──────────────────────────────────────────
//  단일 Task 런타임 상태
// ──────────────────────────────────────────
[Serializable]
public struct TaskState
{
    public int taskIndex;
    public TaskStatus status;
    public float elapsedTime;          // 경과 시간 (초)
    public float timeoutSeconds;       // 0 = 무제한
    public List<int> completedClientIds;   // 완료 신호 보낸 클라이언트 목록
    public int retryCount;             // 현재 Task 재도전 횟수
    public int currentStepIndex;       // 다단계 Task: 현재 스텝 인덱스 (0-based)
    public int totalSteps;             // 다단계 Task: 전체 스텝 수 (0 = 단일 인터랙션)
    public int moduleId;
    public int activePlayerId;
}

// ──────────────────────────────────────────
//  서버 → 클라이언트 상태 동기화
// ──────────────────────────────────────────
public struct TaskStateBroadcast : IBroadcast
{
    public TaskState currentTask;
    public int totalTaskCount;
}

// ──────────────────────────────────────────
//  신규 접속자용 스냅샷 (현재 전체 상태)
// ──────────────────────────────────────────
public struct ScenarioSnapshotBroadcast : IBroadcast
{
    public ScenarioData scenarioData;
    public TaskState currentTask;
    public int totalTaskCount;
}

// ──────────────────────────────────────────
//  클라이언트 → 서버 완료 신호
// ──────────────────────────────────────────
public struct TaskCompleteSignal : IBroadcast
{
    public int taskIndex; // 어느 Task 를 완료했는지
    public int clientId;
}

// ──────────────────────────────────────────
//  클라이언트 → 서버: 아이템 사용(Use/펌프) 신호
// ──────────────────────────────────────────
public struct ItemUsedSignal : IBroadcast
{
    public int taskIndex;
    public string itemId;   // prefabId
    public int clientId;
}

// ──────────────────────────────────────────
//  클라이언트 → 서버: 아이템 집기(Grab) 신호
// ──────────────────────────────────────────
public struct ItemGrabbedSignal : IBroadcast
{
    public int taskIndex;
    public string itemId;   // prefabId
    public int clientId;
}

// ──────────────────────────────────────────
//  클라이언트 → 서버: UI 완료 확인 신호
//  (ConfirmTaskModule, PC/Mobile 폴백)
// ──────────────────────────────────────────
public struct TaskConfirmSignal : IBroadcast
{
    public int taskIndex;
    public string moduleId;
    public int clientId;
}

// ──────────────────────────────────────────
//  클라이언트 → 서버: 존 인터랙션 신호
//  (클라이언트 물리 트리거 → 서버 검증)
// ──────────────────────────────────────────
public struct ZoneInteractionSignal : IBroadcast
{
    public int taskIndex;
    public string zoneId;
    public string itemId;
    public int clientId;
}

// ──────────────────────────────────────────
//  서버 → 전체 클라이언트: DOTween 스냅 애니메이션 지시
//  서버가 존 인터랙션을 검증한 뒤 브로드캐스트
// ──────────────────────────────────────────
public struct PlaySnapAnimBroadcast : IBroadcast
{
    public int taskIndex;
    public string itemId;       // 스냅할 아이템 prefabId
    public string guideId;      // 목표 위치 (TaskInteractionZone.zoneId)
    public float snapDuration;  // DOTween 이동 시간 (초)
}

// ──────────────────────────────────────────
//  클라이언트 → 서버: 멀티미터 측정값 신호
//  (ZoneAndMeasureModule 검증용)
// ──────────────────────────────────────────
public struct ValueMeasuredSignal : IBroadcast
{
    public int taskIndex;      // 현재 진행 중인 태스크 인덱스
    public string terminalId;  // 측정된 단자 ID (예: "HV_Cable_Term")
    public float value;        // 측정된 실제 값 (V 또는 Ω)
    public int clientId;       // 신호를 보낸 클라이언트 ID
}