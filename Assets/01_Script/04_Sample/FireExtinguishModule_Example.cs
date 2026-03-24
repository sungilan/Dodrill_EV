using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  FireExtinguishModule.cs
//  ITaskModule 구현 예시
//
//  이 파일을 참고해서 새 모듈을 만드세요.
//  만든 후 ScenarioBootstrapper.RegisterModules() 에 등록하면 끝.
// ============================================================

    public class FireExtinguishModule : ITaskModule
    {
        public string ModuleId => "fire_extinguish"; // JSON taskDef.moduleId 와 일치

        private System.Action _onComplete;
        private System.Action _onFail;
        private ModuleConfig  _config;

        // ── ITaskModule 구현 ──────────────────

        public void OnStart(ModuleConfig config, System.Action onComplete, System.Action onFail)
        {
            _config     = config;
            _onComplete = onComplete;
            _onFail     = onFail;

            // config.extra 에서 설정값 읽기
            string targetZone = config?.targetZoneId ?? "fire_zone_01";

            Debug.Log($"[FireExtinguishModule] 시작 — targetZone: {targetZone}");

            // TODO: 필요한 초기화 (이벤트 구독 등)
            // FireZone.OnExtinguished += HandleExtinguished;
        }

        public void OnUpdate(float deltaTime)
        {
            // 매 프레임 판정이 필요한 로직
            // 예: 진화 진행률 체크, 추가 실패 조건 등
        }

        public void OnComplete()
        {
            Debug.Log("[FireExtinguishModule] 완료 정리");
            // TODO: 이벤트 구독 해제
            // FireZone.OnExtinguished -= HandleExtinguished;
        }

        public void OnFail()
        {
            Debug.Log("[FireExtinguishModule] 실패 정리");
            // TODO: 이벤트 구독 해제
        }

        // ── 내부 로직 ─────────────────────────

        // 예시: 외부 이벤트로 완료 판정
        // private void HandleExtinguished()
        // {
        //     _onComplete?.Invoke();
        // }
    }