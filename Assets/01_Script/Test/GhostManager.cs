using UnityEngine;

public class GhostManager : MonoBehaviour
{
    [Header("Ghost Settings")]
    public Material ghostMaterial; // 반투명한 하이라이트 머티리얼
    [Range(0, 1)] public float ghostAlpha = 0.3f;

    private GameObject ghostObject;
    private Material ghostMat;
    private Color initialColor;

    public void CreateGhost()
    {
        // 1. 새로운 게임 오브젝트 생성 및 이름 설정
        ghostObject = new GameObject(gameObject.name + "_Ghost");

        // 2. 위치와 회전값을 현재(조립된 상태)와 동일하게 설정
        ghostObject.transform.position = transform.position;
        ghostObject.transform.rotation = transform.rotation;
        ghostObject.transform.localScale = transform.localScale;

        // 3. 메쉬 필터와 렌더러 복제
        MeshFilter meshFilter = ghostObject.AddComponent<MeshFilter>();
        meshFilter.mesh = GetComponent<MeshFilter>().sharedMesh;

        MeshRenderer meshRenderer = ghostObject.AddComponent<MeshRenderer>();

        // 4. 고스트 전용 머티리얼 적용
        if(ghostMaterial != null)
        {
            meshRenderer.material = ghostMaterial;
            // 필요 시 런타임에서 알파값 조정
            Color col = meshRenderer.material.color;
            col.a = ghostAlpha;
            meshRenderer.material.color = col;
        }

        // 5. 고스트는 잡히거나 부딪히면 안 되므로 콜라이더는 넣지 않거나 트리거로 설정
        // 초기에는 꺼두었다가 부품이 탈거될 때만 켭니다.
        ghostObject.SetActive(false);
        ghostMat = meshRenderer.material;
        initialColor = ghostMat.color;
    }

    public void SetGhostActive(bool active)
    {
        if(ghostObject != null) ghostObject.SetActive(active);
    }

    public void UpdateGhostColor(Color newColor)
    {
        if(ghostMat != null) ghostMat.color = newColor;
    }

    public void ResetGhostColor()
    {
        if(ghostMat != null) ghostMat.color = initialColor;
    }
}