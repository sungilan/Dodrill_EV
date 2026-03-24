using UnityEngine;

// ============================================================
//  SpawnPoint.cs
//  씬에 빈 GameObject로 배치해두는 스폰 위치 마커
//
//  씬 세팅 예시:
//    SpawnPoints (빈 GameObject)
//    ├── SpawnPoint_Shelf_A    ← SpawnPoint 컴포넌트, id: "SpawnPoint_Shelf_A"
//    ├── SpawnPoint_Shelf_B
//    └── SpawnPoint_Tray
// ============================================================

public class SpawnPoint : MonoBehaviour
{
    [Tooltip("JSON spawnPointId 와 매핑되는 고유 ID")]
    public string pointId;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.1f);
        Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.3f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.35f,
            string.IsNullOrEmpty(pointId) ? "(id 없음)" : pointId);
#endif
    }
}
