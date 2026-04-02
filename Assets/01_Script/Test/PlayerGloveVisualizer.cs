using UnityEngine;

public class PlayerGloveVisualizer : MonoBehaviour
{
    public static PlayerGloveVisualizer Instance { get; private set; }

    [Header("Hand Mesh Renderers")]
    public SkinnedMeshRenderer leftHandMesh;
    public SkinnedMeshRenderer rightHandMesh;

    [Header("Materials")]
    public Material bareHandMaterial;       // ¸Ç¼Õ ÀçÁú
    public Material insulatedGloveMaterial; // Àý¿¬ Àå°© ÀçÁú

    private void Awake() => Instance = this;

    /// <summary>
    /// Àå°© Âø¿ë/ÇØÁ¦ ½Ã°¢ ¿¬Ãâ
    /// </summary>
    public void ApplyGlove(bool equipped)
    {
        Material targetMat = equipped ? insulatedGloveMaterial : bareHandMaterial;

        if(leftHandMesh) leftHandMesh.material = targetMat;
        if(rightHandMesh) rightHandMesh.material = targetMat;

        Debug.Log(equipped ? "<color=yellow>[Visual]</color> Àý¿¬ Àå°© Âø¿ë ¿Ï·á" : "<color=yellow>[Visual]</color> Àå°© ÇØÁ¦");
    }
}