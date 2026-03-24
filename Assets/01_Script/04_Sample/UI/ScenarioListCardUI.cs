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

        public void Bind(ScenarioEntry entry, ScenarioProgressEntry progress)
        {
            ScenarioId = entry.scenarioId;

            if (titleText != null)
                titleText.text = entry.scenarioName;

            RefreshProgress(progress);
        }

        public void RefreshProgress(ScenarioProgressEntry progress)
        {
            if (progressSlider != null)
                progressSlider.value = progress.progressRate;

            if (progressPercent != null)
                progressPercent.text = $"{Mathf.RoundToInt(progress.progressRate * 100)}%";
        }
    }
}
