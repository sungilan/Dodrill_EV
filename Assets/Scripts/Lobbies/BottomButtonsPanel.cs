using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// VR_UICanvas 하단 바 토글 컨트롤러.
/// 하단 바는 항상 노출, <see cref="slidePanel"/> 전체(예: Left_TaskListPanel)를 위아래 슬라이드 토글합니다.
/// </summary>
public class BottomButtonsPanel : MonoBehaviour
{
    [Header("Panel References")]
    [Tooltip("슬라이드로 올라오는 전체 패널 Rect")]
    [SerializeField] private RectTransform slidePanel;

    [Header("Bottom Bar")]
    [SerializeField] private Button toggleButton;   // 하단 바 클릭 시 토글
    [SerializeField] private TMP_Text currentTaskText;

    [Header("Slide Settings")]
    [Tooltip("패널이 완전히 올라왔을 때 anchoredPosition.y")]
    [SerializeField] private float openedY = 0f;
    [Tooltip("패널이 내려갔을 때 anchoredPosition.y (하단 바 높이만큼 남김)")]
    [SerializeField] private float closedY = -400f;
    [SerializeField] private float duration = 0.3f;
    [SerializeField] private bool startOpened = true;

    [Header("연출 · 다른 스크립트와 연결")]
    [Tooltip("패널 열림(true)/닫힘(false)이 바뀔 때마다 호출. ToggleButton·Animator·커스텀 연출을 여기 또는 코드에서 묶을 수 있습니다.")]
    [SerializeField] private UnityEvent<bool> onOpenStateChanged;

    private bool isOpen;
    private Coroutine animRoutine;

    private void Awake()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        isOpen = startOpened;
        ApplyImmediate();
        NotifyOpenStateChanged();
    }

    // ───────── Public API ─────────

    public void Toggle()
    {
        isOpen = !isOpen;

        if (animRoutine != null)
            StopCoroutine(animRoutine);

        animRoutine = StartCoroutine(Animate());
        NotifyOpenStateChanged();
    }

    /// <summary>
    /// FingerTriggerAreaToggleEvents 등에서 열림/닫힘을 직접 지정할 때 사용합니다.
    /// </summary>
    public void SetOpen(bool open)
    {
        if (isOpen == open) return;
        isOpen = open;
        if (animRoutine != null)
            StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(Animate());
        NotifyOpenStateChanged();
    }

    public void SetCurrentTaskText(string taskName)
    {
        if (currentTaskText != null)
            currentTaskText.text = taskName;
    }

    /// <summary>현재 패널이 펼쳐진 상태인지 (연출 스크립트 참고용).</summary>
    public bool IsOpen => isOpen;

    private void NotifyOpenStateChanged()
    {
        if (toggleButton != null)
        {
            var toggleVisual = toggleButton.GetComponent<ToggleButton>();
            if (toggleVisual != null)
                toggleVisual.SetSelected(isOpen);
        }

        onOpenStateChanged?.Invoke(isOpen);
    }

    // ───────── Animation ─────────

    private void ApplyImmediate()
    {
        if (slidePanel == null) return;

        Vector2 pos = slidePanel.anchoredPosition;
        pos.y = isOpen ? openedY : closedY;
        slidePanel.anchoredPosition = pos;
    }

    private IEnumerator Animate()
    {
        float startY = slidePanel.anchoredPosition.y;
        float endY   = isOpen ? openedY : closedY;
        float time   = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            t = 1f - Mathf.Pow(1f - t, 3f); // EaseOutCubic

            Vector2 pos = slidePanel.anchoredPosition;
            pos.y = Mathf.Lerp(startY, endY, t);
            slidePanel.anchoredPosition = pos;

            yield return null;
        }

        ApplyImmediate();
        animRoutine = null;
    }
}
