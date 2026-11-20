using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DoDrill.Training
{
    /// <summary>
    /// 진행률 저장/불러오기 싱글톤.
    /// 씬 어딘가 (또는 DontDestroyOnLoad 오브젝트)에 하나만 붙여두면 됨.
    ///
    /// [ScenarioRunner 연동 - 한 줄만 추가]
    ///   ScenarioProgressManager.Instance.SaveProgress(scenarioId, completedCount, totalCount);
    /// </summary>
    public class ScenarioProgressManager : MonoBehaviour
    {
        public static ScenarioProgressManager Instance { get; private set; }

        private const string FileName = "progress.json";
        private string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        private AllProgressData _data = new();

        // ─────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        // ─────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────

        /// <summary>Task 완료 시점마다 호출. 자동 저장.</summary>
        public void SaveProgress(string scenarioId, int completedTasks, int totalTasks)
        {
            var entry = GetOrCreate(scenarioId);
            entry.completedTasks = completedTasks;
            entry.totalTasks     = totalTasks;
            Save();
        }

        /// <summary>특정 시나리오 진행률 조회 (없으면 0/0 entry 생성).</summary>
        public ScenarioProgressEntry GetProgress(string scenarioId) => GetOrCreate(scenarioId);

        /// <summary>전체 기록 반환.</summary>
        public List<ScenarioProgressEntry> GetAll() => _data.entries;

        /// <summary>특정 시나리오 진행률 초기화.</summary>
        public void ResetProgress(string scenarioId)
        {
            var entry = _data.entries.Find(e => e.scenarioId == scenarioId);
            if (entry == null) return;
            entry.completedTasks = 0;
            Save();
        }

        /// <summary>전체 초기화.</summary>
        public void ResetAll() { _data = new AllProgressData(); Save(); }

        // ─────────────────────────────────────────
        // 내부
        // ─────────────────────────────────────────

        private ScenarioProgressEntry GetOrCreate(string scenarioId)
        {
            var entry = _data.entries.Find(e => e.scenarioId == scenarioId);
            if (entry != null) return entry;
            entry = new ScenarioProgressEntry { scenarioId = scenarioId };
            _data.entries.Add(entry);
            return entry;
        }

        private void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(_data, prettyPrint: true));
            Debug.Log($"[ProgressManager] 저장: {FilePath}");
        }

        private void Load()
        {
            if (!File.Exists(FilePath)) { Debug.Log("[ProgressManager] 저장 파일 없음 → 새로 시작"); return; }
            _data = JsonUtility.FromJson<AllProgressData>(File.ReadAllText(FilePath)) ?? new AllProgressData();
            Debug.Log($"[ProgressManager] 불러오기: {_data.entries.Count}개");
        }
    }
}
