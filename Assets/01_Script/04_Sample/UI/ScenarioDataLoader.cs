using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoDrill.Training
{
    /// <summary>
    /// JSON + 썸네일 로드 공용 유틸.
    /// Resources 폴더 기반 — 플랫폼 무관하게 동작
    ///
    /// [폴더 구조]
    ///   Assets/Resources/
    ///     Scenarios/
    ///       1_vital_signs_complex.json   ← TextAsset 으로 로드
    ///       2_spo2_ekg_monitor.json
    ///     Thumbnails/
    ///       1_vital_signs_complex.png    ← Texture2D 로 로드
    ///       2_spo2_ekg_monitor.png
    ///
    /// ※ scenario_manifest.txt 불필요 — Resources.LoadAll() 로 전체 로드
    /// </summary>
    public class ScenarioDataLoader : MonoBehaviour
    {
        public static ScenarioDataLoader Instance { get; private set; }

        public event Action<List<ScenarioEntry>> OnLoaded;

        private List<ScenarioEntry> _entries = new();
        public IReadOnlyList<ScenarioEntry> Entries => _entries;
        public bool IsLoaded { get; private set; }

        private const string ScenarioResourcePath = "Scenarios";
        private const string ThumbnailResourcePath = "Thumbnails";

        // ─────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartCoroutine(LoadAll());
        }

        // ─────────────────────────────────────────
        // 로드
        // ─────────────────────────────────────────

        private IEnumerator LoadAll()
        {
            _entries.Clear();
            IsLoaded = false;

            // Resources.LoadAll 은 동기 — 프레임 분산을 위해 foreach 에 yield 삽입
            var jsonAssets = Resources.LoadAll<TextAsset>(ScenarioResourcePath);

            if (jsonAssets == null || jsonAssets.Length == 0)
            {
                Debug.LogWarning($"[ScenarioDataLoader] Resources/{ScenarioResourcePath} 에 JSON 없음");
                IsLoaded = true;
                OnLoaded?.Invoke(_entries);
                yield break;
            }

            foreach (var asset in jsonAssets)
            {
                var data = JsonUtility.FromJson<ScenarioData>(asset.text);
                if (data == null) continue;

                // 진행률 초기화
                int total = data.scenario?.tasks?.Count ?? 0;
                var prog = ScenarioProgressManager.Instance.GetProgress(data.scenarioId);
                if (prog.totalTasks == 0 && total > 0)
                    ScenarioProgressManager.Instance.SaveProgress(data.scenarioId, 0, total);

                // 썸네일 — JSON 파일명(asset.name) 기준으로 로드
                // scenarioId 와 파일명이 다를 수 있으므로 asset.name 우선 사용
                Texture2D thumb = LoadThumbnail(asset.name);
                if (thumb == null)
                    thumb = LoadThumbnail(data.scenarioId); // 폴백

                _entries.Add(new ScenarioEntry
                {
                    scenarioId = data.scenarioId,
                    scenarioName = GetDisplayName(data),
                    thumbnail = thumb,
                });

                yield return null; // 프레임 분산
            }

            IsLoaded = true;
            OnLoaded?.Invoke(_entries);
            Debug.Log($"[ScenarioDataLoader] 로드 완료: {_entries.Count}개");
        }

        // ─────────────────────────────────────────
        // 썸네일 로드 (Resources 동기)
        // ─────────────────────────────────────────

        public Texture2D LoadThumbnail(string scenarioId)
        {
            string path = $"{ThumbnailResourcePath}/{scenarioId}";
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
                Debug.LogWarning($"[ScenarioDataLoader] 썸네일 없음: {path}");
            return tex;
        }

        // 기존 코루틴 방식 호환용 (외부에서 코루틴으로 호출하는 곳 있을 경우)
        public IEnumerator LoadThumbnail(string scenarioId, Action<Texture2D> callback)
        {
            callback(LoadThumbnail(scenarioId));
            yield break;
        }

        // ─────────────────────────────────────────
        // scenarioName 로컬라이즈
        // ─────────────────────────────────────────

        private string GetDisplayName(ScenarioData data)
        {
            if (data.scenarioName != null)
            {
                string name = data.scenarioName.GetLocalized();
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return data.scenarioId ?? "Unknown";
        }
    }

    /// <summary>로드된 시나리오 한 항목 (ScenarioListPanel / TrainingCreatePopup 공유)</summary>
    [Serializable]
    public class ScenarioEntry
    {
        public string scenarioId;
        public string scenarioName;
        public Texture2D thumbnail;
    }
}