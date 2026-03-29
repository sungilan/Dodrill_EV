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
        // 자식 포함해서 MeshFilter 탐색
        var mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning($"[GhostManager] {name}: MeshFilter 없음 — 고스트 생성 스킵");
            return;
        }

        _ghostObject = new GameObject(gameObject.name + "_Ghost");
        _ghostObject.transform.position = transform.position;
        _ghostObject.transform.rotation = transform.rotation;
        _ghostObject.transform.localScale = transform.lossyScale;

        var ghostMF = _ghostObject.AddComponent<MeshFilter>();
        ghostMF.mesh = mf.sharedMesh;

        var ghostMR = _ghostObject.AddComponent<MeshRenderer>();
        if (ghostMaterial != null)
        {
            ghostMR.material = ghostMaterial;
            var col = ghostMR.material.color;
            col.a = ghostAlpha;
            ghostMR.material.color = col;
            _ghostMat = ghostMR.material;
            _initialColor = col;
        }

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