using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ModuleRegistry.cs
//  moduleId(string) → ITaskModule 매핑 관리
//  서버 전용
//
//  새 모듈 추가 시 ScenarioBootstrapper.RegisterModules() 에 한 줄 추가
// ============================================================

    public class ModuleRegistry
    {
        private readonly Dictionary<string, ITaskModule> _modules = new();

        public void Register(ITaskModule module)
        {
            if (_modules.ContainsKey(module.ModuleId))
            {
                Debug.LogWarning($"[ModuleRegistry] 중복 모듈 ID: {module.ModuleId}");
                return;
            }

            _modules[module.ModuleId] = module;
            Debug.Log($"[ModuleRegistry] 등록: {module.ModuleId}");
        }

        /// <summary>
        /// moduleId 로 모듈을 찾아 반환
        /// 없으면 null + LogError
        /// </summary>
        public ITaskModule Get(string moduleId)
        {
            if (_modules.TryGetValue(moduleId, out var module))
                return module;

            Debug.LogError($"[ModuleRegistry] 모듈 없음: {moduleId} — ScenarioBootstrapper.RegisterModules() 에 등록했는지 확인");
            return null;
        }

        public bool Contains(string moduleId) => _modules.ContainsKey(moduleId);

        public int Count => _modules.Count;
    }