using TMPro;
using UnityEngine;
using DG.Tweening;
using EPOOutline;

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
    public TextMeshProUGUI _labelText;
    public GameObject arrow;

    private Tween _arrowTween;

    // ── 외부 API ─────────────────────────────────────────

    /// <summary>
    /// GuideSystem이 마커 생성 직후 호출.
    /// color = spawnGuideColor 또는 targetGuideColor
    /// </summary>
    public void Setup(string description, Color color, bool showHighlight = true)
    {
        if(highlightCircle == null) highlightCircle = FindChild("HighlightCircle");
        if(descriptionText == null) descriptionText = GetComponentInChildren<TextMeshProUGUI>();
        if(arrow == null) arrow = FindChild("Arrow");

        // ① HighlightCircle — EPOOutline 적용
        if(highlightCircle != null)
        {
            highlightCircle.SetActive(showHighlight);

            // Outlinable 컴포넌트 확보
            var outlinable = highlightCircle.GetComponent<Outlinable>();
            if(outlinable == null)
            {
                outlinable = highlightCircle.AddComponent<Outlinable>();
                outlinable.AddAllChildRenderersToRenderingList();
            }

            outlinable.RenderStyle = RenderStyle.Single;
            outlinable.OutlineParameters.Color = color;
            outlinable.OutlineParameters.Enabled = true;
            outlinable.enabled = true;
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

    // ★ 진행도 텍스트 추가
    public void SetProgressText(string progressText)
    {
        if(_labelText != null)
        {
            _labelText.text += $"\n<size=80%>{progressText}</size>";
        }
    }

    // ★ 진행도만 업데이트
    public void UpdateProgressText(string progressText)
    {
        // 기존 텍스트에서 진행도 부분만 교체
        if(_labelText != null)
        {
            string[] parts = _labelText.text.Split('\n');
            _labelText.text = parts[0] + $"\n<size=80%>{progressText}</size>";
        }
    }
}

public class GuideLabelBillboard : MonoBehaviour
{
    private Camera _cam;
    private void Start() => _cam = Camera.main;
    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        transform.LookAt(transform.position + _cam.transform.rotation * Vector3.forward,
                         _cam.transform.rotation * Vector3.up);
    }
}