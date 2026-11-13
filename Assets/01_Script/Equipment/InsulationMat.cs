using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 절연 매트. 탈거한 고전압 부품을 이 위에 두면 보관 확인, 다른 곳에 두면 ManualTips 가 경고한다.
/// 판정 범위는 로컬 XZ 로 size, 위로 height 만큼.
/// </summary>
public class InsulationMat : MonoBehaviour
{
    public static readonly List<InsulationMat> All = new List<InsulationMat>();
    public Vector2 size = new Vector2(1.2f, 0.9f);
    public float height = 0.6f;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public static bool Contains(Vector3 world)
    {
        foreach (var m in All)
        {
            if (m == null) continue;
            Vector3 p = m.transform.InverseTransformPoint(world);
            Vector3 s = m.transform.lossyScale;
            p = Vector3.Scale(p, s);   // 로컬 단위를 m 로
            if (Mathf.Abs(p.x) <= m.size.x * 0.5f + 0.05f && Mathf.Abs(p.z) <= m.size.y * 0.5f + 0.05f && p.y > -0.1f && p.y < m.height) return true;
        }
        return false;
    }
}
