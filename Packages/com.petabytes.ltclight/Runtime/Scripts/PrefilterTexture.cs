using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace petabytes.LTCLight
{
    public class PrefilterInfo
    {
        public Texture Tex;
        public RectInt Region;
        
        public bool IsRealTime;
        public bool needsPrefilter;
        public Int16 SlotIdx;
        public int RefCount;
    }
    public class PrefilterTexture
    {
        public RenderTexture PrefilteredTexture { get;  private set; }
        public Int16 SlotUsageBit
        {
            get => _slotUsageBit;
        }
        
        private Shader _prefilteredShader;
        private Material _prefilteredMaterial;

        private Dictionary<Texture, PrefilterInfo> prefilterInfos = new Dictionary<Texture, PrefilterInfo>();
        public const int kAtlasSize = 2048;
        public const int kSlotSize = 512;
        private Int16 _slotUsageBit;

        public void Init(Shader prefilteredShader)
        {
            PrefilteredTexture = new RenderTexture(kAtlasSize, kAtlasSize, 0, RenderTextureFormat.ARGB32, 10);
            PrefilteredTexture.useMipMap = true;
            PrefilteredTexture.autoGenerateMips = false;
            PrefilteredTexture.filterMode = FilterMode.Trilinear;
            PrefilteredTexture.wrapMode = TextureWrapMode.Clamp;
            _prefilteredShader = prefilteredShader;
            _prefilteredMaterial = new Material(_prefilteredShader);
        }

        public void Deinit()
        {
            PrefilteredTexture.Release();
            prefilterInfos.Clear();
            GameObject.DestroyImmediate(_prefilteredMaterial);
            _slotUsageBit = 0;
        }

        public bool GetPrefilterInfo(Texture tex, out PrefilterInfo info)
        {
            if (prefilterInfos.TryGetValue(tex, out info))
                return true;

            return false;
        }

        public bool AddTexture(Texture texture, bool isRealtime, out RectInt region)
        {
            if (texture == null)
            {
                region = default;
                return false;
            }
            
            region = new RectInt();
            if (_slotUsageBit == 0xFFFF)
            {
                Debug.LogError("Prefilter texture exceeds limits of 16");
                return false;
            }
            
            // Check existed records
            if (prefilterInfos.TryGetValue(texture, out var info))
            {
                region = info.Region;
                info.IsRealTime = isRealtime;
                info.RefCount++;
                return true;
            }
            
            // Bit scan for free slot
            int i = 0;
            Int16 bitmask = 0;
            for (; i < 16; ++i)
            {
                bitmask = (Int16)(1 << i);
                if ((_slotUsageBit & bitmask) == 0)
                    break;
            }

            _slotUsageBit |= bitmask;
            region = new RectInt(i%4*kSlotSize, i/4*kSlotSize, kSlotSize, kSlotSize);
            
            info = new PrefilterInfo()
            {
                Region = region,
                IsRealTime = isRealtime,
                needsPrefilter = true,
                SlotIdx = (Int16)i,
                RefCount = 1,
                Tex = texture
            };
            info.IsRealTime = isRealtime;
            info.Region = region;
            prefilterInfos.Add(texture, info);
            return true;
        }

        public void RemoveTexture(Texture texture)
        {
            if (!prefilterInfos.TryGetValue(texture, out var info))
                return;
            info.RefCount--;

            if (info.RefCount == 0)
            {
                prefilterInfos.Remove(texture);
                _slotUsageBit &= (Int16)~(0x1 << info.SlotIdx);
            }
        }

        public void PerformPrefilter()
        {
            _prefilteredMaterial.SetInt("_SampleCount", 13);
            var cmd = new CommandBuffer();
            cmd.name = "LTCPrefilter";

            foreach (var info in prefilterInfos)
            {
                if (!(info.Value.needsPrefilter || info.Value.IsRealTime))
                    continue;

                for (int i = 0; i < 10; ++i)
                {
                    int kTempRtId = Shader.PropertyToID("BlurTemp" + i);
                    cmd.GetTemporaryRT(kTempRtId, kSlotSize >> i, kSlotSize >> i, 0, FilterMode.Bilinear);
                    float size = (0.1f * i);
                    cmd.SetGlobalFloat("_FilterSize", size * size);
                    cmd.SetGlobalVector("_FilterDir", new Vector4(0, 1, 0, 1));
                    cmd.SetRenderTarget(kTempRtId);
                    cmd.SetGlobalTexture("_MainTex", info.Value.Tex);
                    cmd.DrawProcedural(Matrix4x4.identity, _prefilteredMaterial, 0, MeshTopology.Triangles, 3);

                    cmd.SetGlobalVector("_FilterDir", new Vector4(1, 0, 1, 0));
                    cmd.SetRenderTarget(new RenderTargetIdentifier(PrefilteredTexture, i));
                    Rect viewport = new Rect(info.Value.Region.position / (1 << i), info.Value.Region.size / (1 << i));
                    cmd.SetViewport(viewport);
                    cmd.SetGlobalTexture("_MainTex", kTempRtId);
                    cmd.DrawProcedural(Matrix4x4.identity, _prefilteredMaterial, 0, MeshTopology.Triangles, 3);
                    cmd.ReleaseTemporaryRT(kTempRtId);
                }

                if (!info.Value.IsRealTime)
                    info.Value.needsPrefilter = false;
            }
            
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }
    }
    
}
