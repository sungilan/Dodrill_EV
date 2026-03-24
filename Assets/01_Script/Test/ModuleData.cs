// ModuleData.cs (각 모듈 오브젝트에 부착)
using UnityEngine;

public class ModuleData : MonoBehaviour
{
    public float voltage = 3.8f; // 정상 전압
    public bool isFaulty = false; // 불량 여부

    public void SetAsFaulty()
    {
        isFaulty = true;
        voltage = 1.2f; // 불량 시 전압 급락
    }
}