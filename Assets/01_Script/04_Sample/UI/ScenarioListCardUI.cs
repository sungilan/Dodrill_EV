using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoDrill.Training
{
    /// <summary>
    /// 훈련 콘텐츠 리스트 카드 한 장.
    /// 제목 + 진행률 바 + 퍼센트 텍스트만 표시.
    ///
    /// [Inspector 세팅]
    ///   Title Text       → TMP_Text  (카드 제목)
    ///   Progress Slider  → Slider    (진행률 바, 0~1)
    ///   Progress Percent → TMP_Text  ("50%" 형식)
    /// </summary>
    public class ScenarioListCardUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Slider   progressSlider;
        [SerializeField] private TMP_Text progressPercent;

        public string ScenarioId { get; private set; }

        // (11/20) 훈련 리스트 카드의 진행률(%)·게이지는 쓰지 않는다 — 다시 보이려면 true. 진행률 저장 자체(ScenarioProgressManager)는 그대로 둔다.
        private const bool ShowProgress = false;

        private void Awake() => HideProgress();

        private void HideProgress()
        {
            if (ShowProgress) return;
            if (progressSlider != null) progressSlider.gameObject.SetActive(false);
            if (progressPercent != null) progressPercent.gameObject.SetActive(false);
            // '진행률(%)' 라벨 — 이름(Progress…)이나 글자(진행률/Progress)로 찾는다(번역으로 글자가 늦게 바뀔 수 있어 이름도 본다)
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == titleText) continue;
                string n = t.gameObject.name ?? "";
                string x = t.text ?? "";
                if (n.IndexOf("Progress", System.StringComparison.OrdinalIgnoreCase) >= 0 || x.Contains("진행률") || x.IndexOf("Progress", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
            }
        }

        public void Bind(ScenarioEntry entry, ScenarioProgressEntry progress)
        {
            ScenarioId = entry.scenarioId;

            if (titleText != null)
                titleText.text = entry.scenarioName;

            HideProgress();
            RefreshProgress(progress);
        }

        public void RefreshProgress(ScenarioProgressEntry progress)
        {
            if (!ShowProgress) return;
            if (progressSlider != null)
                progressSlider.value = progress.progressRate;

            if (progressPercent != null)
                progressPercent.text = $"{Mathf.RoundToInt(progress.progressRate * 100)}%";
        }
    }
}
