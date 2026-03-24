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
    private static int playerCount = 0;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
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
        if (!playerObjects.ContainsKey(type))
        {
            Debug.LogError($"No player object for platform type: {type}");
            return;
        }

        GameObject playerInstance = Instantiate(playerObjects[type], transform.position, transform.rotation);

        if (type != PlatformType.VR)
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
}