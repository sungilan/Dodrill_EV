// ⚠️ DEPRECATED: ScenarioSpawner + ScenarioRunner 연동 방식으로 대체됨
// 씬에서 이 컴포넌트를 제거하고 ScenarioSpawner를 사용하세요.
#if false

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using Sirenix.OdinInspector;
#endif

// ============================================================
//  TaskObjectSpawner.cs
//  Game 씬 전용 — 현재 Task에 맞는 아이템 + Zone 가이드만 표시
//
//  흐름:
//    ScenarioStateReceiver.OnTaskStateUpdated 수신
//    → HideAllItems() + HideAllGuides()
//    → 해당 moduleId의 아이템 MeshRenderer/Collider 활성화
//    → 해당 Zone 가이드 활성화
//
//  아이템 표시 방식:
//    SetActive 대신 MeshRenderer + non-Trigger Collider enabled 토글
//    → FishNet NetworkObject(scene object) 안전하게 유지
// ============================================================

namespace DoDrill
{
    public class TaskObjectSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SceneGuideRegistry    _registry;
        [SerializeField] private ScenarioStateReceiver _stateReceiver;

        [Header("Items (prefabId → Scene Object)")]
        [SerializeField] private List<ItemEntry> _items = new();

        [System.Serializable]
        public class ItemEntry
        {
            public string     prefabId;
            public GameObject itemObject;
        }

        // ── moduleId → 활성화할 zoneId ──────────────────────
        private static readonly Dictionary<string, string> _moduleToZone
            = new Dictionary<string, string>
        {
            { "MeasureBodyTemperature",      "PatientEar"          },
            { "MeasurePulse",                "PatientRadialArtery" },
            { "MeasureRespiration",          "PatientChest"        },
            { "MeasureBloodPressure",        "PatientUpperArm"     },
            { "MeasureBST",                  "PatientFinger"       },
            { "MeasureSpO2",                 "PatientFinger"       },
            { "AttachEKGElectrodes_LeadII",  "PatientChest"        },
            { "AdministerSublingual",        "PatientMouth"        },
            { "AdministerOral",              "PatientMouth"        },
            { "SelectInjectionSite",         "InjectionSite"       },
            { "DisinfectSite",               "InjectionSite"       },
            { "AdministerIM",                "InjectionSite"       },
            { "Start10DW_IV",                "IVPort"              },
        };

        // ── moduleId → 표시할 아이템 prefabId 목록 ──────────
        private static readonly Dictionary<string, string[]> _moduleToItems
            = new Dictionary<string, string[]>
        {
            { "HandHygiene",                new[] { "HandSanitizer" } },
            { "MeasureBodyTemperature",     new[] { "TympanicThermometer", "ThermometerCap" } },
            { "MeasurePulse",               new[] { "SecondHandWatch" } },
            { "MeasureRespiration",         new[] { "SecondHandWatch" } },
            { "MeasureBloodPressure",       new[] { "Sphygmomanometer", "Stethoscope" } },
            { "MeasureBST",                 new[] { "BSTMachine", "LancetDevice", "Lancet", "TestStrip", "AlcoholSwab" } },
            { "MeasureSpO2",                new[] { "PulseOximeter" } },
            { "AttachEKGElectrodes_LeadII", new[] { "Electrode" } },
            { "VerifyMedicationOrder",      new[] { "MedicationCard" } },
            { "PrepareMedication",          new[] { "MedicationCard", "Medication_Bearse", "WaterCup" } },
            { "PrepareMedication_IM",       new[] { "SterileSyringe", "Medication_Ketocin", "DistilledWater" } },
            { "ExplainMedication",          new[] { "MedicationCard" } },
            { "AdministerSublingual",       new[] { "Medication_Nitroglycerin" } },
            { "AdministerOral",             new[] { "Medication_Bearse", "WaterCup" } },
            { "SelectInjectionSite",        new string[0] },
            { "DisinfectSite",              new[] { "AlcoholSwab" } },
            { "AdministerIM",               new[] { "SterileSyringe" } },
            { "Start10DW_IV",               new[] { "IVFluid_10DW" } },
        };

        // ── 에디터 자동 등록 ─────────────────────────────────
#if UNITY_EDITOR
        [Button("① References 자동 연결", ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
        private void AutoFindReferences()
        {
            if (_registry == null)
                _registry = FindAnyObjectByType<SceneGuideRegistry>();
            if (_stateReceiver == null)
                _stateReceiver = FindAnyObjectByType<ScenarioStateReceiver>();

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[TaskObjectSpawner] References 자동 연결 완료 — registry:{_registry != null}, receiver:{_stateReceiver != null}");
        }

        [Button("② Items 자동 등록 (씬 이름 매칭)", ButtonSizes.Medium), GUIColor(0.4f, 1f, 0.6f)]
        private void AutoFindItems()
        {
            // 씬의 모든 GameObject 수집 (SM_ 접두사 제거 후 매핑)
            var allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var nameMap = new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var go in allObjects)
            {
                string key = go.name.StartsWith("SM_", System.StringComparison.OrdinalIgnoreCase)
                    ? go.name.Substring(3)
                    : go.name;
                if (!nameMap.ContainsKey(key))
                    nameMap[key] = go;
            }

            // 기존 리스트 유지하면서 미등록 항목만 추가
            var registeredIds = new HashSet<string>();
            foreach (var entry in _items)
                if (!string.IsNullOrEmpty(entry.prefabId))
                    registeredIds.Add(entry.prefabId);

            int added = 0, notFound = 0;
            foreach (var kvp in _moduleToItems)
            {
                foreach (var prefabId in kvp.Value)
                {
                    if (registeredIds.Contains(prefabId)) continue;

                    if (nameMap.TryGetValue(prefabId, out var found))
                    {
                        _items.Add(new ItemEntry { prefabId = prefabId, itemObject = found });
                        registeredIds.Add(prefabId);
                        added++;
                        Debug.Log($"[TaskObjectSpawner] 등록: {prefabId} → {found.name}");
                    }
                    else
                    {
                        notFound++;
                        Debug.LogWarning($"[TaskObjectSpawner] 씬에 없음: {prefabId}");
                    }
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[TaskObjectSpawner] 자동 등록 완료 — 추가:{added}, 미발견:{notFound}");
        }

        [Button("③ Items 리스트 초기화", ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.4f)]
        private void ClearItems()
        {
            _items.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[TaskObjectSpawner] Items 리스트 초기화 완료");
        }
#endif

        // ── 내부 ────────────────────────────────────────────
        private readonly Dictionary<string, GameObject> _itemMap = new();

        // ── 생명주기 ────────────────────────────────────────

        private void Awake()
        {
            foreach (var entry in _items)
                if (!string.IsNullOrEmpty(entry.prefabId) && entry.itemObject != null)
                    _itemMap[entry.prefabId] = entry.itemObject;
        }

        private void Start()
        {
            // 런타임 시작 시 전부 숨김 — 태스크 시작 이벤트에서 필요한 것만 표시
            HideAllItems();
        }

        private void OnEnable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshotReceived;
        }

        private void OnDisable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshotReceived;
        }

        // ── 이벤트 처리 ─────────────────────────────────────

        private void HandleTaskUpdated(TaskStateBroadcast broadcast)
        {
            if (broadcast.currentTask.status != TaskStatus.Running) return;
            Refresh(broadcast.currentTask.taskIndex);
        }

        private void HandleSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            if (broadcast.currentTask.status == TaskStatus.Running)
                Refresh(broadcast.currentTask.taskIndex);
        }

        // ── 갱신 ────────────────────────────────────────────

        private void Refresh(int taskIndex)
        {
            HideAllItems();
            _registry?.HideAll();

            string moduleId = ResolveModuleId(taskIndex);
            if (string.IsNullOrEmpty(moduleId)) return;

            // Zone 가이드 활성화
            if (_moduleToZone.TryGetValue(moduleId, out string zoneId))
            {
                _registry?.Show(zoneId);
                Debug.Log($"[TaskObjectSpawner] 가이드 활성화: {zoneId} ← {moduleId}");
            }

            // 아이템 활성화
            if (_moduleToItems.TryGetValue(moduleId, out string[] itemIds))
            {
                foreach (var id in itemIds)
                {
                    if (_itemMap.TryGetValue(id, out var go))
                    {
                        SetVisible(go, true);
                        Debug.Log($"[TaskObjectSpawner] 아이템 표시: {id}");
                    }
                    else
                    {
                        Debug.LogWarning($"[TaskObjectSpawner] 아이템 없음: {id} (Inspector에 등록되어 있는지 확인)");
                    }
                }
            }
        }

        // ── 전체 숨김 ────────────────────────────────────────

        private void HideAllItems()
        {
            foreach (var entry in _items)
                if (entry.itemObject != null)
                    SetVisible(entry.itemObject, false);
        }

        // ── Renderer / Collider 토글 ─────────────────────────
        // Trigger Collider(Zone 감지용)는 건드리지 않음

        private void SetVisible(GameObject go, bool visible)
        {
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
                mr.enabled = visible;

            foreach (var col in go.GetComponentsInChildren<Collider>())
                if (!col.isTrigger) col.enabled = visible;
        }

        // ── 유틸 ────────────────────────────────────────────

        private string ResolveModuleId(int taskIndex)
        {
            var scenario = _stateReceiver?.CurrentScenario;
            if (scenario != null && taskIndex < scenario.scenario.tasks.Count)
                return scenario.scenario.tasks[taskIndex].moduleId;
            return string.Empty;
        }
    }
}

#endif // DEPRECATED
