using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 하단 바 중앙 원형 카운트다운 UI.
/// TaskState의 timeoutSeconds / elapsedTime 기반으로 게이지 및 숫자 갱신.
/// </summary>
public class CountdownUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _gaugeImage;        // timer_gauge.png, FillMethod: Radial360
    [SerializeField] private TextMeshProUGUI _countText;
    [SerializeField] private GameObject _root;         // 타임아웃 없을 때 숨길 루트

    private float _timeout;
    private float _elapsed;
    private bool _running;

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
    }

    private void Update()
    {
        if (!_running || _timeout <= 0f) return;

        _elapsed += Time.deltaTime;
        Refresh();
    }

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        Apply(broadcast.currentTask);
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        Apply(broadcast.currentTask);
    }

    private void Apply(TaskState state)
    {
        _timeout = state.timeoutSeconds;
        _elapsed = state.elapsedTime;
        _running = state.status == TaskStatus.Running && _timeout > 0f;

        if (_root != null)
            _root.SetActive(_timeout > 0f);

        Refresh();
    }

    private void Refresh()
    {
        if (_timeout <= 0f) return;

        float remaining = Mathf.Max(0f, _timeout - _elapsed);
        float fill = remaining / _timeout;

        if (_gaugeImage != null)
            _gaugeImage.fillAmount = fill;

        if (_countText != null)
            _countText.text = Mathf.CeilToInt(remaining).ToString();
    }
}
