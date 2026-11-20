using UnityEngine;

// ============================================================
//  FreeModePartAttachment.cs
//  각 부품 GO에 부착 — PartDataSO 연결 + 머티리얼 원본 보관
//
//  씬 세팅:
//    - 탈거 가능한 모든 InteractablePart GO에 추가
//    - partData 필드에 해당 PartDataSO 드래그 연결
// ============================================================
public class FreeModePartAttachment : MonoBehaviour
{
    [Header("부품 데이터")]
    public PartDataSO partData;

    // 원본 머티리얼 캐시
    private Renderer[] _renderers;
    private Material[][] _originalMaterials;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _originalMaterials = new Material[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
            _originalMaterials[i] = _renderers[i].materials;
    }

    public void RestoreMaterials()
    {
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _originalMaterials[i];
    }
}
