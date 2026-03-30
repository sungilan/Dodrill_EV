using UnityEngine;

// ============================================================
//  FreeModeRaycaster.cs
//  자유탈거 모드 PC 입력 처리
//
//  [클릭] 바라보는 InteractablePart를 탈거/재조립
//  [G키]  들고 있는 부품을 내려놓으면 인벤토리에 추가
//
//  씬 세팅:
//    - MainCamera에 부착 (또는 FreeLookController와 동일 오브젝트)
//    - FreeModeManager.IsFreeModeActive 가 true일 때만 동작
// ============================================================
[RequireComponent(typeof(Camera))]
public class FreeModeRaycaster : MonoBehaviour
{
    [Header("레이캐스트")]
    public float rayDistance = 5f;
    public LayerMask partLayerMask = ~0;

    [Header("부품 스폰 위치")]
    [Tooltip("부품을 손에서 놓을 때 잠깐 올려놓는 위치 (카메라 앞)")]
    public float holdDistance = 1.2f;

    [Header("하이라이트")]
    public Material highlightMaterial;

    private Camera _cam;
    private InteractablePart _hovered;
    private InteractablePart _holding;
    private Material[] _originalMats;

    // 부품을 들고 있을 때 부드럽게 따라다니게 할 Transform
    private Transform _holdPoint;

    private void Awake()
    {
        _cam = GetComponent<Camera>();

        // 홀드 포인트: 카메라 앞 고정 위치
        var go = new GameObject("FreeModeHoldPoint");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, -0.15f, holdDistance);
        _holdPoint = go.transform;
    }

    private void Update()
    {
        if (!FreeModeManager.IsActive) return;

        UpdateHover();
        HandleClick();
        MoveHeldPart();
    }

    // ── 호버 하이라이트 ────────────────────────────────────

    private void UpdateHover()
    {
        var hit = Raycast();

        InteractablePart newHovered = hit.HasValue
            ? hit.Value.collider.GetComponentInParent<InteractablePart>()
            : null;

        if (newHovered == _hovered) return;

        // 이전 하이라이트 해제
        if (_hovered != null) RestoreHighlight(_hovered);

        _hovered = newHovered;

        // 새 하이라이트
        if (_hovered != null && _hovered != _holding)
            ApplyHighlight(_hovered);
    }

    // ── 클릭: 탈거 or 내려놓기 ────────────────────────────

    private void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // 들고 있는 부품 → 내려놓기 → 인벤토리 추가
        if (_holding != null)
        {
            DropToInventory();
            return;
        }

        // 부품 클릭 → 탈거
        if (_hovered != null)
            TryDetach(_hovered);
    }

    // ── 탈거 ───────────────────────────────────────────────

    private void TryDetach(InteractablePart part)
    {
        if (part.currentState == InteractablePart.PartState.Assembled)
        {
            // 볼트가 있으면 먼저 자동 해체 (자유 모드는 볼트 무시)
            part.ForceDetach();
        }

        if (part.currentState != InteractablePart.PartState.Detached) return;

        // 손에 집기
        _holding = part;
        RestoreHighlight(part);

        Debug.Log($"[FreeModeRaycaster] 집음: {part.name}");
    }

    // ── 이동: 들고 있는 부품을 홀드포인트에 맞춤 ───────────

    private void MoveHeldPart()
    {
        if (_holding == null) return;

        _holding.transform.position = Vector3.Lerp(
            _holding.transform.position,
            _holdPoint.position,
            Time.deltaTime * 12f);
    }

    // ── 내려놓기 → 인벤토리 ───────────────────────────────

    private void DropToInventory()
    {
        var partData = _holding.GetComponent<FreeModePartAttachment>()?.partData;

        if (partData != null)
        {
            FreeModeInventory.Instance?.AddPart(partData);
            Debug.Log($"[FreeModeRaycaster] 인벤토리 추가: {partData.displayName}");
        }
        else
        {
            Debug.LogWarning($"[FreeModeRaycaster] {_holding.name}에 FreeModePartAttachment 없음");
        }

        // 부품 숨기기 (인벤토리에 들어간 상태)
        _holding.gameObject.SetActive(false);
        _holding = null;
    }

    // ── 레이캐스트 유틸 ───────────────────────────────────

    private RaycastHit? Raycast()
    {
        Ray ray = _cam.ScreenPointToRay(new Vector2(Screen.width * .5f, Screen.height * .5f));
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, partLayerMask))
            return hit;
        return null;
    }

    // ── 하이라이트 유틸 ───────────────────────────────────

    private void ApplyHighlight(InteractablePart part)
    {
        if (highlightMaterial == null) return;
        var renderers = part.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++) mats[i] = highlightMaterial;
            r.materials = mats;
        }
    }

    private void RestoreHighlight(InteractablePart part)
    {
        // InteractablePart의 원본 머티리얼로 복원
        // (InteractablePart에 RestoreOriginalMaterials 공개 메서드가 있다고 가정)
        if (part.TryGetComponent<FreeModePartAttachment>(out var att))
            att.RestoreMaterials();
    }
}
