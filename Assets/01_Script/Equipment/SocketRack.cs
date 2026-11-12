using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  SocketRack.cs
//
//  임팩 소켓 거치대. 자식 슬롯마다 소켓 하나(10mm·17mm·27mm).
//  에어 임팩렌치의 앤빌(소켓 꽂는 사각 축)을 슬롯 소켓에 갖다 대면 그 소켓이 끼워진다(이미 끼운 게 있으면 바꿔 끼움).
//  어떤 렌치가 이미 낀 크기의 소켓은 거치대에서 안 보인다(각 화면에서 계산).
// ============================================================
public class SocketRack : MonoBehaviour
{
    public static readonly List<SocketRack> All = new List<SocketRack>();

    [System.Serializable]
    public class Slot
    {
        [Tooltip("1 = 10mm, 2 = 17mm, 3 = 27mm")] public int size;
        public Transform point;
        public GameObject visual;
    }

    public Slot[] slots;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    /// <summary>점 p 근처(radius) 슬롯의 소켓 크기, 없으면 0.</summary>
    public int SizeNear(Vector3 p, float radius)
    {
        if (slots == null) return 0;
        foreach (var s in slots)
            if (s != null && s.point != null && (s.point.position - p).sqrMagnitude < radius * radius) return s.size;
        return 0;
    }

    private void Update()
    {
        if (slots == null) return;
        foreach (var s in slots)
        {
            if (s == null || s.visual == null) continue;
            bool inUse = AirImpactWrench.AnyHasSocket(s.size);
            if (s.visual.activeSelf == inUse) s.visual.SetActive(!inUse);
        }
    }

    public static string SizeName(int size) => size == 1 ? "10mm" : size == 2 ? "17mm" : size == 3 ? "27mm" : "없음";
}
