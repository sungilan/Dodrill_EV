using System.Collections.Generic;
using UnityEngine;

namespace DoDrill.Training
{
    /// <summary>
    /// 훈련 콘텐츠 리스트 패널.
    /// ScenarioDataLoader 로드 완료 시 카드 동적 생성.
    ///
    /// [Inspector 세팅]
    ///   Card Prefab  → ScenarioListCardUI 프리팹
    ///   Card Parent  → ScrollView > Viewport > Content
    ///   Max Cards    → 0 = JSON 수만큼 전부 / 1 이상 = 해당 개수만
    /// </summary>
    public class ScenarioListPanel : MonoBehaviour
    {
        [SerializeField] private ScenarioListCardUI cardPrefab;
        [SerializeField] private Transform          cardParent;

        [Tooltip("0 = 전부 출력 / 1 이상 = 해당 개수만")]
        [SerializeField] private int maxCards = 0;

        private readonly List<ScenarioListCardUI> _cards = new();

        private void Start()
        {
            if (ScenarioDataLoader.Instance == null) return;

            if (ScenarioDataLoader.Instance.IsLoaded)
                BuildCards(new List<ScenarioEntry>(ScenarioDataLoader.Instance.Entries));
            else
                ScenarioDataLoader.Instance.OnLoaded += BuildCards;
        }

        private void OnDestroy()
        {
            if (ScenarioDataLoader.Instance != null)
                ScenarioDataLoader.Instance.OnLoaded -= BuildCards;
        }

        private void BuildCards(List<ScenarioEntry> entries)
        {
            foreach (var c in _cards) if (c) Destroy(c.gameObject);
            _cards.Clear();

            int count = (maxCards > 0 && maxCards < entries.Count) ? maxCards : entries.Count;

            for (int i = 0; i < count; i++)
            {
                var entry    = entries[i];
                var progress = ScenarioProgressManager.Instance.GetProgress(entry.scenarioId);
                var card     = Instantiate(cardPrefab, cardParent);
                card.Bind(entry, progress);
                _cards.Add(card);
            }
        }

        /// <summary>특정 시나리오 카드 진행률만 갱신.</summary>
        public void RefreshCard(string scenarioId)
        {
            var card = _cards.Find(c => c.ScenarioId == scenarioId);
            if (card == null) return;
            card.RefreshProgress(ScenarioProgressManager.Instance.GetProgress(scenarioId));
        }

        /// <summary>전체 카드 진행률 새로 고침.</summary>
        public void RefreshAll()
        {
            foreach (var card in _cards)
                card.RefreshProgress(ScenarioProgressManager.Instance.GetProgress(card.ScenarioId));
        }
    }
}
