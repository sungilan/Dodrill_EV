using TMPro;
using UnityEngine;
using DG.Tweening;

// ============================================================
//  GuideMarker.cs
//
//  프리팹 구조:
//    GuideMarker (root, 이 스크립트)
//      ├── HighlightCircle   — Renderer (색상 자동 적용)
//      ├── DescriptionText   — TextMeshPro (색상 자동 적용)
//      └── Arrow             — 위아래 애니메이션 (LineRenderer 색상 자동 적용)
//
//  GuideSystem.spawnGuideColor / targetGuideColor가
//  Setup(color) 호출로 모든 요소에 자동 반영됨.
//  Inspector에서 색상 목록 직접 연결 불필요.
// ============================================================
public class GuideMarker : MonoBehaviour
{
    [Header("내부 참조 (비워두면 자식에서 자동 탐색)")]
    public GameObject highlightCircle;
    public GameObject nameCanvas;
    public TextMeshProUGUI descriptionText;
    public GameObject arrow;

    private Tween _arrowTween;

    // ── 외부 API ─────────────────────────────────────────

    /// <summary>
    /// GuideSystem이 마커 생성 직후 호출.
    /// color = spawnGuideColor 또는 targetGuideColor
    /// </summary>
    public void Setup(string description, Color color)
    {
        // 자식에서 자동 탐색 (Inspector 연결 생략 가능)
        if (highlightCircle == null)
            highlightCircle = FindChild("HighlightCircle");
        if (descriptionText == null)
            descriptionText = GetComponentInChildren<TextMeshProUGUI>();
        if (arrow == null)
            arrow = FindChild("Arrow");

        // ① HighlightCircle — Renderer 색상
        if (highlightCircle != null)
        {
            // 자식들을 포함하여 모든 Outline 컴포넌트를 찾아 색상 적용
            var outlines = highlightCircle.GetComponentsInChildren<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();
            foreach (var outline in outlines)
            {
                // 직접 프로퍼티 setter를 호출해야 내부 needsUpdate가 true가 되어 셰이더에 반영됩니다.
                outline.OutlineColor = color;
            }
        }

        // ② DescriptionText — TMP 색상 + 텍스트
        if (descriptionText != null)
        {
            descriptionText.text = description;
            descriptionText.color = color;
        }

        // ③ Arrow — LineRenderer 색상 + 위아래 애니메이션
        if (arrow != null)
        {
            foreach (var lr in arrow.GetComponentsInChildren<LineRenderer>())
            {
                lr.startColor = color;
                lr.endColor = color;
            }
            // Renderer도 같이 적용 (메시 화살표 프리팹 대응)
            foreach (var r in arrow.GetComponentsInChildren<Renderer>())
                ApplyColor(r, color);

            _arrowTween = arrow.transform
                .DOLocalMoveY(0.25f, 0.7f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetRelative();
        }

        // 텍스트가 항상 카메라를 바라보도록
        if (nameCanvas != null)
            if (!nameCanvas.GetComponent<GuideLabelBillboard>())
                nameCanvas.gameObject.AddComponent<GuideLabelBillboard>();
    }

    /// <summary>넛지: 화살표 흔들기</summary>
    public void Nudge()
    {
        if (arrow != null)
            arrow.transform.DOShakePosition(1.2f, 0.05f, 10, 90f, false, true);
    }

    private void OnDestroy() => _arrowTween?.Kill();

    // ── 내부 유틸 ─────────────────────────────────────────

    private GameObject FindChild(string childName)
    {
        var t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    private void ApplyColor(Renderer r, Color color)
    {
        // 공유 머테리얼을 건드리지 않도록 인스턴스 복사
        if (r.material != null)
            r.material.color = color;
    }
}