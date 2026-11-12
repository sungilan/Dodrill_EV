using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  EquipmentSlot.cs
//
//  맵에 장식으로만 놓여 있던 모델 자리에 '쓸 수 있는 장비'를 띄우는 자리표.
//  · 모든 클라이언트: 장식 모델(decor)의 렌더러·콜라이더를 끈다
//  · 서버: 시작되면 이 자리(위치·회전)에 prefab 을 한 번 생성·Spawn → 모두에게 같은 장비가 보이고 잡을 수 있다
//  · 새 자리(장식 없음)도 빈 오브젝트에 붙여 쓰면 된다
// ============================================================
public class EquipmentSlot : MonoBehaviour
{
    public static readonly List<EquipmentSlot> All = new List<EquipmentSlot>();

    public NetworkObject prefab;
    [Tooltip("숨길 장식 모델(장비가 대신 놓인다). 비우면 숨기지 않는다")] public GameObject decor;

    [System.NonSerialized] public bool Spawned;

    private void Awake()
    {
        if (decor != null)
        {
            foreach (var r in decor.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in decor.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); EquipmentSpawner.Ensure(); }
    private void OnDisable() { All.Remove(this); }
}

/// <summary>서버에서 EquipmentSlot 자리에 장비를 한 번씩 Spawn 한다.</summary>
public class EquipmentSpawner : MonoBehaviour
{
    private static EquipmentSpawner s_inst;
    private float _next;

    public static void Ensure()
    {
        if (s_inst != null) return;
        var go = new GameObject("[EquipmentSpawner]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<EquipmentSpawner>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        var nm = InstanceFinder.NetworkManager;
        if (nm == null || !nm.IsServerStarted) return;

        for (int i = EquipmentSlot.All.Count - 1; i >= 0; i--)
        {
            var slot = EquipmentSlot.All[i];
            if (slot == null || slot.Spawned || slot.prefab == null) continue;
            slot.Spawned = true;
            var t = slot.transform;
            var nob = Instantiate(slot.prefab, t.position, t.rotation);
            if (slot.gameObject.scene.IsValid() && nob.gameObject.scene != slot.gameObject.scene)
                SceneManager.MoveGameObjectToScene(nob.gameObject, slot.gameObject.scene);
            nm.ServerManager.Spawn(nob);
            Debug.Log($"[Equipment] spawn {slot.prefab.name} at {slot.name}");
        }
    }
}
