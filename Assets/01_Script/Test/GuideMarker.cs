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

    [Header("거리별 크기 (가까이 가면 작게, 멀리서는 크게)")]
    [Tooltip("이 거리(m)에서 원래 크기. 가까울수록 비례해서 작아진다.")]
    public float referenceDistance = 3f;
    public float minScale = 0.3f;
    public float maxScale = 1.6f;

    private Tween _arrowTween;
    private Vector3 _baseScale = Vector3.one;
    private bool _baseScaleSaved;

    // 대상이 움직이면(리프트 상승·하강 등) 마커도 같이 따라가도록 처음 간격을 유지한다
    private Transform _followTarget;
    private Vector3 _followOffset;

    /// <summary>따라다니는 대상(목표 지점). 없으면 null.</summary>
    public Transform FollowTarget => _followTarget;

    /// <summary>대상 물체를 따라다니게 한다. 생성 시점의 위치 차이를 그대로 유지.</summary>
    public void Follow(Transform target)
    {
        _followTarget = target;
        if (target != null) _followOffset = transform.position - target.position;
    }

    private void Awake()
    {
        _baseScale = transform.localScale;
        _baseScaleSaved = true;
    }

    // ── (11/19) 호버 라벨 양보 ──
    // 마우스로 대상 위에 올리면 '클릭하여 집기' 라벨이 뜨는데, 같은 자리의 가이드 이름표·분홍 아웃라인이 맨 앞에 그려져 라벨을 가렸다.
    // → 호버 중인 대상의 마커는 잠깐(호출이 끊기면 0.15초 뒤 복구) 이름표·아웃라인을 숨긴다. 화살표는 그대로 둔다.
    private static Collider s_hoverCol;
    private static float s_hoverUntil;
    private bool _yielding;

    /// <summary>이 콜라이더를 호버 중 — 해당 대상의 가이드 마커가 이름표·아웃라인을 잠시 숨긴다. 호버 동안 매 프레임 부른다.</summary>
    public static void SuppressFor(Collider col)
    {
        s_hoverCol = col;
        s_hoverUntil = Time.unscaledTime + 0.15f;
    }

    private bool IsHoveredTarget()
    {
        if (s_hoverCol == null || Time.unscaledTime > s_hoverUntil) return false;
        var h = s_hoverCol.transform;
        if (_followTarget != null && (h == _followTarget || h.IsChildOf(_followTarget) || _followTarget.IsChildOf(h))) return true;
        // 따라갈 대상이 없는 마커(스폰 지점 등) — 호버한 물체 바로 위·아래에 서 있으면 같은 대상으로 본다
        var b = s_hoverCol.bounds;
        b.Expand(new Vector3(0.2f, 2f, 0.2f));
        return b.Contains(transform.position);
    }

    private void UpdateYield()
    {
        bool y = IsHoveredTarget();
        if (y == _yielding) return;
        _yielding = y;
        if (nameCanvas != null) nameCanvas.SetActive(!y);
        if (highlightCircle != null)
        {
            var o = highlightCircle.GetComponent<Outlinable>();
            if (o != null) o.enabled = !y;
        }
    }

    // 가까이서 보면 화살표·라벨이 화면을 가득 채워 시야를 가리던 문제 방지
    private void LateUpdate()
    {
        if (_followTarget != null)
            transform.position = _followTarget.position + _followOffset;
        UpdateYield();

        var cam = Camera.main;
        if (cam == null || referenceDistance <= 0f) return;
        if (!_baseScaleSaved) { _baseScale = transform.localScale; _baseScaleSaved = true; }

        float d = Vector3.Distance(cam.transform.position, transform.position);
        float s = Mathf.Clamp(d / referenceDistance, minScale, maxScale);
        transform.localScale = _baseScale * s;
    }

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

        // 설명 배경을 호버 라벨(‘후드 래치’)과 같은 둥근 어두운 카드로, 차체에 가려지지 않게
        if (nameCanvas != null)
        {
            foreach (var img in nameCanvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                WorldHoverLabel.StyleCard(img);
            WorldHoverLabel.DrawOnTop(nameCanvas);
        }
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