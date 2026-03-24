// ============================================================
//  ITaskModule.cs
//  서버 전용 — 모든 Task 모듈의 공통 인터페이스
//
//  설계 원칙:
//    - 서버에서만 인스턴스화 / 실행
//    - 시각효과 필요 시 TaskEffectBroadcast 로 클라이언트에 위임
//    - 완료 판정은 SignalComplete() 호출 → ScenarioRunner 가 처리
//
//  새 모듈 추가:
//    1. ITaskModule 구현
//    2. ModuleRegistry.Register() 에 등록
// ============================================================

    public interface ITaskModule
    {
        /// <summary>JSON moduleId 와 일치해야 함</summary>
        string ModuleId { get; }

        /// <summary>
        /// Task 시작 시 서버에서 1회 호출
        /// config: JSON 의 ModuleConfig (requiredItems, targetZoneId, extra)
        /// onComplete: 완료 조건 충족 시 호출 (ScenarioRunner 로 전달됨)
        /// onFail: 실패 조건 충족 시 호출
        /// </summary>
        void OnStart(ModuleConfig config, System.Action onComplete, System.Action onFail);

        /// <summary>매 서버 프레임 호출 (타임아웃 체크 등)</summary>
        void OnUpdate(float deltaTime);

        /// <summary>Task 완료 시 정리 작업</summary>
        void OnComplete();

        /// <summary>Task 실패 시 정리 작업</summary>
        void OnFail();
    }