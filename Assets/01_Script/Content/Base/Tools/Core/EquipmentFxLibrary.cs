using UnityEngine;

// ============================================================
//  EquipmentFxLibrary.cs
//
//  정비 장비(스프레이·오일통·작업등·에어 임팩렌치 등)가 쓰는 이펙트 재질 묶음.
//  Resources/Equipment/EquipmentFxLibrary.asset 하나만 두고 EquipFx 가 불러 쓴다
//  (Resources 에 두어야 런타임에 만드는 파티클의 재질·셰이더가 빌드에 빠지지 않는다).
// ============================================================
[CreateAssetMenu(menuName = "DoDrill/Equipment Fx Library")]
public class EquipmentFxLibrary : ScriptableObject
{
    [Tooltip("스프레이 안개·소화기 분말 (부드러운 연기 입자)")] public Material mist;
    [Tooltip("액체 방울 (오일·냉각수 줄기)")] public Material drop;
    [Tooltip("불꽃 (부품 파손)")] public Material spark;
    [Tooltip("먼지·공기 분출")] public Material dust;
    [Tooltip("바닥에 고인 액체 (불투명 Lit, 매끈)")] public Material puddle;
    [Tooltip("작업등 렌즈 (Emission 켜진 Lit)")] public Material lens;
    [Tooltip("에어 호스 (LineRenderer)")] public Material hose;
}
