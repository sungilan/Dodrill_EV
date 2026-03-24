using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace XumNet
{
    /// <summary>
    /// Singleton network manager used to spawn <see cref="NetworkObject"/> instances.
    /// </summary>
    public sealed partial class XumNetwork : NetworkBehaviour
    {
        /// <summary>
        /// Gets the active <see cref="XumNetwork"/> instance.
        /// </summary>
        public static XumNetwork Instance { get; private set; }
        private GameObject _gameObject;
        private NetworkManager _networkManager;

        /// <summary>
        /// Cleans up event subscriptions when the object is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (_networkManager != null)
                _networkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
        }

        /// <summary>
        /// Initializes the singleton instance and registers scene callbacks.
        /// </summary>
        private void Awake()
        {
            _networkManager = InstanceFinder.NetworkManager;
            if (_networkManager == null)
            {
                NetworkManagerExtensions.LogWarning($"PlayerSpawner on {gameObject.name} cannot work as NetworkManager wasn't found on this object or within parent objects.");
                return;
            }


            Debug.Log("you are at XumNetwork, awake start");
            if (Instance == null)
            {
                Instance = this;
                Debug.Log("you are at XumNetwork, inside Instance == null, now the instance is " + Instance.name);
            }
            else if (Instance != this)
                Destroy(Instance.gameObject);

            _networkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

        }

        /// <summary>
        /// Called on the server after a client finishes loading its start scenes.
        /// </summary>
        /// <param name="connection">Connection of the client.</param>
        /// <param name="asServer">True if invoked on the server.</param>
        private void SceneManager_OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
                return;
            Debug.Log("you are at XumNetwork, OnClientLoadedStartScene");

        }

        /// <summary>
        /// Spawns a <see cref="NetworkObject"/> at <see cref="Vector3.zero"/> without assigning ownership.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        public static void Instantiate(NetworkObject prefab)
        {
            //Instance._gameObject = prefab.gameObject;
            Instantiate(prefab, Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Spawns a <see cref="NetworkObject"/> at a position and rotation with default ownership.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="pos">Spawn position.</param>
        /// <param name="rot">Spawn rotation.</param>
        public static void Instantiate(NetworkObject prefab, Vector3 pos, Quaternion rot)
        {
            //Instance._gameObject = prefab.gameObject;
            Instantiate(prefab, pos, rot, InstanceFinder.ClientManager.Connection);
        }

        /// <summary>
        /// Spawns a <see cref="NetworkObject"/> with the specified owner connection.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="pos">Spawn position.</param>
        /// <param name="rot">Spawn rotation.</param>
        /// <param name="owner">Connection that will own the object.</param>
        public static void Instantiate(NetworkObject prefab, Vector3 pos, Quaternion rot, NetworkConnection owner)
        {
            //Instance._gameObject = prefab.gameObject;
            Instance.HandleInstantiation(prefab, pos, rot, owner);
        }

        /// <summary>
        /// Helper used by the public <c>Instantiate</c> methods to perform the actual spawn logic.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="pos">Spawn position.</param>
        /// <param name="rot">Spawn rotation.</param>
        /// <param name="owner">Connection that will own the object.</param>
        private void HandleInstantiation(NetworkObject prefab, Vector3 pos, Quaternion rot, NetworkConnection owner)
        {
            //Instance._gameObject = prefab.gameObject;
            LetsInitPlayer(prefab, pos, rot, owner);
        }

        /// <summary>
        /// Server-side spawn implementation.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="pos">Spawn position.</param>
        /// <param name="rot">Spawn rotation.</param>
        /// <param name="owner">Connection that will own the spawned object.</param>
        [ServerRpc(RequireOwnership = false)]
        private void LetsInitPlayer(NetworkObject prefab, Vector3 pos, Quaternion rot, NetworkConnection owner)
        {
            //GameObject _go = Instantiate(prefab, pos, rot);
            NetworkObject nob = _networkManager.GetPooledInstantiated(prefab, pos, rot, true);

            _networkManager.ServerManager.Spawn(nob, owner);
            _networkManager.SceneManager.AddOwnerToDefaultScene(nob);
        }
    }
}