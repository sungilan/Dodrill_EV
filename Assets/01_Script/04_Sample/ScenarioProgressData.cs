using System;
using System.Collections.Generic;

namespace DoDrill.Training
{
    /// <summary>
    /// PersistentDataPath/progress.json 에 저장되는 전체 진행률 데이터
    /// </summary>
    [Serializable]
    public class AllProgressData
    {
        public List<ScenarioProgressEntry> entries = new();
    }

    [Serializable]
    public class ScenarioProgressEntry
    {
        public string scenarioId;
        public int completedTasks;
        public int totalTasks;

        /// <summary>모든 Task가 완료됐으면 true</summary>
        public bool isCompleted => totalTasks > 0 && completedTasks >= totalTasks;

        /// <summary>0.0 ~ 1.0 진행률 (Slider.value에 직접 사용)</summary>
        public float progressRate => totalTasks > 0 ? (float)completedTasks / totalTasks : 0f;

        /// <summary>진행률 % 텍스트 (예: "50%")</summary>
        public string progressPercent => $"{Mathf.RoundToInt(progressRate * 100)}%";

        // Mathf 사용을 위해 UnityEngine 참조
        private static class Mathf { public static int RoundToInt(float f) => (int)System.Math.Round(f); }
    }
}
