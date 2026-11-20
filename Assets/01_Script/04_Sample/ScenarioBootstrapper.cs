using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioBootstrapper.cs
//  씬의 빈 GameObject 에 붙여서 사용
//  모든 ITaskModule 구현체를 여기서 등록
//
//  새 모듈 묶음은 콘텐츠 쪽에서 ScenarioHooks.AddModuleProvider() 로 등록(엔진은 콘텐츠를 모른다)
// ============================================================

    public class ScenarioBootstrapper : MonoBehaviour
    {
        public ModuleRegistry Registry { get; private set; }

        private void Awake()
        {
            Registry = new ModuleRegistry();
            RegisterModules();
        }

        // ──────────────────────────────────────
        //  모듈 등록 — ScenarioHooks 의 공급자들이 순서대로 채운다
        // ──────────────────────────────────────
        private void RegisterModules()
        {
            // 콘텐츠 모듈은 각 콘텐츠가 ScenarioHooks.AddModuleProvider 로 등록해 둔다
            //   간호술기 → 04_Sample/Modules/NursingModuleSet.cs / EV → Test/EvScenarioBinding.cs
            int providers = ScenarioHooks.ApplyModuleProviders(Registry);
            if (providers == 0)
                Debug.LogWarning("[ScenarioBootstrapper] 등록된 모듈 공급자가 없습니다 — 콘텐츠의 ScenarioHooks.AddModuleProvider 등록(EvScenarioBinding 등)이 실행됐는지 확인");

        Debug.Log($"[ScenarioBootstrapper] 총 {Registry.Count}개 모듈 등록 완료");
        }
    }