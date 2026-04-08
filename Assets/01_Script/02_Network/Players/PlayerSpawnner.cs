using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
using XRAirpotrSecurity;
using AYellowpaper.SerializedCollections;

public class PlayerSpawnner : NetworkBehaviour
{
    [SerializeField]
    private SerializedDictionary<PlatformType, GameObject> playerObjects = new SerializedDictionary<PlatformType, GameObject>();
    [Header("Stable Prefab Mapping (권장)")]
    [SerializeField] private GameObject vrPlayerPrefab;
    [SerializeField] private GameObject pcPlayerPrefab;
    [SerializeField] private GameObject mobilePlayerPrefab;
    private static int playerCount = 0;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(IsOwner)
        {
            name = "Local Player";
            PlatformType playerType = Utils.GetPlatformType();
            // ✅ IsOwner인 경우만 SpawnPlayerReq 호출
            SpawnPlayerReq(NetworkManager.ClientManager.Connection, playerType);
        }
        else
        {
            name = $"Remote Player - {++playerCount}";
            // ❌ 다른 클라이언트의 SpawnPlayerReq는 호출하지 않음
        }
    }

    [ServerRpc]
    private void SpawnPlayerReq(NetworkConnection connection, PlatformType type)
    {
        GameObject prefab = ResolvePlayerPrefab(type);
        if(prefab == null)
        {
            Debug.LogError($"No player object for platform type: {type}");
            return;
        }

        GameObject playerInstance = Instantiate(prefab, transform.position, transform.rotation);

        if(type != PlatformType.VR)
        {
            playerInstance.transform.position += Vector3.up * 0.7f;
        }

        playerInstance.name = $"{type} Player - {connection.ClientId}";

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        NetworkObject parentNetObj = GetComponent<NetworkObject>();

        // ✅ Spawn 먼저, 그 다음 SetParent
        Spawn(playerInstance, connection);
        netObj.SetParent(parentNetObj); // Spawn 후에 SetParent
    }

    private GameObject ResolvePlayerPrefab(PlatformType type)
    {
        // Dictionary 인스펙터가 흔들릴 때를 대비해 고정 필드를 우선 사용.
        switch(type)
        {
            case PlatformType.VR:
                if(vrPlayerPrefab != null) return vrPlayerPrefab;
                break;
            case PlatformType.PC:
                if(pcPlayerPrefab != null) return pcPlayerPrefab;
                break;
            case PlatformType.Mobile:
                if(mobilePlayerPrefab != null) return mobilePlayerPrefab;
                break;
        }

        if(playerObjects != null && playerObjects.TryGetValue(type, out var mapped) && mapped != null)
            return mapped;

        return null;
    }

    private void OnValidate()
    {
        // 기존 Dictionary 값이 있으면 고정 필드로 한번 채워두고,
        // 이후에는 고정 필드가 있으면 Dictionary도 동기화해 혼선 방지.
        if(playerObjects != null)
        {
            if(vrPlayerPrefab == null && playerObjects.TryGetValue(PlatformType.VR, out var vr) && vr != null)
                vrPlayerPrefab = vr;
            if(pcPlayerPrefab == null && playerObjects.TryGetValue(PlatformType.PC, out var pc) && pc != null)
                pcPlayerPrefab = pc;
            if(mobilePlayerPrefab == null && playerObjects.TryGetValue(PlatformType.Mobile, out var mobile) && mobile != null)
                mobilePlayerPrefab = mobile;

            if(vrPlayerPrefab != null) playerObjects[PlatformType.VR] = vrPlayerPrefab;
            if(pcPlayerPrefab != null) playerObjects[PlatformType.PC] = pcPlayerPrefab;
            if(mobilePlayerPrefab != null) playerObjects[PlatformType.Mobile] = mobilePlayerPrefab;
        }
    }
}