using UnityEngine;

// ============================================================
//  GhostManager.cs
//  부품 탈거 시 원래 위치에 반투명 고스트 표시.
//  수정: GetComponentInChildren으로 자식 MeshFilter도 탐색
// ============================================================
public class GhostManager : MonoBehaviour
{
    [Header("Ghost Settings")]
    public Material ghostMaterial;
    [Range(0, 1)] public float ghostAlpha = 0.3f;

    private GameObject _ghostObject;
    private Material _ghostMat;
    private Color _initialColor;

    public void CreateGhost()
    {
        // 1. 원본 메쉬 찾기 (시각적 고스트용)
        var mf = GetComponentInChildren<MeshFilter>();
        if(mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning($"[GhostManager] {name}: MeshFilter 없음 — 고스트 생성 스킵");
            return;
        }

        // 2. 고스트 오브젝트 껍데기 생성 및 위치 동기화
        _ghostObject = new GameObject(gameObject.name + "_Ghost");
        _ghostObject.transform.position = transform.position;
        _ghostObject.transform.rotation = transform.rotation;
        _ghostObject.transform.localScale = transform.lossyScale;

        // 3. 고스트에 MeshFilter / MeshRenderer부터 먼저 붙여주기! (순서 중요)
        var ghostMF = _ghostObject.AddComponent<MeshFilter>();
        ghostMF.mesh = mf.sharedMesh;

        var ghostMR = _ghostObject.AddComponent<MeshRenderer>();
        if(ghostMaterial != null)
        {
            ghostMR.material = ghostMaterial;
            var col = ghostMR.material.color;
            col.a = ghostAlpha; // ghostAlpha 변수가 클래스 내에 있다고 가정
            ghostMR.material.color = col;
            _ghostMat = ghostMR.material;
            _initialColor = col;
        }

        // 4. 스냅 감지 스크립트 및 트리거 콜라이더 추가
        SnapZoneTrigger snapTrigger = _ghostObject.AddComponent<SnapZoneTrigger>();
        snapTrigger.targetPart = this.GetComponent<InteractablePart>();

        BoxCollider triggerCol = _ghostObject.AddComponent<BoxCollider>();
        triggerCol.isTrigger = true;

        // ---------------------------------------------------------
        // 5. 트리거 크기 자동 계산 (원본 오브젝트 기준)
        // ---------------------------------------------------------

        // ★ 회전되어 있으면 AABB(바운딩 박스)가 왜곡되므로 원본의 회전을 잠시 0으로 풉니다.
        Quaternion originalRot = transform.rotation;
        transform.rotation = Quaternion.identity;

        // 고스트가 아닌 '원본(자기 자신)'의 렌더러들을 전부 가져옵니다.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        if(renderers.Length > 0)
        {
            Bounds totalBounds = renderers[0].bounds;
            for(int i = 1; i < renderers.Length; i++)
            {
                totalBounds.Encapsulate(renderers[i].bounds);
            }

            // 중심점 설정 (원본 바운드 중심을 로컬 좌표로 변환)
            triggerCol.center = transform.InverseTransformPoint(totalBounds.center);

            // 크기 설정 (절댓값 처리하여 스케일 버그 방지)
            triggerCol.size = new Vector3(
                Mathf.Abs(totalBounds.size.x / transform.lossyScale.x),
                Mathf.Abs(totalBounds.size.y / transform.lossyScale.y),
                Mathf.Abs(totalBounds.size.z / transform.lossyScale.z)
            );

            // 플레이어가 쉽게 맞출 수 있게 트리거 영역 1.2배 넉넉하게 확장
            triggerCol.size *= 1.2f;
        }
        else
        {
            triggerCol.size = Vector3.one;
        }

        // 원본 회전값 원상 복구
        transform.rotation = originalRot;

        // 6. 고스트 비활성화로 마무리
        _ghostObject.SetActive(false);
    }

    public void SetGhostActive(bool active)
    {
        if (_ghostObject != null) _ghostObject.SetActive(active);
    }

    public void UpdateGhostColor(Color newColor)
    {
        if (_ghostMat != null) _ghostMat.color = newColor;
    }

    public void ResetGhostColor()
    {
        if (_ghostMat != null) _ghostMat.color = _initialColor;
    }

    private void OnDestroy()
    {
        if (_ghostObject != null) Destroy(_ghostObject);
    }
}