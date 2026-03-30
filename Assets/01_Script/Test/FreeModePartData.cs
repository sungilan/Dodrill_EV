using UnityEngine;

// ============================================================
//  FreeModePartData.cs
//  자유탈거 모드에서 InteractablePart에 추가되는 부품 메타데이터
//  인스펙터에서 각 부품 프리팹에 부착
// ============================================================
[CreateAssetMenu(fileName = "PartData_", menuName = "EV Training/Part Data")]
public class PartDataSO : ScriptableObject
{
    [Header("식별")]
    public string partId;           // 고유 ID (예: "BatteryPack", "MSD_Lever")
    public string displayName;      // 인벤토리 표시 이름
    public string prefabId;         // 스폰용 프리팹 ID

    [Header("설명")]
    [TextArea(3, 6)]
    public string description;      // 인벤토리 호버 시 표시

    [Header("인벤토리 아이콘")]
    public Sprite icon;             // null이면 기본 아이콘 사용
}
