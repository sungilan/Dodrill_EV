using System;
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

        // ── 범용 모듈 종류(type) ─────────────────────────────
        //  JSON 의 modules[].config.type 에 종류 이름을 적으면, 그 moduleId 로 등록된 C# 모듈이 없어도
        //  종류별 공장(factory)이 단계마다 새 모듈을 만들어 준다. → 새 콘텐츠는 JSON 만으로 단계를 만든다.
        //  엔진 기본 종류는 GenericModules.RegisterTypes 가 등록한다(Confirm, GrabToZone, UseItem, GrabAll, Measure, Wait).
        private readonly Dictionary<string, Func<string, ModuleConfig, ITaskModule>> _types =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>범용 모듈 종류를 등록한다. 같은 이름은 먼저 등록된 것이 이긴다.</summary>
        public void RegisterType(string type, Func<string, ModuleConfig, ITaskModule> factory)
        {
            if (string.IsNullOrEmpty(type) || factory == null) return;
            if (_types.ContainsKey(type)) return;
            _types[type] = factory;
        }

        public bool HasType(string type) => !string.IsNullOrEmpty(type) && _types.ContainsKey(type);

        public IEnumerable<string> TypeNames => _types.Keys;

        /// <summary>
        /// 단계에 쓸 모듈을 찾는다.
        ///  1) moduleId 로 등록된 C# 모듈(EV 등 콘텐츠 전용 모듈)
        ///  2) config.type 이 가리키는 범용 모듈
        ///  3) moduleId 자체가 범용 종류 이름인 경우(예: "Wait")
        /// 없으면 null + LogError.
        /// </summary>
        public ITaskModule Resolve(string moduleId, ModuleConfig config)
        {
            if (!string.IsNullOrEmpty(moduleId) && _modules.TryGetValue(moduleId, out var module))
                return module;

            string type = !string.IsNullOrEmpty(config?.type) ? config.type : moduleId;
            if (!string.IsNullOrEmpty(type) && _types.TryGetValue(type, out var factory))
            {
                var created = factory(moduleId, config);
                if (created != null)
                {
                    Debug.Log($"[ModuleRegistry] 범용 모듈 사용: {moduleId} → {type}");
                    return created;
                }
            }

            Debug.LogError($"[ModuleRegistry] 모듈 없음: {moduleId}" +
                           (string.IsNullOrEmpty(config?.type) ? "" : $" (type '{config.type}' 도 등록되지 않음)") +
                           $" — 등록된 범용 종류: {string.Join(", ", _types.Keys)}");
            return null;
        }

        public int Count => _modules.Count;
    }