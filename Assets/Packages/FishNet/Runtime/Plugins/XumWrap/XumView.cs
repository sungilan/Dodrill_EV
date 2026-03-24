using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using XumNet;
using FishNet.Serializing;

#if UNITY_EDITOR
[CustomEditor(typeof(XumView))]
/// <summary>Custom inspector that exposes a button to scan RPC methods.</summary>
public class XumViewEditor : Editor
{
    /// <summary>
    /// Draws the inspector UI and exposes a button to rescan RPC methods.
    /// </summary>
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Scan XumView Methods"))
            ((XumView)target).ScanForXumRPC();
        DrawDefaultInspector();
    }
}
#endif

namespace XumNet
{
    /// <summary>
    /// Possible recipients for an RPC call.
    /// </summary>
    public enum RpcTarget
    {
        /// <summary>Send the RPC to the master client only.</summary>
        MasterClient,
        /// <summary>Send the RPC to all clients except the sender.</summary>
        Others,
        /// <summary>Send the RPC to all clients including the sender.</summary>
        All,
        /// <summary>Send the RPC to a specific player.</summary>
        SpecificPlayer
    }

    [DefaultExecutionOrder(-64)]
    [RequireComponent(typeof(NetworkObject))]
    /// <summary>
    /// Network behaviour providing RPC functionality and lifecycle callbacks.
    /// </summary>
    public class XumView : NetworkBehaviour
    {
        /// <summary>Invoked when this view is started on the client.</summary>
        public Action OnStart;
        /// <summary>Invoked when this view stops on the client.</summary>
        public Action OnStop;
        /// <summary>Invoked when this view is destroyed on the client.</summary>
        public Action OnDestroy;

        [SerializeField]
        /// <summary>Components scanned for <see cref="XumRPC"/> methods.</summary>
        private MonoBehaviour[] behavioursToBeScanned;
        /// <summary>True once RPC methods have been scanned.</summary>
        private bool methodsInitialized = false;

        private Dictionary<string, Method> xumMethods = new();
        private struct Method
        {
            public MonoBehaviour source;
            public MethodInfo methodInfo;
            public bool wantSenderInfo;
            public Method(MonoBehaviour source, MethodInfo methodInfo, bool wantSenderInfo)
            {
                this.source = source;
                this.methodInfo = methodInfo;
                this.wantSenderInfo = wantSenderInfo;
            }
        }

        /// <summary>Scans child components for methods decorated with <see cref="XumRPC"/>.</summary>
        public void ScanForXumRPC()
        {
            behavioursToBeScanned = GetComponentsInChildren<MonoBehaviour>(true)
                .Where(comp => comp.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => Attribute.IsDefined(method, typeof(XumRPC)))).ToArray();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Automatically scan for RPC methods whenever values change in the editor.
        /// </summary>
        private void OnValidate()
        {
            ScanForXumRPC();
        }
#endif

        /// <summary>Builds the internal dictionary of RPC methods.</summary>
        private void initXumMethods()
        {
            xumMethods = new();
            foreach (MonoBehaviour component in behavioursToBeScanned)
                foreach (var method in component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => Attribute.IsDefined(method, typeof(XumRPC)))
                .ToList())
                    xumMethods.Add(method.Name,
                        new Method(component
                        , method
                        , method.GetParameters().Length == 0 ? false : method.GetParameters().Last().ParameterType.Equals(typeof(NetworkConnection))));
        }

        /// <summary>Registers a method for RPC invocation.</summary>
        /// <param name="methodName">Name used to call the method.</param>
        /// <param name="source">Component that owns the method.</param>
        /// <param name="methodInfo">Reflection info for the method.</param>
        /// <param name="wantSenderInfo">Whether the RPC expects the sender connection as the last parameter.</param>
        public void RegisterXumMethod(string methodName, MonoBehaviour source, MethodInfo methodInfo, bool wantSenderInfo)
        {
            if (!xumMethods.ContainsKey(methodName))
            {
                xumMethods.Add(methodName, new Method(source, methodInfo, wantSenderInfo));
            }
        }


        /// <summary>Called when the object becomes active on the client.</summary>
        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!GetComponent<NetworkObject>().enabled)
                GetComponent<NetworkObject>().enabled = true;
            OnStart?.Invoke();
        }

        /// <summary>Invokes a registered RPC method on the specified target.</summary>
        /// <param name="methodName">The name of the method to invoke.</param>
        /// <param name="target">Destination for the RPC.</param>
        /// <param name="args">Arguments to pass to the remote call.</param>
        public void RPC(string methodName, RpcTarget target, /*NetworkConnection specificPlayer = null,*/ params object[] args)
        {
            if (xumMethods[methodName].wantSenderInfo)
            {
                args = args.Append(LocalConnection).ToArray();
            }

            switch (target)
            {
                case RpcTarget.MasterClient:
                    ServerRPC(methodName, args);
                    break;
                case RpcTarget.Others:
                    OthersRPC(methodName, args, LocalConnection);
                    break;
                case RpcTarget.All:
                    AllRPC(methodName, args);
                    break;
                    /*case RpcTarget.SpecificPlayer:
                        if (specificPlayer != null)
                        {
                            SpecificPlayerRPC(specificPlayer, methodName, args);
                        }
                    break;*/
            }
        }


        /// <summary>Initializes RPC method caching.</summary>
        private void Awake()
        {
            if (!methodsInitialized)
            {
                ScanForXumRPC();
                initXumMethods();
                methodsInitialized = true;
            }
        }


        #region rpc branches
        /// <summary>Executes a method on the server.</summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        [ServerRpc(RequireOwnership = false)]
        private void ServerRPC(string methodName, object[] args)
        {
            FinalInvoke(methodName, args);
        }
        /// <summary>Server RPC that forwards calls to other clients.</summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        /// <param name="sender">Connection originating the RPC.</param>
        [ServerRpc(RequireOwnership = false)]
        private void OthersRPC(string methodName, object[] args, NetworkConnection sender)
        {
            ClientOthersRPC(methodName, args, sender);
        }
        /// <summary>Server RPC that broadcasts to all clients.</summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        [ServerRpc(RequireOwnership = false)]
        private void AllRPC(string methodName, object[] args)
        {
            ClientAllRPC(methodName, args);
        }
        /// <summary>Receives a forwarded RPC from the server.</summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        /// <param name="sender">Original sender of the RPC.</param>
        [ObserversRpc]
        private void ClientOthersRPC(string methodName, object[] args, NetworkConnection sender)
        {
            if (LocalConnection.Equals(sender))
            {
                return;
            }
            FinalInvoke(methodName, args);
        }
        /// <summary>Receives a broadcast RPC from the server.</summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        [ObserversRpc]
        private void ClientAllRPC(string methodName, object[] args)
        {
            FinalInvoke(methodName, args);
        }
        /// <summary>Sends an RPC to a specific player.</summary>
        /// <param name="target">Connection to deliver the RPC to.</param>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="args">Serialized arguments for the call.</param>
        [TargetRpc]
        private void SpecificPlayerRPC(NetworkConnection target, string methodName, object[] args)
        {
            if (LocalConnection.Equals(target))
            {
                FinalInvoke(methodName, args);
            }
        }

        /// <summary>Invokes the cached method using reflection.</summary>
        /// <param name="methodName">Name of the method to execute.</param>
        /// <param name="args">Arguments to pass to the method.</param>
        private void FinalInvoke(string methodName, object[] args)
        {
            if (!xumMethods.ContainsKey(methodName))
            {
                Debug.LogError($"Method {methodName} not found in xumMethods.");
                return;
            }

            try
            {
                xumMethods[methodName].methodInfo.Invoke(xumMethods[methodName].source, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error invoking method {methodName}: {ex}");
            }
        }
        #endregion

    }

    #region attribute declaration
    /// <summary>Attribute used to mark methods as callable via Xum RPC.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class XumRPC : Attribute
    { }
    #endregion attribute declaration

    #region global Serialization for XumView
    /// <summary>
    /// Utility for serializing common objects for RPC transport. Supports
    /// <c>int</c>, <c>string</c>, <c>bool</c>, <c>NetworkConnection</c>,
    /// <c>Vector3</c>, <c>GameObject</c>, <c>Color</c>, <c>Quaternion</c>, 
    /// <c>Transform</c>, <c>float</c> and <c>float[]</c> values
    /// </summary>
    public static class ObjectSerializer
    {
        /// <summary>
        /// Serializes a supported object type using a <see cref="Writer"/>.
        /// </summary>
        /// <param name="writer">Writer used to serialize the value.</param>
        /// <param name="value">The object to serialize.</param>
        public static void WriteObject(this Writer writer, object value)
        {
            switch (value)
            {
                case int:
                    writer.WriteInt8Unpacked(0);
                    writer.WriteInt32((int)value);
                    break;
                case string:
                    writer.WriteInt8Unpacked(1);
                    writer.WriteString((string)value);
                    break;
                case bool:
                    writer.WriteInt8Unpacked(2);
                    writer.WriteBoolean((bool)value);
                    break;
                case NetworkConnection:
                    writer.WriteInt8Unpacked(3);
                    writer.WriteNetworkConnection((NetworkConnection)value);
                    break;
                case Vector3:
                    writer.WriteInt8Unpacked(4);
                    writer.WriteVector3((Vector3)value);
                    break;
                case GameObject:
                    writer.WriteInt8Unpacked(5);
                    writer.WriteGameObject((GameObject)value);
                    break;
                case Color:
                    writer.WriteInt8Unpacked(6);
                    writer.WriteColor((Color)value);
                    break;
                case Quaternion:
                    writer.WriteInt8Unpacked(7);
                    writer.WriteQuaternionUnpacked((Quaternion)value);
                    break;
                case Transform:
                    writer.WriteInt8Unpacked(8);
                    writer.WriteTransform((Transform)value);
                    break;
                case float:
                    writer.WriteInt8Unpacked(9);
                    writer.WriteSingle((float)value);
                    break;
                case float[] fa:
                    writer.WriteInt8Unpacked(10);
                    writer.WriteArray(fa);
                    break;
                case double d:
                    writer.WriteInt8Unpacked(11);
                    writer.WriteDouble(d);
                    break;
                case double[] da:
                    writer.WriteInt8Unpacked(12);
                    writer.WriteArray(da);
                    break;
                case Vector2 v2:
                    writer.WriteInt8Unpacked(13);
                    writer.WriteVector2(v2);
                    break;
                case List<int> li:
                    writer.WriteInt8Unpacked(14);
                    writer.WriteList(li);
                    break;
                case List<float> lf:
                    writer.WriteInt8Unpacked(15);
                    writer.WriteList(lf);
                    break;
                case List<string> ls:
                    writer.WriteInt8Unpacked(16);
                    writer.WriteList(ls);
                    break;
                default:
                    throw new ArgumentException($"Unsupported type: {value.GetType().Name}");
            }
        }

        /// <summary>
        /// Deserializes an object previously written with <see cref="WriteObject"/>.
        /// </summary>
        /// <param name="reader">Reader containing the serialized value.</param>
        /// <returns>The deserialized object.</returns>
        public static object ReadObject(this Reader reader)
        {
            byte dataType = reader.ReadUInt8Unpacked();
            switch (dataType)
            {
                case 0: return reader.ReadInt32();
                case 1: return reader.ReadString();
                case 2: return reader.ReadBoolean();
                case 3: return reader.ReadNetworkConnection();
                case 4: return reader.ReadVector3();
                case 5: return reader.ReadGameObject();
                case 6: return reader.ReadColor();
                case 7: return reader.ReadQuaternionUnpacked();
                case 8: return reader.ReadTransform();
                case 9: return reader.ReadSingle();
                case 10: return reader.ReadArrayAllocated<float>();
                case 11: return reader.ReadDouble();
                case 12: return reader.ReadArrayAllocated<double>();
                case 13: return reader.ReadVector2();
                case 14: return reader.ReadListAllocated<int>();
                case 15: return reader.ReadListAllocated<float>();
                case 16: return reader.ReadListAllocated<string>();
                default: throw new ArgumentException($"Unsupported type: {dataType}");
                    #endregion attribute declaration  

                    #region global Serialization for XumView  
                    #endregion global Serialization for XumView
            }
        }
    }
}