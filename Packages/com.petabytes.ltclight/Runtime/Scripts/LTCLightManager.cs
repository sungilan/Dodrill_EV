using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace petabytes.LTCLight
{
    public enum LTCLightType
    {
        Polygon,
        Disk,
        Linear,
        Textured
        // TODO: Occluder
    }

    public enum LTCFlag
    {
        IsEnabled = 1,
        IsDoubleSided = 2,
        IsDiffuse = 4,
        IsSpecular = 8,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LTCLightProp
    {
        public int VertCount => vertEnd - vertStart;
        
        public Color color;
        public Vector2 intensity;
        
        // World space vertex index 
        public int vertStart;
        public int vertEnd;
        // Lower 16 bits: LTCLightType
        // Higher 16 bits: LTCFlag
        public int flags;    
        // XYZ pos
        public Vector3 position;
        public float radius;
        public float range;
        public Vector2 startUV;
    }

    public class LTCLightInfo
    {
        public LTCLightProp properties;
        // Properties on CPU side
        public Transform lightTransform;
        public bool useWorldPosition;
        public bool isRealTime;
    }
    
    [ExecuteAlways]
    public partial class LTCLightManager : MonoBehaviour
    {
        public static LTCLightManager Instance { get => instance_; }
        public static bool IsInitialized { get; private set; }
        public Shader prefilteredShader;
        private static LTCLightManager instance_ = null;
        
        private LinkedList<LTCLightInfo> lightInfos_ = new LinkedList<LTCLightInfo>();
        private GraphicsBuffer[] lightInfoGpuBuffers_;
        private GCContainer<Vector4> lightVertices_ = new GCContainer<Vector4>(kInitVertexCount, OnContainerGC);
        private GraphicsBuffer[] lightVerticsGpuBuffers_;
        private int currentIndex_ = 0;
        private bool hasVerticesCompacted_ = false;
        private PrefilterTexture prefilteredTexture_ = new PrefilterTexture();
        
        private const int kInitVertexCount = 1024;
        private const int kInitLightCount = 32;
        private const int kMaxVertexNumPerLight = 32;

        private static readonly int kLightInfoBufferName = Shader.PropertyToID("LTCLightInfosBuffer");
        private static readonly int kLightVerticesBufferName = Shader.PropertyToID("LTCLightVerticesBuffer");
        private static readonly int kLightCountName = Shader.PropertyToID("LTCLightNum");
        private static readonly int kLightTexture = Shader.PropertyToID("LTCTexture");

        // Start is called before the first frame update
        void OnEnable()
        {
            Init();
        }

        void Init()
        {
            if (instance_ != null)
            {
                Debug.LogError("More than one LTCLightManager instances in the scene!");
                return;
            }

            instance_ = this;

            lightInfoGpuBuffers_ = new GraphicsBuffer[2];
            lightVerticsGpuBuffers_ = new GraphicsBuffer[2];

            // TODO: Use double buffer to avoid errors
            // But if we do incremental update, data sync is needed between double buffer
            for (int i = 0; i < 2; ++i)
            {
                lightInfoGpuBuffers_[i] = new GraphicsBuffer(GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.LockBufferForWrite, kInitLightCount, Marshal.SizeOf(typeof(LTCLightProp)));
                lightVerticsGpuBuffers_[i] = new GraphicsBuffer(GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.LockBufferForWrite, kInitVertexCount, Marshal.SizeOf(typeof(Vector4)));
            }


            Shader.SetGlobalTexture("ltc_1", LutData.ltc_1);
            Shader.SetGlobalTexture("ltc_2", LutData.ltc_2);

            prefilteredTexture_.Init(prefilteredShader);
            Shader.SetGlobalTexture(kLightTexture, prefilteredTexture_.PrefilteredTexture);
            IsInitialized = true;
            hasVerticesCompacted_ = true;
            // Debug.Log("LTCLightManager has been initialized!");
        }

        void Deinit()
        {
            IsInitialized = false;

            for (var l = lightInfos_.First; l != null; l = l.Next)
            {
                if (l.Value.lightTransform == null)
                    continue;
                var ltc = l.Value.lightTransform.GetComponent<LTCLight>();
                ltc.OnManagerDisabled();
            }

            for (int i = 0; i < 2; i++)
            {
                lightInfoGpuBuffers_[i]?.Release();
                lightVerticsGpuBuffers_[i]?.Release();
            }

            LutData.Destroy();
            instance_ = null;
            prefilteredTexture_.Deinit();
            lightInfos_.Clear();
            lightVertices_.Clear();
            // Debug.Log("LTCLightManager has been destroyed!");
        }

        void OnDisable()
        {
            Deinit();
        }

        private int NextVertexIndex()
        {
            return lightVertices_.Count;
        }

        private static void OnContainerGC(Dictionary<int, int> idxRemap)
        {
            if (Instance == null)
            {
                return;
            }

            foreach (var item in LTCLightManager.Instance.lightInfos_)
            {
                item.properties.vertStart = idxRemap[item.properties.vertStart];
                item.properties.vertEnd = idxRemap[item.properties.vertEnd];
            }

            Instance.hasVerticesCompacted_ = true;
        }

        public void UnregisterLight(LinkedListNode<LTCLightInfo> node)
        {
            // Debug.Log($"UnregisterLight {node.Value.lightTransform.gameObject}");
            lightVertices_.MarkRangeUnused(node.Value.properties.vertStart, node.Value.properties.vertEnd);
            lightInfos_.Remove(node);
        }

        public void UpdateLightTexture(LinkedListNode<LTCLightInfo> node, Texture texture, Texture oldTex)
        {
            // Add before remove, if texture and oldTex are the same, this can prevent unnecessary prefilter
            if (!texture)
                texture = Texture2D.whiteTexture;
            {
                if (prefilteredTexture_.AddTexture(texture, node.Value.isRealTime, out RectInt region))
                {
                    node.Value.properties.startUV = (Vector2)(region.position) / PrefilterTexture.kAtlasSize;
                }
                else
                {
                    Debug.LogError("Textured light update failed");
                }
            }

            
            if (oldTex)
            {
                prefilteredTexture_.RemoveTexture(oldTex);
            }
        }

        public void UpdateLightVertices(LinkedListNode<LTCLightInfo> node, List<Vector3> vertices)
        {
            List<Vector4> newVertices = new List<Vector4>(vertices.Count);
            for (int i = 0; i < vertices.Count; ++i)
            {
                newVertices.Add(vertices[i]);
            }
            UpdateLightVertices(node, newVertices);
        }
        
        public void UpdateLightVertices(LinkedListNode<LTCLightInfo> node, List<Vector4> vertices)
        {
            // Force update
            node.Value.lightTransform.hasChanged = true;
            if (vertices.Count <= node.Value.properties.VertCount)
            {
                int i = node.Value.properties.vertStart;
                int copyEnd = i + vertices.Count;
                for (; i < copyEnd; ++i)
                {
                    lightVertices_[i] = vertices[i - node.Value.properties.vertStart];
                }
                node.Value.properties.vertEnd = copyEnd;
                lightVertices_.MarkRangeUnused(copyEnd, node.Value.properties.vertEnd);
            }
            else
            {
                lightVertices_.MarkRangeUnused(node.Value.properties.vertStart, node.Value.properties.vertEnd);
                lightVertices_.AddList(vertices, out var newRange);
                node.Value.properties.vertStart = newRange.start;
                node.Value.properties.vertEnd = newRange.end;
            }
        }

        public LinkedListNode<LTCLightInfo> RegisterLight(LTCLight light)
        {
            // Debug.Log($"RegisterLight {light.gameObject}");
            // Extract LTCLightInfo
            LTCLightInfo info = new LTCLightInfo()
            {
                properties = new LTCLightProp()
                {
                    color = light.Color,
                    intensity = light.Intensity,
                    vertStart = NextVertexIndex(),
                    flags = (int)light.Type,
                    radius = light.Radius,
                    range = light.Range,
                },
                useWorldPosition = light.UseWorldPosition,
                lightTransform = light.transform,
            };
            info.properties.position = info.lightTransform.position;
            
            if (light.DoubleSided)
                info.properties.flags |= (int)LTCFlag.IsDoubleSided << 16;
            if (light.isActiveAndEnabled)
                info.properties.flags |= (int)LTCFlag.IsEnabled << 16;
            switch (light.LightMode)
            {
                case LTCLightMode.DiffuseOnly:
                    LTCLight.SetFlagBoolBit(true, (int)LTCFlag.IsDiffuse, ref info.properties.flags);
                    LTCLight.SetFlagBoolBit(false,  (int)LTCFlag.IsSpecular, ref info.properties.flags);
                    break;
                case LTCLightMode.SpecularOnly:
                    LTCLight.SetFlagBoolBit(false, (int)LTCFlag.IsDiffuse, ref info.properties.flags);
                    LTCLight.SetFlagBoolBit(true,  (int)LTCFlag.IsSpecular, ref info.properties.flags);
                    break;
                case LTCLightMode.DiffuseAndSpecular:
                    LTCLight.SetFlagBoolBit(true, (int)LTCFlag.IsDiffuse, ref info.properties.flags);
                    LTCLight.SetFlagBoolBit(true,  (int)LTCFlag.IsSpecular, ref info.properties.flags);
                    break;
            }
            
            switch (light.Type)
            {
                case LTCLightType.Polygon:
                    {
                        // What if polygonPoints less than 3?
                        Debug.Assert(light is LTCPolygonLight);
                        LTCPolygonLight pl = (LTCPolygonLight)light;
                        var vec4s = pl.PolygonPoints.Select(v3 => (Vector4)v3).ToList();
                        lightVertices_.AddList(vec4s, out info.properties.vertStart, out info.properties.vertEnd);
                    }
                    break;
                case LTCLightType.Linear:
                    {
                        Debug.Assert(light is LTCLinearLight);
                        LTCLinearLight ll = (LTCLinearLight)light;
                        lightVertices_.AddList(new List<Vector4>() { ll.StartPoint, ll.EndPoint }, out info.properties.vertStart, out info.properties.vertEnd);
                    }
                    break;
                case LTCLightType.Textured:
                    {
                        Debug.Assert(light is LTCTexturedLight);
                        LTCTexturedLight ll = (LTCTexturedLight)light;
                        lightVertices_.AddList(ll.GeomData, out info.properties.vertStart, out info.properties.vertEnd);
                        info.isRealTime = ll.IsRealTime;
                        if (prefilteredTexture_.AddTexture(ll.Texture?ll.Texture:Texture2D.whiteTexture, ll.IsRealTime, out RectInt region))
                        {
                            info.properties.startUV = (Vector2)(region.position) / PrefilterTexture.kAtlasSize;
                        }
                        else
                        {
                            Debug.LogError("Textured light register failed");
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
            
            return lightInfos_.AddLast(info);;
        }

        // Update is called once per frame
        void LateUpdate()
        {
            prefilteredTexture_.PerformPrefilter();
            var currLightInfoBuffer = lightInfoGpuBuffers_[currentIndex_];
            var currLightVertexBuffer = lightVerticsGpuBuffers_[currentIndex_];
            
            if (currLightInfoBuffer.count < lightInfos_.Count)
            {
                // We are currently using constant buffer, size can't change dynamically
                Debug.LogError("LTC light number exceed max");
                return;
                // var old = _lightInfoGpuBuffer;
                // _lightInfoGpuBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, old.count * 2, Marshal.SizeOf(typeof(LTCLightProp)));
                // old.Release();
            }

            if (currLightVertexBuffer.count < lightVertices_.Count)
            {
                // We are currently using constant buffer, size can't change dynamically
                Debug.LogError("LTC light vertices number exceed max");
                return;
                // var old = _lightVerticsGpuBuffer;
                // _lightVerticsGpuBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, old.count * 2, Marshal.SizeOf(typeof(Vector4)));
                // old.Release();
            }
            
            Shader.SetGlobalInt(kLightCountName, lightInfos_.Count);
            var bufferLightInfo = currLightInfoBuffer.LockBufferForWrite<LTCLightProp>(0, lightInfos_.Count);
            int idx = 0;
            foreach (var info in lightInfos_)
            {
                if (info.useWorldPosition)
                    info.properties.position = Vector3.zero;
                else
                    info.properties.position= info.lightTransform.position;
                bufferLightInfo[idx++] = info.properties;
                // If container compact happens before, we must copy all vertices,
                // otherwise, we only to copy those changed transforms
                if (hasVerticesCompacted_ || info.lightTransform.hasChanged)
                {
                    if (info.lightTransform.hasChanged)
                        info.lightTransform.hasChanged= false;
                    
                    int count = info.properties.vertEnd - info.properties.vertStart;
                    var bufferLightVertex = currLightVertexBuffer.LockBufferForWrite<Vector4>(info.properties.vertStart, count);
                    if (info.useWorldPosition || LTCLight.GetLightTypeFromFlag(info.properties.flags) == LTCLightType.Textured)
                    {
                        for (int i = info.properties.vertStart; i < info.properties.vertEnd; ++i)
                        {
                            bufferLightVertex[i - info.properties.vertStart] = lightVertices_[i];
                        }
                    }
                    else
                    {
                        for (int i = info.properties.vertStart; i < info.properties.vertEnd; ++i)
                        {
                            bufferLightVertex[i - info.properties.vertStart] = info.lightTransform.TransformPoint(lightVertices_[i]);
                        }
                    }
                    
                    currLightVertexBuffer.UnlockBufferAfterWrite<Vector4>(count);
                    
                    // Debug.Log($"Update points of light {info.lightTransform}");
                }
            }
            currLightInfoBuffer.UnlockBufferAfterWrite<LTCLightProp>(lightInfos_.Count);
            
            
            Shader.SetGlobalConstantBuffer(kLightInfoBufferName, currLightInfoBuffer, 0, currLightInfoBuffer.count*currLightInfoBuffer.stride);
            Shader.SetGlobalConstantBuffer(kLightVerticesBufferName, currLightVertexBuffer, 0, currLightVertexBuffer.count*currLightVertexBuffer.stride);

            // currentIndex_ = (currentIndex_ & 0x1)^0x1;
            hasVerticesCompacted_ = false;
        }
    }
}