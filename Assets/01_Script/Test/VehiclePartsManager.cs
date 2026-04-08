using UnityEngine;
using System.Collections.Generic;

public class VehiclePartsManager : MonoBehaviour
{
    [System.Serializable]
    public class PartData
    {
        public string partName;              // "Engine", "Battery" 등
        public GameObject partObject;        // 실제 부품 GameObject
        public BoltGroupCounter boltGroup;   // 해당 볼트 그룹
    }

    [SerializeField] private List<PartData> vehicleParts = new();

    private void Start()
    {
        // 모든 부품을 BoltGroupCounter와 연결
        foreach(var part in vehicleParts)
        {
            if(part.boltGroup != null && part.partObject != null)
            {
                part.boltGroup.targetDespawnObject = part.partObject;
                Debug.Log($"[Vehicle] ✓ {part.partName} 부품 등록: {part.partObject.name}");
            }
        }
    }

    // 특정 부품 찾기
    public GameObject GetPartByName(string partName)
    {
        return vehicleParts.Find(p => p.partName == partName)?.partObject;
    }

    // 특정 BoltGroupCounter의 부품 찾기
    public GameObject GetPartByBoltGroup(BoltGroupCounter boltGroup)
    {
        return vehicleParts.Find(p => p.boltGroup == boltGroup)?.partObject;
    }
}
