using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveGI.Core;
using AdaptiveGI.Native;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveGI.GI
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public class GIGrid
    {
        private static Texture3D emptyTexture;

        public Vector3Int gridSize;
        public float gridResolution;
        public float inverseGridResolution;
        public int maxLights;
        public bool enableComputeShaderSupport;
        public bool enableRenderBufferOptimizations;
        public int shadowChunkSize;
        public int shadowChunkUpdateRadius;

        public AdaptiveGI adaptiveGI;
        public bool usingLinearColorSpace;

        // Textures
        private RenderTexture shRPrimary;
        private RenderTexture shGPrimary;
        private RenderTexture shBPrimary;
        private RenderBuffer[] primaryBuffers;
        private RenderTexture shRSecondary;
        private RenderTexture shGSecondary;
        private RenderTexture shBSecondary;
        private RenderBuffer[] secondaryBuffers;
        private bool isUsingPrimary;

        // Progress tracking
        private int slicesPerFrame;
        private int currentSlice;
        private int lightCount;
        private int lightStartIndex;
        private int lightEndIndex;
        // Shadow chunk progress tracking
        private int currentCasterIndex;
        private int currentUpdateX;
        private int currentUpdateY;
        private int currentUpdateZ;

        // Lights
        private Texture2D lightTexture;
        private Texture2D indicesTexture;
        private NativeArray<float4> lightTextureData;
        private NativeArray<float2> indicesTextureData;
        private NativeArray<LightToCalculate> lightsToCalculate;

        // Shadows
        private Texture3D shadowTexture;
        private NativeArray<half> shadowTextureData;
        private Dictionary<int3, ShadowChunk> shadowChunks;
        private ShadowChunk[] chunksForThisBatch;

        // Grid dimensions
        private int gridWidth;
        private int gridHeight;
        private int gridDepth;

        // Transformations
        private float3 lightGridPosition;
        private float3 lightGridSize;
        private float3 worldToLocalOffset;
        private float3 gridToWorldOffset;
        private SoftBounds gridBounds;

        // Fragment shader
        [SerializeField, HideInInspector] private Shader calculateAdaptiveGIFragment;
        private Material calculateAdaptiveGIMaterial;

        // Compute shader
        [SerializeField, HideInInspector] private ComputeShader calculateAdaptiveGICompute;
        private int kernel;
        private int threadGroupSizeX;
        private int threadGroupSizeY;
        private bool useComputeShader;

        // Render buffer
        private RenderTargetSetup renderTargetSetup;
        private bool useOptimizedLoadActions;
        private static readonly RenderBufferLoadAction[] LOAD_ACTIONS_OPTIMIZED = new RenderBufferLoadAction[]
        {
            RenderBufferLoadAction.DontCare,
            RenderBufferLoadAction.DontCare,
            RenderBufferLoadAction.DontCare
        };
        private static readonly RenderBufferLoadAction[] LOAD_ACTIONS_COMPATIBILITY = new RenderBufferLoadAction[]
        {
            RenderBufferLoadAction.Load,
            RenderBufferLoadAction.Load,
            RenderBufferLoadAction.Load
        };
        private static readonly RenderBufferStoreAction[] STORE_ACTIONS = new RenderBufferStoreAction[]
        {
            RenderBufferStoreAction.Store,
            RenderBufferStoreAction.Store,
            RenderBufferStoreAction.Store
        };

        // Shader property ids
        private static readonly int PROPERTY_GRIDSHR = Shader.PropertyToID("_GridSHR");
        private static readonly int PROPERTY_GRIDSHG = Shader.PropertyToID("_GridSHG");
        private static readonly int PROPERTY_GRIDSHB = Shader.PropertyToID("_GridSHB");
        private static readonly int PROPERTY_FALLBACKSHR = Shader.PropertyToID("_FallbackSHR");
        private static readonly int PROPERTY_FALLBACKSHG = Shader.PropertyToID("_FallbackSHG");
        private static readonly int PROPERTY_FALLBACKSHB = Shader.PropertyToID("_FallbackSHB");
        private static readonly int PROPERTY_GRIDPOSITION = Shader.PropertyToID("_GridPosition");
        private static readonly int PROPERTY_GRIDSIZE = Shader.PropertyToID("_GridSize");
        private static readonly int PROPERTY_GRIDDIMENSION = Shader.PropertyToID("_GridDimension");
        private static readonly int PROPERTY_GRIDINVDIMENSION = Shader.PropertyToID("_GridInvDimension");
        private static readonly int PROPERTY_GRIDRESOLUTION = Shader.PropertyToID("_GridResolution");
        private static readonly int PROPERTY_LIGHTTEXTURE = Shader.PropertyToID("_LightTexture");
        private static readonly int PROPERTY_INDICESTEXTURE = Shader.PropertyToID("_IndicesTexture");
        private static readonly int PROPERTY_CURRENTSLICE = Shader.PropertyToID("_CurrentSlice");
        private static readonly int PROPERTY_LIGHTSTARTINDEX = Shader.PropertyToID("_LightStartIndex");
        private static readonly int PROPERTY_LIGHTENDINDEX = Shader.PropertyToID("_LightEndIndex");
        private static readonly int PROPERTY_FADESTARTDISTANCE = Shader.PropertyToID("_FadeStartDistance");
        private static readonly int PROPERTY_SHADOWGRID = Shader.PropertyToID("_ShadowGrid");
        private static readonly string KEYWORD_SHADOWS_ENABLED = "ENABLE_SHADOWS";
        private static readonly string KEYWORD_TRICUBIC_LIGHTING = "ADAPTIVEGI_TRICUBIC_LIGHTING";
        private const int LIGHT_TEXTURE_STRIDE = 5;
        private const int NUM_COMPUTE_THREADS = 8;

        private const int MAX_GRID_POINTS_CALCULATED_PER_FRAME = 1000000;
        private const long MAX_GRID_POINTS = 8000000;

        private const float GAMMA = 2.2f;

        public void DrawShadowChunksGizmo()
        {
            if (shadowChunks != null)
            {
                float chunkWorldSize = shadowChunkSize * inverseGridResolution;
                Gizmos.color = Color.green;

                foreach (int3 chunkPos in shadowChunks.Keys)
                {
                    Gizmos.DrawWireCube(ChunkToWorld(chunkPos), Vector3.one * chunkWorldSize);
                }
            }
        }

        public async void SampleGI(Vector3 position, System.Action<GIData> callback)
        {
            float3 worldPos = position;
            GridUtils.WorldToLocal(ref worldPos, ref lightGridPosition, ref worldToLocalOffset, gridResolution, out float3 localPos);
            int3 gridPos = (int3)math.floor(localPos);
            if (GridUtils.GridContainsPoint(ref gridPos, gridWidth, gridHeight, gridDepth))
            {
                RenderTexture shRTex;
                RenderTexture shGTex;
                RenderTexture shBTex;

                if (!isUsingPrimary)
                {
                    shRTex = shRPrimary;
                    shGTex = shGPrimary;
                    shBTex = shBPrimary;
                }
                else
                {
                    shRTex = shRSecondary;
                    shGTex = shGSecondary;
                    shBTex = shBSecondary;
                }

                if (shRTex != null && shRTex.IsCreated() && shGTex != null && shGTex.IsCreated() && shBTex != null && shBTex.IsCreated())
                {
                    GIData data = new GIData();
                    bool shRDone = false;
                    bool shGDone = false;
                    bool shBDone = false;

                    AsyncGPUReadbackRequest shRRequest = AsyncGPUReadback.Request(shRTex, 0, gridPos.x, 1, gridPos.y, 1, gridPos.z, 1, request =>
                    {
                        NativeArray<half4> shRData = request.GetData<half4>();
                        half4 shR = shRData[0];
                        data.shR = new Vector4(shR.x, shR.y, shR.z, shR.w);
                        shRDone = true;
                    });
                    AsyncGPUReadbackRequest shGRequest = AsyncGPUReadback.Request(shGTex, 0, gridPos.x, 1, gridPos.y, 1, gridPos.z, 1, request =>
                    {
                        NativeArray<half4> shGData = request.GetData<half4>();
                        half4 shG = shGData[0];
                        data.shG = new Vector4(shG.x, shG.y, shG.z, shG.w);
                        shGDone = true;
                    });
                    AsyncGPUReadbackRequest shBRequest = AsyncGPUReadback.Request(shBTex, 0, gridPos.x, 1, gridPos.y, 1, gridPos.z, 1, request =>
                    {
                        NativeArray<half4> shBData = request.GetData<half4>();
                        half4 shB = shBData[0];
                        data.shB = new Vector4(shB.x, shB.y, shB.z, shB.w);
                        shBDone = true;
                    });

                    while (!shRDone || !shGDone || !shBDone)
                    {
                        await Task.Yield();
                    }

                    callback.Invoke(data);
                }
            }
        }

        public void Initialize()
        {
            InitEmptyTexture();
            Dispose();

            gridWidth = Mathf.CeilToInt(gridSize.x * gridResolution);
            gridHeight = Mathf.CeilToInt(gridSize.y * gridResolution);
            gridDepth = Mathf.CeilToInt(gridSize.z * gridResolution);

            long volume = gridWidth * gridHeight * gridDepth;
            if (volume > MAX_GRID_POINTS)
            {
                Debug.LogError("AdaptiveGI lighting resolution or render volume size exceeded maximum limit.");

                gridWidth = 1;
                gridHeight = 1;
                gridDepth = 1;
            }

            lightGridPosition = adaptiveGI.gridPosition;
            lightGridSize = (float3)(Vector3)gridSize;
            worldToLocalOffset = (lightGridSize / 2) - new float3(1f / (2 * gridResolution), 1f / (2 * gridResolution), 1f / (2 * gridResolution));
            gridToWorldOffset = -(lightGridSize / 2) + new float3(1f / (2 * gridResolution), 1f / (2 * gridResolution), 1f / (2 * gridResolution));

            useComputeShader = enableComputeShaderSupport && SupportsComputeShader();
            useOptimizedLoadActions = enableRenderBufferOptimizations && SupportsRenderBufferOptimizations();

            shRPrimary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };
            shGPrimary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };
            shBPrimary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };

            shRSecondary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };
            shGSecondary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };
            shBSecondary = new RenderTexture(gridWidth, gridHeight, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = useComputeShader,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                volumeDepth = gridDepth,
                dimension = TextureDimension.Tex3D,
                anisoLevel = 0,
                autoGenerateMips = false,
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            };

            _ = shRPrimary.Create();
            _ = shGPrimary.Create();
            _ = shBPrimary.Create();

            _ = shRSecondary.Create();
            _ = shGSecondary.Create();
            _ = shBSecondary.Create();

            primaryBuffers = new RenderBuffer[]
            {
                shRPrimary.colorBuffer,
                shGPrimary.colorBuffer,
                shBPrimary.colorBuffer
            };

            secondaryBuffers = new RenderBuffer[]
            {
                shRSecondary.colorBuffer,
                shGSecondary.colorBuffer,
                shBSecondary.colorBuffer
            };

            isUsingPrimary = true;

            currentSlice = 0;
            currentCasterIndex = 0;

            lightTexture = new Texture2D(LIGHT_TEXTURE_STRIDE, maxLights, TextureFormat.RGBAFloat, false);
            lightTextureData = lightTexture.GetPixelData<float4>(0);

            lightsToCalculate = new NativeArray<LightToCalculate>(maxLights, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            shadowTexture = new Texture3D(gridWidth, gridHeight, gridDepth, TextureFormat.RHalf, false);
            shadowTextureData = shadowTexture.GetPixelData<half>(0);

            shadowChunks = new Dictionary<int3, ShadowChunk>();
            int maximumChunks = (int)math.pow((2 * shadowChunkUpdateRadius) + 1, 3);
            chunksForThisBatch = new ShadowChunk[maximumChunks];

            if (adaptiveGI.voxelizeSceneOnInitialize)
            {
                VoxelizeSceneShadowChunks();
            }

            if (useComputeShader)
            {
                calculateAdaptiveGICompute = Resources.Load<ComputeShader>("CalculateAdaptiveGICompute");
                kernel = calculateAdaptiveGICompute.FindKernel("CSMain");
                threadGroupSizeX = Mathf.CeilToInt(gridWidth / (float)NUM_COMPUTE_THREADS);
                threadGroupSizeY = Mathf.CeilToInt(gridHeight / (float)NUM_COMPUTE_THREADS);

                indicesTexture = new Texture2D(gridDepth, 1, TextureFormat.RGFloat, false);
                indicesTextureData = indicesTexture.GetPixelData<float2>(0);
            }
            else
            {
                calculateAdaptiveGIFragment = Resources.Load<Shader>("CalculateAdaptiveGIFragment");
                calculateAdaptiveGIMaterial = new Material(calculateAdaptiveGIFragment);
            }

            renderTargetSetup = new RenderTargetSetup
            {
                mipLevel = 0,
                cubemapFace = CubemapFace.Unknown,
                depthSlice = currentSlice,
                depth = shRPrimary.depthBuffer,
                colorLoad = useOptimizedLoadActions ? LOAD_ACTIONS_OPTIMIZED : LOAD_ACTIONS_COMPATIBILITY,
                depthLoad = RenderBufferLoadAction.DontCare,
                colorStore = STORE_ACTIONS,
                depthStore = RenderBufferStoreAction.DontCare,
                color = primaryBuffers
            };

            SetShaderGlobals();
            Shader.SetGlobalVector(PROPERTY_GRIDSIZE, (Vector3)(1f / (new float3(gridWidth, gridHeight, gridDepth) * inverseGridResolution)));
            Shader.SetGlobalVector(PROPERTY_GRIDDIMENSION, new Vector3(gridWidth, gridHeight, gridDepth));
            Shader.SetGlobalVector(PROPERTY_GRIDINVDIMENSION, new Vector3(1f / gridWidth, 1f / gridHeight, 1f / gridDepth));
            Shader.SetGlobalTexture(PROPERTY_GRIDSHR, emptyTexture);
            Shader.SetGlobalTexture(PROPERTY_GRIDSHG, emptyTexture);
            Shader.SetGlobalTexture(PROPERTY_GRIDSHB, emptyTexture);
        }

        private bool SupportsComputeShader()
        {
            return SystemInfo.supportsComputeShaders &&
                Application.platform != RuntimePlatform.OSXEditor &&
                Application.platform != RuntimePlatform.OSXPlayer &&
                Application.platform != RuntimePlatform.IPhonePlayer;
        }

        private bool SupportsRenderBufferOptimizations()
        {
            return Application.platform != RuntimePlatform.WebGLPlayer;
        }

        public void Dispose()
        {
            Shader.SetGlobalVector(PROPERTY_GRIDSIZE, Vector3.zero);
            Shader.SetGlobalTexture(PROPERTY_GRIDSHR, emptyTexture);
            Shader.SetGlobalTexture(PROPERTY_GRIDSHG, emptyTexture);
            Shader.SetGlobalTexture(PROPERTY_GRIDSHB, emptyTexture);
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHR, Vector4.zero);
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHG, Vector4.zero);
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHB, Vector4.zero);

            DisposeShadowChunks();

            if (lightTexture != null)
            {
                Object.DestroyImmediate(lightTexture);
            }

            if (indicesTexture != null)
            {
                Object.DestroyImmediate(indicesTexture);
            }

            if (shadowTexture != null)
            {
                Object.DestroyImmediate(shadowTexture);
            }

            if (calculateAdaptiveGIMaterial != null)
            {
                Object.DestroyImmediate(calculateAdaptiveGIMaterial);
            }

            if (lightsToCalculate.IsCreated)
            {
                lightsToCalculate.Dispose();
            }

            if (lightTextureData.IsCreated)
            {
                lightTextureData.Dispose();
            }

            if (indicesTextureData.IsCreated)
            {
                indicesTextureData.Dispose();
            }

            if (shadowTextureData.IsCreated)
            {
                shadowTextureData.Dispose();
            }

            Graphics.SetRenderTarget(null);

            if (shRPrimary != null)
            {
                shRPrimary.Release();
            }

            if (shGPrimary != null)
            {
                shGPrimary.Release();
            }

            if (shBPrimary != null)
            {
                shBPrimary.Release();
            }

            if (shRSecondary != null)
            {
                shRSecondary.Release();
            }

            if (shGSecondary != null)
            {
                shGSecondary.Release();
            }

            if (shBSecondary != null)
            {
                shBSecondary.Release();
            }
        }

        private void DisposeShadowChunks()
        {
            if (shadowChunks != null)
            {
                foreach (ShadowChunk chunk in shadowChunks.Values)
                {
                    chunk.Dispose();
                }
                shadowChunks.Clear();
            }
        }

        private void InitEmptyTexture()
        {
            if (emptyTexture == null)
            {
                emptyTexture = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);
                emptyTexture.SetPixel(0, 0, 0, new Color(0, 0, 0, 0));
                emptyTexture.Apply();
            }
        }

        internal void UpdateLightGrid()
        {
            if (adaptiveGI.enableShadows)
            {
                UpdateShadowCasterGrid(currentSlice == 0);
                UpdateShadowChunks();
            }

            if (currentSlice == 0)
            {
                lightGridPosition = adaptiveGI.gridPosition;
                gridBounds = new SoftBounds(lightGridPosition, lightGridSize / 2);

                if (adaptiveGI.enableShadows)
                {
                    currentCasterIndex = 0;
                    CopyShadowChunks();
                    shadowTexture.Apply(false);
                    NativeUtils.ClearNativeArray(shadowTextureData);
                }

                lightCount = 0;
                StageGIProbes(ref lightCount, ref gridBounds, ref adaptiveGI.gIProbeLightsToCalculate, ref lightsToCalculate);
                StageAdaptiveLights();

                if (lightCount > 0)
                {
                    NativeArray<LightToCalculate> lightsToSort = lightsToCalculate.GetSubArray(0, lightCount);
                    lightsToSort.Sort(new LightZComparer());
                }

                StageLightData(lightCount, usingLinearColorSpace, ref lightGridPosition, ref worldToLocalOffset, ref gridResolution, ref lightsToCalculate, ref lightTextureData);
                lightTexture.Apply(false);

                if (useComputeShader)
                {
                    FindAllSliceLightIndices(lightCount, gridDepth, ref lightGridPosition, ref lightGridSize, inverseGridResolution, ref lightsToCalculate, ref indicesTextureData);
                    indicesTexture.Apply(false);
                }
                else
                {
                    lightStartIndex = 0;
                    lightEndIndex = lightCount;
                }
            }

            slicesPerFrame = Mathf.Max(1, Mathf.CeilToInt(gridDepth / (1f / adaptiveGI.gILightingUpdatesPerSecond) * Time.deltaTime));
            if (slicesPerFrame * gridWidth * gridHeight > MAX_GRID_POINTS_CALCULATED_PER_FRAME)
            {
                slicesPerFrame = MAX_GRID_POINTS_CALCULATED_PER_FRAME / (gridWidth * gridHeight);
            }

            if (useComputeShader)
            {
                CalculateComputeShader();
            }
            else
            {
                CalculateFragmentShader();
            }
        }

        public void VoxelizeSceneShadowChunks()
        {
            DisposeShadowChunks();

            Collider[] sceneColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            foreach (Collider collider in sceneColliders)
            {
                if (!(adaptiveGI.shadowVoxelizationMask == ~0 || (adaptiveGI.shadowVoxelizationMask.value & (1 << collider.gameObject.layer)) != 0))
                {
                    continue;
                }

                Vector3 min = collider.bounds.min;
                Vector3 max = collider.bounds.max;

                int3 chunkMin = WorldToChunk(min);
                int3 chunkMax = WorldToChunk(max);

                for (int x = chunkMin.x; x <= chunkMax.x; x++)
                {
                    for (int y = chunkMin.y; y <= chunkMax.y; y++)
                    {
                        for (int z = chunkMin.z; z <= chunkMax.z; z++)
                        {
                            int3 chunkPos = new int3(x, y, z);

                            if (!shadowChunks.TryGetValue(chunkPos, out ShadowChunk chunk))
                            {
                                chunk = new ShadowChunk(chunkPos, shadowChunkSize);
                                shadowChunks.Add(chunkPos, chunk);
                            }
                        }
                    }
                }
            }

            NativeList<OverlapSphereCommand> commands = new NativeList<OverlapSphereCommand>(Allocator.TempJob);
            float voxelRadius = inverseGridResolution * adaptiveGI.shadowVoxelizationRadius;
            foreach (ShadowChunk shadowChunk in shadowChunks.Values)
            {
                ShadowChunk chunk = shadowChunk;
                ScheduleShadowChunk(ref commands, ref chunk, ref shadowChunkSize, ref inverseGridResolution, ref voxelRadius, adaptiveGI.shadowVoxelizationMask);
            }

            if (commands.Length > 0)
            {
                NativeArray<ColliderHit> results = new NativeArray<ColliderHit>(commands.Length, Allocator.TempJob);

                JobHandle jobHandle = OverlapSphereCommand.ScheduleBatch(commands.AsArray(), results, commands.Length / adaptiveGI.perJobComputeThreads, 1);
                jobHandle.Complete();

                int commandIndex = 0;
                foreach (ShadowChunk shadowChunk in shadowChunks.Values)
                {
                    ShadowChunk chunk = shadowChunk;
                    ProcessShadowChunk(ref results, ref chunk, ref shadowChunkSize, ref commandIndex, ref AdaptiveGIMaterial.colliderMaterialProperties);
                }

                results.Dispose();
            }

            commands.Dispose();
        }

        private void UpdateShadowChunks()
        {
            if (currentUpdateX > shadowChunkUpdateRadius)
            {
                currentUpdateX = -shadowChunkUpdateRadius;
                currentUpdateY = -shadowChunkUpdateRadius;
                currentUpdateZ = -shadowChunkUpdateRadius;
            }

            int3 centerChunkPos = WorldToChunk(adaptiveGI.currentCameraPosition);

            NativeList<OverlapSphereCommand> commands = new NativeList<OverlapSphereCommand>(Allocator.TempJob);
            int chunksProcessedThisFrame = 0;

            float halfChunkWorldSize = shadowChunkSize * inverseGridResolution * 0.5f;
            Vector3 halfExtents = new Vector3(halfChunkWorldSize, halfChunkWorldSize, halfChunkWorldSize);
            float voxelRadius = inverseGridResolution * adaptiveGI.shadowVoxelizationRadius;

            while (currentUpdateX <= shadowChunkUpdateRadius && chunksProcessedThisFrame < adaptiveGI.shadowChunksPerFrame)
            {
                int3 offset = new int3(currentUpdateX, currentUpdateY, currentUpdateZ);
                int3 chunkPos = centerChunkPos + offset;
                float3 worldChunkPos = ChunkToWorld(chunkPos);

                if (Physics.CheckBox(worldChunkPos, halfExtents, Quaternion.identity, adaptiveGI.shadowVoxelizationMask))
                {
                    if (!shadowChunks.TryGetValue(chunkPos, out ShadowChunk chunk))
                    {
                        chunk = new ShadowChunk(chunkPos, shadowChunkSize);
                        shadowChunks.Add(chunkPos, chunk);
                    }

                    ScheduleShadowChunk(ref commands, ref chunk, ref shadowChunkSize, ref inverseGridResolution, ref voxelRadius, adaptiveGI.shadowVoxelizationMask);
                    chunksForThisBatch[chunksProcessedThisFrame] = chunk;
                    chunksProcessedThisFrame++;
                }
                else
                {
                    if (shadowChunks.TryGetValue(chunkPos, out ShadowChunk chunkToRemove))
                    {
                        chunkToRemove.Dispose();
                        _ = shadowChunks.Remove(chunkPos);
                    }
                }

                currentUpdateZ++;
                if (currentUpdateZ > shadowChunkUpdateRadius)
                {
                    currentUpdateZ = -shadowChunkUpdateRadius;
                    currentUpdateY++;
                    if (currentUpdateY > shadowChunkUpdateRadius)
                    {
                        currentUpdateY = -shadowChunkUpdateRadius;
                        currentUpdateX++;
                    }
                }
            }

            if (commands.Length > 0)
            {
                NativeArray<ColliderHit> results = new NativeArray<ColliderHit>(commands.Length, Allocator.TempJob);

                JobHandle jobHandle = OverlapSphereCommand.ScheduleBatch(commands.AsArray(), results, commands.Length / adaptiveGI.perJobComputeThreads, 1);
                jobHandle.Complete();

                int commandIndex = 0;
                for (int i = 0; i < chunksProcessedThisFrame; i++)
                {
                    ShadowChunk chunk = chunksForThisBatch[i];
                    ProcessShadowChunk(ref results, ref chunk, ref shadowChunkSize, ref commandIndex, ref AdaptiveGIMaterial.colliderMaterialProperties);
                }

                results.Dispose();
            }

            commands.Dispose();
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void ScheduleShadowChunk(ref NativeList<OverlapSphereCommand> commands, ref ShadowChunk chunk, ref int shadowChunkSize, ref float inverseGridResolution, ref float voxelRadius, int layerMask)
        {
            for (int x = 0; x < shadowChunkSize; x++)
            {
                for (int y = 0; y < shadowChunkSize; y++)
                {
                    for (int z = 0; z < shadowChunkSize; z++)
                    {
                        int3 voxelPos = new int3(x, y, z);
                        ShadowChunk.VoxelToWorld(ref voxelPos, ref chunk.position, shadowChunkSize, inverseGridResolution, out float3 worldPos);
                        commands.Add(new OverlapSphereCommand(worldPos, voxelRadius, new QueryParameters(layerMask: layerMask)));
                    }
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void ProcessShadowChunk(ref NativeArray<ColliderHit> results, ref ShadowChunk chunk, ref int shadowChunkSize, ref int commandIndex, ref NativeHashMap<int, AdaptiveGIMaterial.MaterialProperties> colliderMaterialProperties)
        {
            for (int x = 0; x < shadowChunkSize; x++)
            {
                for (int y = 0; y < shadowChunkSize; y++)
                {
                    for (int z = 0; z < shadowChunkSize; z++)
                    {
                        ColliderHit hit = results[commandIndex];
                        bool hasHit = hit.instanceID != 0;
                        float opacity = hasHit ? 1 : 0;
                        if (hasHit && colliderMaterialProperties.IsCreated && colliderMaterialProperties.TryGetValue(hit.instanceID, out AdaptiveGIMaterial.MaterialProperties material))
                        {
                            opacity = material.opacity;
                        }

                        int3 voxelPos = new int3(x, y, z);
                        int index = GridUtils.FlattenIndex(ref voxelPos, shadowChunkSize, shadowChunkSize);

                        chunk.data[index] = (byte)(opacity * byte.MaxValue);

                        commandIndex++;
                    }
                }
            }
        }

        private int3 WorldToChunk(float3 worldPos)
        {
            float chunkWorldSize = shadowChunkSize * inverseGridResolution;
            return (int3)math.round(worldPos / chunkWorldSize);
        }

        private float3 ChunkToWorld(int3 chunkPos)
        {
            float chunkWorldSize = shadowChunkSize * inverseGridResolution;
            return (float3)chunkPos * chunkWorldSize;
        }

        private void CopyShadowChunks()
        {
            if (shadowChunks.Count == 0)
            {
                return;
            }

            int chunkSizeCubed = shadowChunkSize * shadowChunkSize * shadowChunkSize;

            int3 minChunk = WorldToChunk(gridBounds.min);
            int3 maxChunk = WorldToChunk(gridBounds.max);

            JobHandle dependencyHandle = new JobHandle();

            for (int cx = minChunk.x; cx <= maxChunk.x; cx++)
            {
                for (int cy = minChunk.y; cy <= maxChunk.y; cy++)
                {
                    for (int cz = minChunk.z; cz <= maxChunk.z; cz++)
                    {
                        int3 chunkPos = new int3(cx, cy, cz);

                        if (shadowChunks.TryGetValue(chunkPos, out ShadowChunk chunk))
                        {
                            CopyShadowChunkJob copyShadowChunkJob = new CopyShadowChunkJob
                            {
                                shadowChunkPosition = chunkPos,
                                shadowChunkSize = shadowChunkSize,
                                gridResolution = gridResolution,
                                inverseGridResolution = inverseGridResolution,
                                lightGridPosition = lightGridPosition,
                                worldToLocalOffset = worldToLocalOffset,
                                gridWidth = gridWidth,
                                gridHeight = gridHeight,
                                gridDepth = gridDepth,
                                chunkData = chunk.data,
                                shadowTextureData = shadowTextureData
                            };

                            dependencyHandle = copyShadowChunkJob.Schedule(chunkSizeCubed, chunkSizeCubed / adaptiveGI.perJobComputeThreads, dependencyHandle);
                        }
                    }
                }
            }

            dependencyHandle.Complete();
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct CopyShadowChunkJob : IJobParallelFor
        {
            public int3 shadowChunkPosition;
            public int shadowChunkSize;

            public float gridResolution;
            public float inverseGridResolution;

            public float3 lightGridPosition;
            public float3 worldToLocalOffset;

            public int gridWidth;
            public int gridHeight;
            public int gridDepth;

            [ReadOnly]
            public NativeArray<byte> chunkData;
            [NativeDisableParallelForRestriction]
            public NativeArray<half> shadowTextureData;

            public void Execute(int index)
            {
                GridUtils.UnflattenIndex(index, shadowChunkSize, shadowChunkSize, out int3 voxelPos);
                byte voxelData = chunkData[index];
                if (voxelData == 0)
                {
                    return;
                }

                ShadowChunk.VoxelToWorld(ref voxelPos, ref shadowChunkPosition, shadowChunkSize, inverseGridResolution, out float3 voxelWorldPos);
                GridUtils.WorldToLocal(ref voxelWorldPos, ref lightGridPosition, ref worldToLocalOffset, gridResolution, out float3 localPos);
                int3 gridPos = (int3)math.ceil(localPos);

                if (GridUtils.GridContainsPoint(ref gridPos, gridWidth, gridHeight, gridDepth))
                {
                    int voxelIndex = GridUtils.FlattenIndex(ref gridPos, gridWidth, gridHeight);
                    shadowTextureData[voxelIndex] += (half)((float)voxelData / byte.MaxValue);
                }
            }
        }

        private void UpdateShadowCasterGrid(bool calculateRemaining)
        {
            if (AdaptiveShadowCaster.Instances == null)
            {
                return;
            }

            int totalCasters = AdaptiveShadowCaster.Instances.Count;
            if (totalCasters == 0 || currentCasterIndex >= totalCasters)
            {
                return;
            }

            int castersToProcess = calculateRemaining
                ? totalCasters - currentCasterIndex
                : Mathf.Max(1, Mathf.CeilToInt(totalCasters / (1f / adaptiveGI.gILightingUpdatesPerSecond) * Time.deltaTime));

            int processed = 0;
            for (int i = 0; i < castersToProcess; i++)
            {
                int index = currentCasterIndex + i;
                if (index >= totalCasters)
                {
                    break;
                }

                AdaptiveShadowCaster shadowCaster = AdaptiveShadowCaster.Instances[index];
                float3 position = shadowCaster.transform.position;
                GridUtils.WorldToLocal(
                    ref position,
                    ref lightGridPosition,
                    ref worldToLocalOffset,
                    gridResolution,
                    out float3 localPosition
                );
                quaternion rotation = shadowCaster.transform.rotation;
                float3 localSize = shadowCaster.transform.lossyScale * gridResolution;

                switch (shadowCaster.type)
                {
                    case AdaptiveShadowCasterType.Cube:
                        CalculateBoxShadow(
                            ref localPosition,
                            ref rotation,
                            ref localSize,
                            shadowCaster.opacity,
                            ref shadowTextureData,
                            gridWidth,
                            gridHeight,
                            gridDepth,
                            inverseGridResolution
                        );
                        break;
                    case AdaptiveShadowCasterType.Sphere:
                        CalculateSphereShadow(
                            ref localPosition,
                            math.max(math.max(localSize.x, localSize.y), localSize.z) / 2,
                            shadowCaster.opacity,
                            ref shadowTextureData,
                            gridWidth,
                            gridHeight,
                            gridDepth,
                            gridResolution
                        );
                        break;
                }
                processed++;
            }

            currentCasterIndex += processed;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void CalculateBoxShadow(
            ref float3 localPosition,
            ref quaternion rotation,
            ref float3 localSize,
            float opacity,
            ref NativeArray<half> shadowTextureData,
            int gridWidth,
            int gridHeight,
            int gridDepth,
            float inverseGridResolution
        )
        {
            quaternion invRotation = math.inverse(rotation);
            float3 halfSize = localSize / 2f;

            float3x3 rotMatrix = math.float3x3(invRotation);
            float3 cellExtent = 0.5f * (new float3(math.csum(math.abs(rotMatrix.c0)), math.csum(math.abs(rotMatrix.c1)), math.csum(math.abs(rotMatrix.c2))) * inverseGridResolution);

            float maxSize = math.max(math.max(halfSize.x, halfSize.y), halfSize.z);
            int3 minGrid = (int3)math.floor(localPosition - maxSize);
            int3 maxGrid = (int3)math.ceil(localPosition + maxSize);

            int startX = math.max(0, minGrid.x);
            int endX = math.min(gridWidth - 1, maxGrid.x);
            int startY = math.max(0, minGrid.y);
            int endY = math.min(gridHeight - 1, maxGrid.y);
            int startZ = math.max(0, minGrid.z);
            int endZ = math.min(gridDepth - 1, maxGrid.z);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    for (int z = startZ; z <= endZ; z++)
                    {
                        int3 gridPos = new int3(x, y, z);
                        float3 translated = gridPos - localPosition;
                        float3 rotatedCenter = math.rotate(invRotation, translated);

                        float3 cellMin = rotatedCenter - cellExtent;
                        float3 cellMax = rotatedCenter + cellExtent;

                        float3 overlapMin = math.max(cellMin, -halfSize);
                        float3 overlapMax = math.min(cellMax, halfSize);
                        float3 penetration = overlapMax - overlapMin;

                        if (math.all(penetration > 0))
                        {
                            float3 penetrationFactor = penetration / (2 * cellExtent);
                            float volumeFactor = penetrationFactor.x * penetrationFactor.y * penetrationFactor.z;

                            float smoothFactor = math.smoothstep(0.0f, 1.0f, volumeFactor);
                            half finalOpacity = (half)(opacity * smoothFactor);
                            int index = GridUtils.FlattenIndex(ref gridPos, gridWidth, gridHeight);
                            shadowTextureData[index] += (half)math.max(shadowTextureData[index], finalOpacity);
                        }
                    }
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void CalculateSphereShadow(
            ref float3 localPosition,
            float radius,
            float opacity,
            ref NativeArray<half> shadowTextureData,
            int gridWidth,
            int gridHeight,
            int gridDepth,
            float gridResolution
        )
        {
            int3 minGrid = (int3)math.floor(localPosition - radius);
            int3 maxGrid = (int3)math.ceil(localPosition + radius);

            int startX = math.max(0, minGrid.x);
            int endX = math.min(gridWidth - 1, maxGrid.x);
            int startY = math.max(0, minGrid.y);
            int endY = math.min(gridHeight - 1, maxGrid.y);
            int startZ = math.max(0, minGrid.z);
            int endZ = math.min(gridDepth - 1, maxGrid.z);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    for (int z = startZ; z <= endZ; z++)
                    {
                        int3 gridPos = new int3(x, y, z);
                        float distance = math.length(gridPos - localPosition) - radius;
                        float penetration = 1.0f - math.saturate(distance * gridResolution);

                        if (penetration > 0)
                        {
                            float smoothFactor = math.smoothstep(0.0f, 1.0f, penetration);
                            half finalOpacity = (half)(opacity * smoothFactor);
                            int index = GridUtils.FlattenIndex(ref gridPos, gridWidth, gridHeight);
                            shadowTextureData[index] += (half)math.max(shadowTextureData[index], finalOpacity);
                        }
                    }
                }
            }
        }

        private void StageAdaptiveLights()
        {
            if (AdaptiveLight.Instances != null)
            {
                for (int i = 0; i < AdaptiveLight.Instances.Count; i++)
                {
                    AdaptiveLight adaptiveLight = AdaptiveLight.Instances[i];
                    if (adaptiveLight.isActive)
                    {
                        LightToCalculate light = new(adaptiveLight, true);
                        if (SoftBounds.Intersects(ref light.bounds, ref gridBounds))
                        {
                            lightsToCalculate[lightCount] = light;

                            lightCount++;
                            if (lightCount >= maxLights)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void StageGIProbes(ref int lightCount, ref SoftBounds gridBounds, ref NativeArray<LightToCalculate> gIProbeLightsToCalculate, ref NativeArray<LightToCalculate> lightsToCalculate)
        {
            for (int i = 0; i < gIProbeLightsToCalculate.Length; i++)
            {
                LightToCalculate light = gIProbeLightsToCalculate[i];
                if (light.isActive && SoftBounds.Intersects(ref light.bounds, ref gridBounds))
                {
                    lightsToCalculate[lightCount] = light;
                    lightCount++;
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void StageLightData(int lightCount, bool usingLinearColorSpace, ref float3 lightGridPosition, ref float3 worldToLocalOffset, ref float gridResolution, ref NativeArray<LightToCalculate> lightsToCalculate, ref NativeArray<float4> lightTextureData)
        {
            for (int i = 0; i < lightCount; i++)
            {
                LightToCalculate light = lightsToCalculate[i];
                GridUtils.WorldToLocal(ref light.position, ref lightGridPosition, ref worldToLocalOffset, gridResolution, out float3 localPosition);
                float3 adjustedColor = usingLinearColorSpace ? math.pow(light.color, GAMMA) : math.pow(light.color, 1f / GAMMA);

                int baseIndex = i * LIGHT_TEXTURE_STRIDE;
                lightTextureData[baseIndex + 0] = new float4(adjustedColor.x, adjustedColor.y, adjustedColor.z, light.intensity);
                lightTextureData[baseIndex + 1] = new float4(light.conePower, light.power, light.deadzone, light.rangeSquared);
                lightTextureData[baseIndex + 2] = new float4(light.outerSpotAngle, light.innerSpotAngle, localPosition.x, localPosition.y);
                lightTextureData[baseIndex + 3] = new float4(localPosition.z, light.direction.x, light.direction.y, light.direction.z);
                lightTextureData[baseIndex + 4] = new float4(light.shadowStrength, light.shadowSoftness, light.shadowDepthBias, light.shadowStartBias);
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void FindAllSliceLightIndices(int lightCount, int gridDepth, ref float3 lightGridPosition, ref float3 lightGridSize, float inverseGridResolution, ref NativeArray<LightToCalculate> lightsToCalculate, ref NativeArray<float2> indicesTextureData)
        {
            int lightStartIndex = 0;
            int lightEndIndex = 0;
            for (int z = 0; z < gridDepth; z++)
            {
                float sliceZ = lightGridPosition.z - (lightGridSize.z / 2f) + (z * inverseGridResolution);

                lightStartIndex = lightEndIndex;
                lightEndIndex = lightCount;

                for (int i = 0; i < lightCount; i++)
                {
                    if (lightsToCalculate[i].bounds.max.z >= sliceZ)
                    {
                        lightStartIndex = i;
                        break;
                    }
                }

                for (int i = lightStartIndex; i < lightCount; i++)
                {
                    if (lightsToCalculate[i].bounds.min.z > sliceZ)
                    {
                        lightEndIndex = i;
                        break;
                    }
                }

                indicesTextureData[z] = new float2(lightStartIndex, lightEndIndex);
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void FindSliceLightIndices(int lightCount, int currentSlice, ref int lightStartIndex, ref int lightEndIndex, ref float3 lightGridPosition, ref float3 lightGridSize, float inverseGridResolution, ref NativeArray<LightToCalculate> lightsToCalculate)
        {
            float sliceZ = lightGridPosition.z - (lightGridSize.z / 2f) + (currentSlice * inverseGridResolution);

            lightStartIndex = lightEndIndex;
            lightEndIndex = lightCount;

            for (int i = 0; i < lightCount; i++)
            {
                if (lightsToCalculate[i].bounds.max.z >= sliceZ)
                {
                    lightStartIndex = i;
                    break;
                }
            }

            for (int i = lightStartIndex; i < lightCount; i++)
            {
                if (lightsToCalculate[i].bounds.min.z > sliceZ)
                {
                    lightEndIndex = i;
                    break;
                }
            }
        }

        private void CalculateFragmentShader()
        {
            calculateAdaptiveGIMaterial.SetTexture(PROPERTY_LIGHTTEXTURE, lightTexture);
            calculateAdaptiveGIMaterial.SetTexture(PROPERTY_SHADOWGRID, shadowTexture);
            calculateAdaptiveGIMaterial.SetVector(PROPERTY_GRIDSIZE, new Vector3(gridWidth, gridHeight, gridDepth));
            calculateAdaptiveGIMaterial.SetFloat(PROPERTY_GRIDRESOLUTION, gridResolution);

            if (adaptiveGI.enableShadows)
            {
                calculateAdaptiveGIMaterial.EnableKeyword(KEYWORD_SHADOWS_ENABLED);
            }
            else
            {
                calculateAdaptiveGIMaterial.DisableKeyword(KEYWORD_SHADOWS_ENABLED);
            }

            for (int i = 0; i < slicesPerFrame; i++)
            {
                FindSliceLightIndices(lightCount, currentSlice, ref lightStartIndex, ref lightEndIndex, ref lightGridPosition, ref lightGridSize, inverseGridResolution, ref lightsToCalculate);
                calculateAdaptiveGIMaterial.SetInt(PROPERTY_CURRENTSLICE, currentSlice);
                calculateAdaptiveGIMaterial.SetInt(PROPERTY_LIGHTSTARTINDEX, lightStartIndex);
                calculateAdaptiveGIMaterial.SetInt(PROPERTY_LIGHTENDINDEX, lightEndIndex);
                renderTargetSetup.depthSlice = currentSlice;

                Graphics.SetRenderTarget(renderTargetSetup);
                Graphics.Blit(emptyTexture, calculateAdaptiveGIMaterial);

                currentSlice++;
                if (currentSlice == gridDepth)
                {
                    SetShaderGlobals();

                    isUsingPrimary = !isUsingPrimary;

                    renderTargetSetup.color = isUsingPrimary ? primaryBuffers : secondaryBuffers;

                    currentSlice = 0;
                    break;
                }
            }
        }

        private void CalculateComputeShader()
        {
            calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_LIGHTTEXTURE, lightTexture);
            calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_INDICESTEXTURE, indicesTexture);
            calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_SHADOWGRID, shadowTexture);
            calculateAdaptiveGICompute.SetVector(PROPERTY_GRIDSIZE, new Vector3(gridWidth, gridHeight, gridDepth));
            calculateAdaptiveGICompute.SetFloat(PROPERTY_GRIDRESOLUTION, gridResolution);

            calculateAdaptiveGICompute.SetInt(PROPERTY_CURRENTSLICE, currentSlice);

            if (adaptiveGI.enableShadows)
            {
                calculateAdaptiveGICompute.EnableKeyword(KEYWORD_SHADOWS_ENABLED);
            }
            else
            {
                calculateAdaptiveGICompute.DisableKeyword(KEYWORD_SHADOWS_ENABLED);
            }

            if (isUsingPrimary)
            {
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHR, shRPrimary);
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHG, shGPrimary);
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHB, shBPrimary);
            }
            else
            {
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHR, shRSecondary);
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHG, shGSecondary);
                calculateAdaptiveGICompute.SetTexture(kernel, PROPERTY_GRIDSHB, shBSecondary);
            }

            int slicesToCalculate = Mathf.Min(gridDepth - currentSlice, slicesPerFrame);

            calculateAdaptiveGICompute.Dispatch(kernel, threadGroupSizeX, threadGroupSizeY, slicesToCalculate);

            currentSlice += slicesToCalculate;

            if (currentSlice == gridDepth)
            {
                SetShaderGlobals();

                isUsingPrimary = !isUsingPrimary;

                currentSlice = 0;
            }
        }

        private void SetShaderGlobals()
        {
            if (isUsingPrimary)
            {
                Shader.SetGlobalTexture(PROPERTY_GRIDSHR, shRPrimary);
                Shader.SetGlobalTexture(PROPERTY_GRIDSHG, shGPrimary);
                Shader.SetGlobalTexture(PROPERTY_GRIDSHB, shBPrimary);
            }
            else
            {
                Shader.SetGlobalTexture(PROPERTY_GRIDSHR, shRSecondary);
                Shader.SetGlobalTexture(PROPERTY_GRIDSHG, shGSecondary);
                Shader.SetGlobalTexture(PROPERTY_GRIDSHB, shBSecondary);
            }
            Shader.SetGlobalVector(PROPERTY_GRIDPOSITION, new Vector3(lightGridPosition.x, lightGridPosition.y, lightGridPosition.z));
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHR, (adaptiveGI.ambientProbe.shR + new Vector4(0, 0, 0, adaptiveGI.fallbackAdditiveColor.x)) * adaptiveGI.fallbackIntensity);
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHG, (adaptiveGI.ambientProbe.shG + new Vector4(0, 0, 0, adaptiveGI.fallbackAdditiveColor.y)) * adaptiveGI.fallbackIntensity);
            Shader.SetGlobalVector(PROPERTY_FALLBACKSHB, (adaptiveGI.ambientProbe.shB + new Vector4(0, 0, 0, adaptiveGI.fallbackAdditiveColor.z)) * adaptiveGI.fallbackIntensity);
            Shader.SetGlobalFloat(PROPERTY_FADESTARTDISTANCE, 1f / adaptiveGI.fadeStartDistance);
            if (adaptiveGI.smoothLighting)
            {
                Shader.EnableKeyword(KEYWORD_TRICUBIC_LIGHTING);
            }
            else
            {
                Shader.DisableKeyword(KEYWORD_TRICUBIC_LIGHTING);
            }
        }

        public struct LightZComparer : System.Collections.Generic.IComparer<LightToCalculate>
        {
            public readonly int Compare(LightToCalculate a, LightToCalculate b)
            {
                return a.bounds.min.z.CompareTo(b.bounds.min.z);
            }
        }
    }
}
