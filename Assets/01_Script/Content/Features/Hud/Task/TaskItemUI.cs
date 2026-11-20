using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  TaskItemUI.cs
//  TaskItemPrefab 에 붙이는 컴포넌트
//
//  프리팹 구조 (권장):
//    TaskItemPrefab (HorizontalLayoutGroup)
//    ├── IndexText   (TMP)  "1."
//    ├── TitleText   (TMP)  "손 위생 실시"
//    └── CheckBox    (GameObject)
//        ├── EmptyIcon  (Image) — 미완료 빈 박스
//        ├── CheckIcon  (Image) — 완료 체크 표시
//        └── FailIcon   (Image) — 실패 X 표시
//
//  TitleText TMP 설정 (Inspector):
//    - Font Size : 고정값 직접 지정 (Auto Size OFF)
//    - Overflow  : Ellipsis  ← 줄임표(...) 처리
//    - Wrapping  : Disabled  ← 한 줄 고정
// ============================================================

public class TaskItemUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI _indexText;
    [SerializeField] private TextMeshProUGUI _titleText;

    [Tooltip("완료 시 표시되는 체크 아이콘 오브젝트")]
    [SerializeField] private GameObject _checkIcon;

    [Tooltip("미완료 시 표시되는 빈 박스 오브젝트")]
    [SerializeField] private GameObject _emptyIcon;

    [Tooltip("실패 시 표시되는 X 아이콘 오브젝트 (없으면 EmptyIcon 빨간색으로 폴백)")]
    [SerializeField] private GameObject _failIcon;

    [Header("Index 너비 설정")]
    [Tooltip("숫자 한 자리당 추가되는 너비 (px)")]
    [SerializeField] private float _indexCharWidth = 20f;

    [Tooltip("IndexText 기본 패딩 (px)")]
    [SerializeField] private float _indexBasePadding = 10f;

    private TaskStatus _currentStatus = TaskStatus.Pending;

    // ── Setup ─────────────────────────────

    public void Setup(int index, string title, TaskStatus status)
    {
        if (_indexText != null)
        {
            _indexText.text = $"{index}.";
            AdjustIndexWidth(index);
        }

        if (_titleText != null)
        {
            _titleText.text = title;
            // 폰트 크기/줄임표는 TMP Inspector 설정에 위임 — 코드 개입 없음
        }

        _currentStatus = TaskStatus.Pending;
        SetIconState(IconState.Empty);

        // ContentSizeFitter 레이아웃 리빌드 (마지막 아이템 삐뚤어짐 방지)
        StartCoroutine(ForceLayoutRebuild());
    }

    // ── 레이아웃 강제 리빌드 ──────────────

    private IEnumerator ForceLayoutRebuild()
    {
        yield return null;
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    // ── Index 너비 자동 조절 ──────────────

    private void AdjustIndexWidth(int index)
    {
        if (_indexText == null) return;

        // 항상 2자리 기준으로 고정 (1~9번도 10번과 동일한 너비)
        int digits = Mathf.Max(index.ToString().Length, 2);
        float width = _indexBasePadding + digits * _indexCharWidth;

        var le = _indexText.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.preferredWidth = width;
        }
        else
        {
            var rt = _indexText.GetComponent<RectTransform>();
            var size = rt.sizeDelta;
            rt.sizeDelta = new Vector2(width, size.y);
        }
    }

    // ── 상태 변경 ─────────────────────────

    public void SetStatus(TaskStatus status)
    {
        if (_currentStatus == status) return;
        _currentStatus = status;

        switch (status)
        {
            case TaskStatus.Pending:
            case TaskStatus.Running:
                SetIconState(IconState.Empty);
                break;

            case TaskStatus.Failed:
                SetIconState(IconState.Fail);
                break;

            case TaskStatus.Completed:
                StartCoroutine(CheckAnimation());
                break;
        }
    }

    // ── 아이콘 상태 제어 ──────────────────

    private enum IconState { Empty, Check, Fail }

    private void SetIconState(IconState state)
    {
        if (_checkIcon != null) _checkIcon.SetActive(state == IconState.Check);
        if (_emptyIcon != null) _emptyIcon.SetActive(state == IconState.Empty);
        if (_failIcon != null) _failIcon.SetActive(state == IconState.Fail);

        // FailIcon이 없으면 EmptyIcon 빨간색으로 폴백
        if (_failIcon == null && _emptyIcon != null)
        {
            var img = _emptyIcon.GetComponent<Image>();
            if (img != null)
                img.color = state == IconState.Fail
                    ? new Color(1f, 0.3f, 0.3f, 1f)
                    : Color.white;
        }
    }

    // ── 체크 팝 애니메이션 ────────────────

    private IEnumerator CheckAnimation()
    {
        SetIconState(IconState.Check);

        if (_checkIcon == null) yield break;

        _checkIcon.transform.localScale = Vector3.zero;

        float t = 0f;
        const float duration = 0.2f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            float scale = 1f + Mathf.Sin(p * Mathf.PI) * 0.25f;
            _checkIcon.transform.localScale = Vector3.one * Mathf.Clamp(scale, 0f, 1.3f);
            yield return null;
        }

        _checkIcon.transform.localScale = Vector3.one;
    }
}