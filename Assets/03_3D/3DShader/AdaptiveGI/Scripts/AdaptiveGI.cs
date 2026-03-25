using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AdaptiveGI.Core;
using AdaptiveGI.GI;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[assembly: InternalsVisibleTo("AdaptiveGI.Editor")]
namespace AdaptiveGI
{
    [ExecuteAlways]
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    [AddComponentMenu("AdaptiveGI/Adaptive GI")]
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    [Description("The core component of AdaptiveGI. Manages global illumination visual and performance settings.")]
    public class AdaptiveGI : MonoBehaviour
    {
        public static AdaptiveGI Instance;

        [Header("Rendering")]
        [Tooltip("Should the render volume follow the camera or the transform of the GameObject this component is on.")]
        public bool followCamera = true;
        [Tooltip("Set this to override the camera that the render volume follows when Follow Camera is enabled.")]
        public Transform overrideCameraTransform = null;
        [Range(0, 1), Tooltip("The distance from the edge of the render volume that GI starts to fade.")]
        public float fadeStartDistance = 0.2f;
        [SerializeField, Min(1), Tooltip("The volume around the follow transform that GI is rendered.")]
        internal Vector3Int renderVolume = new(100, 100, 100);

        [Header("Ambient")]
        [Tooltip("The ambient color coming from the sky.")]
        public Color ambientSkyColor = new(0.212f, 0.227f, 0.259f);
        [Tooltip("The ambient color coming from the equator.")]
        public Color ambientEquatorColor = new(0.114f, 0.125f, 0.133f);
        [Tooltip("The ambient color coming from the ground.")]
        public Color ambientGroundColor = new(0.047f, 0.043f, 0.035f);
        [Tooltip("Directional ambient lights to contribute to ambient lighting.")]
        public AmbientLight[] ambientLights;
        [Min(0), Tooltip("The intensity of all ambient light.")]
        public float ambientIntensity = 1;

        [Header("Fallback")]
        [Range(0, 1), Tooltip("The intensity of the fallback lighting outside the render volume.")]
        public float fallbackIntensity = 0.1f;
        [Range(0, 1), Tooltip("The intensity of the directional light's influence on the fallback lighting outside the render volume.")]
        public float fallbackDirectionalLightIntensity = 0;

        [Header("Visuals")]
        [Tooltip("The layermask used for GI calculations.")]
        public LayerMask gIMask = ~0;
        [Tooltip("The layermask GI Probes are allowed to spawn on.")]
        public LayerMask gIProbeMask = ~0;
        [Min(0), Tooltip("The intensity of the global illumination.")]
        public float gIIntensity = 1;
        [Range(0, 1), Tooltip("The intensity of the color bouncing.")]
        public float gIColorBounceIntensity = 1;
        [Range(0, 1), Tooltip("The intensity of importance sampled probes.")]
        public float gIImportanceRayIntensity = 1;
        [Range(0, 10), Tooltip("Probes with fewer than this number of rays that contributed to GI will be importance sampled. Use this to brighten dark areas of a scene.")]
        public int gIImportanceRayCount = 5;
        [Range(0, 10), Tooltip("How much surface normal affects GI.")]
        public float gINormalPower = 1;
        [Range(0, 1), Tooltip("The power of the global illumination. Lower values reduce light attenuation.")]
        public float gIPower = 1;
        [Range(0.01f, 10), Tooltip("The range of the GI's influence.")]
        public float gIRange = 2;
        [Range(0.01f, 1), Tooltip("The deadzone around each GI Probe. Higher values increase the area around each probe where attenuation doesn't change.")]
        public float gIDeadzone = 0.5f;
        [Range(0, 1), Tooltip("The strength of shadows cast by GI Probes.")]
        public float gIShadowStrength = 1;
        [Range(0, 1), Tooltip("The softness of shadows cast by GI Probes.")]
        public float gIShadowSoftness = 0.5f;
        [Range(0, 10), Tooltip("The depth bias of shadows cast by GI Probes.")]
        public float gIShadowDepthBias = 1;
        [Range(0, 10), Tooltip("How far from GI Probes shadows start being cast.")]
        public float gIShadowStartBias = 0;

        [Header("Shadows")]
        [Tooltip("Whether shadows are calculated or not.")]
        public bool enableShadows = true;
        [Range(1, 2), Tooltip("A multiplier on GI Intensity when shadows are enabled to account for overall brightness decrease.")]
        public float shadowGIIntensityMultiplier = 1.5f;
        [Range(1, 10), Tooltip("How many chunks are voxelized each frame. Higher numbers update faster, but decrease framerates.")]
        public int shadowChunksPerFrame = 1;
        [Range(0, 2), Tooltip("The radius around each grid point that is checked for a solid collider when voxelizing.")]
        public float shadowVoxelizationRadius = 0.5f;
        [Tooltip("The layermask to voxelize for shadow casting.")]
        public LayerMask shadowVoxelizationMask = ~0;
        [SerializeField, Tooltip("Whether to increase loading times and pre voxelize the scene at startup.")]
        internal bool voxelizeSceneOnInitialize = false;
        [SerializeField, Tooltip("The size in voxels of each shadow chunk.")]
        internal ShadowChunkSize shadowChunkSize = ShadowChunkSize._16;
        [SerializeField, Range(1, 20), Tooltip("The radius in chunks to update voxels.")]
        internal int shadowChunkUpdateRadius = 2;

        [Header("Prewarming")]
        [Tooltip("Whether to prewarm GI Probes at startup. Enabling this increases initial load times.")]
        public bool prewarmGIProbesOnInitialize = true;
        [Range(1, 10), Tooltip("The number of recursive iterations on startup to place GI Probes. Higher values increase initial load times.")]
        public int prewarmIterations = 3;

        [Header("Performance")]
        [Tooltip("How many rays are fired from each probe. Heavily affects performance.")]
        public RayDirections.RayCount gIRayCount = RayDirections.RayCount._25;
        [Tooltip("The number of rays fired from the camera each frame for GI Probe placement.")]
        public RayDirections.RayCount probePlacementRayCount = RayDirections.RayCount._10;
        [Range(0.01f, 10), Tooltip("A multiplier on the cascade update intervals. A lower number will update GI faster, while a higher number will improve framerates and reduce GI responsiveness.")]
        public float gIProbeUpdateIntervalMultiplier = 1;
        [Range(1, 100), Tooltip("How many GI lighting updates are calculated per second. Lower numbers improve GPU performance.")]
        public int gILightingUpdatesPerSecond = 15;
        [Range(0.01f, 3), Tooltip("The density of GI Probes.")]
        public float probeResolution = 1;
        [Tooltip("Whether to smooth out lighting by applying more performance intensive tricubic sampling.")]
        public bool smoothLighting = false;
        [SerializeField, Range(0.01f, 3), Tooltip("The resolution of the lighting. This setting heavily affects performance.")]
        internal float lightingResolution = 1;
        [SerializeField, Min(0), Tooltip("How many lights can be rendered at a time.")]
        internal int maxLights = 500;
        [SerializeField, Range(100, 10000), Tooltip("The maximum number of probes in the world at one time, when this limit is exceeded, the farthest away probes are removed and reused.")]
        internal int maxProbes = 1000;
        [SerializeField, Range(1, 32), Tooltip("The number of threads used in GI calculation.")]
        internal int perJobComputeThreads = 4;

        [Header("Update Cascades")]
        [SerializeField, Tooltip("Distance thresholds for update cascades (must be in ascending order)")]
        internal float[] cascadeDistances = new float[] { 20, 60, 100, 200 };
        [SerializeField, Tooltip("Update intervals in seconds for each cascade (matched to distances)")]
        internal float[] cascadeUpdateIntervals = new float[] { 0.5f, 2, 8, 16 };

        [Header("Compatibility")]
        [SerializeField, Tooltip("Whether to enable the use of compute shaders or not. Should generally be enabled unless you are experiencing issues. This will fall back to false on known unsupported platforms such as MacOS.")]
        internal bool enableComputeShaderSupport = true;
        [SerializeField, Tooltip("Whether to enable underlying render buffer optimizations that can break compatibility with some platforms. Should generally be enabled unless you are experiencing issues. This will fall back to false on known unsupported platforms such as WebGL.")]
        internal bool enableRenderBufferOptimizations = true;
        [SerializeField, Tooltip("Whether to enable a warning being thrown to the console if Burst compilation is disabled. Should be enabled unless you are debugging Burst.")]
        internal bool enableBurstCompilationCheck = true;

        [Header("Gizmos")]
        [Tooltip("Should GI Probe Gizmos be displayed.")]
        public bool showProbeGizmos = false;
        [Tooltip("Should the shadow grid chunks be displayed.")]
        public bool showShadowChunks = false;

        private GIGrid giGrid;
        private bool isInitialized;
        private bool areProbesInitialized;
        private bool areCascadesInitialized;
        private int placementRayIndex;
        private float lastProbeResolution;

        internal bool usingLinearColorSpace;

        internal AmbientProbe ambientProbe;
        internal float3 fallbackAdditiveColor;

        internal Vector3 gridPosition;

        internal Light directionalLight;
        internal bool directionalLightEnabled;
        internal float3 directionalLightColor;
        internal float directionalLightIntensity;
        internal Vector3 dirLightBack;

        internal Vector3[] rayDirs;

        internal GIProbe[] gIProbes;
        internal NativeArray<LightToCalculate> gIProbeLightsToCalculate;
        internal NativeArray<float3> probePositions;
        internal NativeHashSet<int3> probeSet;
        internal NativeArray<int3> distanceCheckOffsets;
        internal float probeSpacing;
        internal float probeSpacingCellSize;
        internal float probeRange;

        internal Transform currentCameraTransform;
        internal float3 currentCameraPosition;

        private NativeArray<float> cascadeDistancesSq;
        private NativeArray<int> probeCascadeAssignments;
        private NativeArray<int> itemsInCascadeCount;
        private float[] cascadeAccumulators;
        private int[] cascadeCursors;
        private int[] probesToUpdate;
        private int probesToUpdateCount;

        private LightGIToCalculate[] raytracingLightGIData;
        private int[] bounceLightIndexes;
        private float3[] bounceColors;
        private int[] probeRayOffsets;
        private int[] bounceCountsPerProbe;

        private const float PROBE_RESOLUTION_FACTOR = 5f;
        internal const float NEARBY_OBJECT_FACTOR = 25f;
        internal const float CONTACT_OFFSET = 0.01f;

        private const int PREWARM_PLACEMENT_RAY_COUNT = 2000;
        private const int MAX_PROBES_UPDATED_PER_FRAME = 1000;
        private const float RANGE_FACTOR = 1.5f;

        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorUpdate;
#endif
            Initialize();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
            Dispose();
        }

        private void Reset()
        {
#if ADAPTIVE_GI_URP
            ambientIntensity = 1;
            gIImportanceRayIntensity = 1;
#elif ADAPTIVE_GI_HDRP
            ambientIntensity = 10000;
            gIImportanceRayIntensity = 0.5f;
#endif
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                MainUpdate();
            }
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (!Application.isPlaying && UnityEditor.EditorApplication.isFocused)
            {
                MainUpdate();
            }
        }
#endif

        private void MainUpdate()
        {
            if (!isInitialized)
            {
                return;
            }

            ValidateParameters();
            UpdateCamera();
            FollowTransform();
            FindProbeLocations((int)probePlacementRayCount);
            UpdateAmbientProbe();

            UpdateProbes();
            giGrid.UpdateLightGrid();
        }

        private void OnDrawGizmos()
        {
            if (showProbeGizmos)
            {
                for (int i = 0; i < gIProbes.Length; i++)
                {
                    if (!gIProbes[i].isActive)
                    {
                        continue;
                    }

                    Gizmos.color = new Color(gIProbes[i].color.x, gIProbes[i].color.y, gIProbes[i].color.z) * Mathf.Clamp01(gIProbes[i].intensity);
                    Gizmos.DrawWireSphere(gIProbes[i].position, probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR);
                    Gizmos.DrawRay(gIProbes[i].position, gIProbes[i].direction);
                }
            }
            if (showShadowChunks)
            {
                giGrid.DrawShadowChunksGizmo();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.DrawWireCube(gridPosition, renderVolume);
        }

        public void VoxelizeSceneShadowChunks()
        {
            if (!isInitialized)
            {
                return;
            }

            giGrid?.VoxelizeSceneShadowChunks();
        }

        [Description("Samples the global illumination at a world space position. A callback function needs to be provided to receive the GIData struct after the data is retrieved asyncronously.")]
        public void SampleGI(Vector3 position, System.Action<GIData> callback)
        {
            if (!isInitialized)
            {
                return;
            }

            giGrid?.SampleGI(position, callback);
        }

        [Description("Prewarms GI Probes synchronously so that GI is fully accumulated by the time this function returns. Use this to \"bake\" GI once a scene is finished loading (ex. procedural generation).")]
        public void Prewarm()
        {
            if (!isInitialized)
            {
                return;
            }

            ValidateParameters();
            UpdateCamera();
            FollowTransform();
            UpdateAmbientProbe();
            FindProbeLocations(PREWARM_PLACEMENT_RAY_COUNT);
            UpdateLightData();
            probeRange = gIRange + (probeSpacing * RANGE_FACTOR);
            probesToUpdateCount = maxProbes;
            for (int i = 0; i < maxProbes; i++)
            {
                probesToUpdate[i] = i;
            }

            for (int i = 0; i < prewarmIterations; i++)
            {
                BatchUpdateProbes(default, raytracingLightGIData, 0, true);
            }
        }

        internal void Initialize()
        {
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                isInitialized = false;
                return;
            }
#endif

            if (enableBurstCompilationCheck && BurstCompiler.Options.EnableBurstCompilation == false)
            {
                Debug.LogWarning("Burst compilation is disabled. AdaptiveGI's performance will be greatly reduced! On the toolbar, navigate to Jobs -> Burst -> Enable Compilation to enable Burst compilation. Uncheck 'Enable Burst Compilation Check' in the AdaptiveGI inspector to remove this warning.");
            }

            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogError("Another AdaptiveGI Instance already exists.");
                DestroyImmediate(gameObject);
                isInitialized = false;
                return;
            }

            usingLinearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;

            ambientProbe = new AmbientProbe();

            UpdateCamera();
            FollowTransform();

            giGrid ??= new GIGrid();
            giGrid.gridSize = renderVolume;
            giGrid.gridResolution = lightingResolution;
            giGrid.inverseGridResolution = 1f / lightingResolution;
            giGrid.maxLights = maxLights + maxProbes;
            giGrid.enableComputeShaderSupport = enableComputeShaderSupport;
            giGrid.enableRenderBufferOptimizations = enableRenderBufferOptimizations;
            giGrid.shadowChunkSize = (int)shadowChunkSize;
            giGrid.shadowChunkUpdateRadius = shadowChunkUpdateRadius;
            giGrid.adaptiveGI = this;
            giGrid.usingLinearColorSpace = usingLinearColorSpace;
            giGrid.Initialize();

            placementRayIndex = 0;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0;

            AdaptiveLightGI.Instances ??= new List<AdaptiveLightGI>();

            raytracingLightGIData = new LightGIToCalculate[maxLights];
            bounceLightIndexes = new int[maxProbes * (int)gIRayCount];
            bounceColors = new float3[maxProbes * (int)gIRayCount];
            probeRayOffsets = new int[maxProbes];
            bounceCountsPerProbe = new int[maxProbes];

            DisposeProbes();
            InitializeProbes();

            DisposeCascades();
            InitializeCascades();

            InitializeHdrpVolumes();

            isInitialized = true;

            if (prewarmGIProbesOnInitialize)
            {
                Prewarm();
            }
        }

        private void Dispose()
        {
            giGrid?.Dispose();
            DisposeProbes();
            DisposeCascades();

            if (Instance == this)
            {
                Instance = null;
            }

            isInitialized = false;
        }

        private void InitializeProbes()
        {
            probeSpacing = PROBE_RESOLUTION_FACTOR / probeResolution;
            probeSpacingCellSize = probeSpacing / PROBE_RESOLUTION_FACTOR;
            int probeSpacingInCells = Mathf.CeilToInt(probeSpacing / probeSpacingCellSize);

            List<int3> offsets = new List<int3>();
            for (int z = -probeSpacingInCells; z <= probeSpacingInCells; z++)
            {
                for (int y = -probeSpacingInCells; y <= probeSpacingInCells; y++)
                {
                    for (int x = -probeSpacingInCells; x <= probeSpacingInCells; x++)
                    {
                        float3 offset = new float3(x, y, z) * probeSpacingCellSize;
                        if (math.lengthsq(offset) <= probeSpacing * probeSpacing)
                        {
                            offsets.Add(new int3(x, y, z));
                        }
                    }
                }
            }
            distanceCheckOffsets = offsets.ToNativeArray(Allocator.Persistent);

            gIProbes = new GIProbe[maxProbes];
            gIProbeLightsToCalculate = new NativeArray<LightToCalculate>(maxProbes, Allocator.Persistent);
            probePositions = new NativeArray<float3>(maxProbes, Allocator.Persistent);
            probeSet = new NativeHashSet<int3>(maxProbes, Allocator.Persistent);
            float time = Time.time;
            for (int i = 0; i < maxProbes; i++)
            {
                gIProbes[i] = new GIProbe(time, i);
                gIProbeLightsToCalculate[i] = new LightToCalculate(gIProbes[i], false);
                probePositions[i] = float.MaxValue;
            }
            areProbesInitialized = true;
        }

        private void DisposeProbes()
        {
            gIProbes = null;
            if (gIProbeLightsToCalculate.IsCreated)
            {
                gIProbeLightsToCalculate.Dispose();
            }
            if (probePositions.IsCreated)
            {
                probePositions.Dispose();
            }
            if (probeSet.IsCreated)
            {
                probeSet.Dispose();
            }
            if (distanceCheckOffsets.IsCreated)
            {
                distanceCheckOffsets.Dispose();
            }

            areProbesInitialized = false;
        }

        private void InitializeCascades()
        {
            if (cascadeDistances.Length != cascadeUpdateIntervals.Length)
            {
                Debug.LogError("Cascade distances and update intervals must have the same length!");
                return;
            }

            cascadeDistancesSq = new NativeArray<float>(cascadeDistances.Length, Allocator.Persistent);
            for (int i = 0; i < cascadeDistances.Length; i++)
            {
                cascadeDistancesSq[i] = cascadeDistances[i] * cascadeDistances[i]; // Store squared distances
            }

            probesToUpdate = new int[maxProbes];
            probeCascadeAssignments = new NativeArray<int>(maxProbes, Allocator.Persistent);

            // Initialize cascade management arrays
            cascadeAccumulators = new float[cascadeDistances.Length];
            cascadeCursors = new int[cascadeDistances.Length];
            itemsInCascadeCount = new NativeArray<int>(cascadeDistances.Length, Allocator.Persistent);
            areCascadesInitialized = true;
        }

        private void DisposeCascades()
        {
            if (cascadeDistancesSq.IsCreated)
            {
                cascadeDistancesSq.Dispose();
            }
            if (probeCascadeAssignments.IsCreated)
            {
                probeCascadeAssignments.Dispose();
            }
            if (itemsInCascadeCount.IsCreated)
            {
                itemsInCascadeCount.Dispose();
            }
            areCascadesInitialized = false;
        }

        private void InitializeHdrpVolumes()
        {
#if ADAPTIVE_GI_HDRP
            if (!gameObject.TryGetComponent(out UnityEngine.Rendering.HighDefinition.CustomPassVolume passVolume))
            {
                passVolume = gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.CustomPassVolume>();
            }
            passVolume.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            passVolume.injectionPoint = UnityEngine.Rendering.HighDefinition.CustomPassInjectionPoint.AfterOpaqueAndSky;
            Rendering.HDRP.AdaptiveGIPass adaptiveGIPass = null;
            if (passVolume.customPasses != null)
            {
                foreach (UnityEngine.Rendering.HighDefinition.CustomPass pass in passVolume.customPasses)
                {
                    if (pass is Rendering.HDRP.AdaptiveGIPass foundPass)
                    {
                        adaptiveGIPass = foundPass;
                    }
                }
            }
            if (adaptiveGIPass == null)
            {
                passVolume.AddPassOfType<Rendering.HDRP.AdaptiveGIPass>();
            }

            if (!gameObject.TryGetComponent(out UnityEngine.Rendering.Volume postProcessVolume))
            {
                postProcessVolume = gameObject.AddComponent<UnityEngine.Rendering.Volume>();
            }
            postProcessVolume.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            postProcessVolume.priority = float.MaxValue;
            UnityEngine.Rendering.HighDefinition.IndirectLightingController indirectLightingController = null;
            if (postProcessVolume.profile.components != null)
            {
                foreach (UnityEngine.Rendering.VolumeComponent component in postProcessVolume.profile.components)
                {
                    if (component is UnityEngine.Rendering.HighDefinition.IndirectLightingController foundController)
                    {
                        indirectLightingController = foundController;
                    }
                }
            }
            if (indirectLightingController == null)
            {
                indirectLightingController = postProcessVolume.profile.Add<UnityEngine.Rendering.HighDefinition.IndirectLightingController>();
            }
            indirectLightingController.indirectDiffuseLightingMultiplier.overrideState = true;
            indirectLightingController.indirectDiffuseLightingMultiplier.value = 0;  
#endif
        }

        private void ValidateParameters()
        {
            ambientIntensity = Mathf.Max(0, ambientIntensity);

            fallbackIntensity = Mathf.Clamp01(fallbackIntensity);
            fallbackDirectionalLightIntensity = Mathf.Clamp01(fallbackDirectionalLightIntensity);

            gIIntensity = Mathf.Max(0, gIIntensity);
            gIColorBounceIntensity = Mathf.Clamp01(gIColorBounceIntensity);
            gINormalPower = Mathf.Max(0, gINormalPower);
            gIPower = Mathf.Clamp01(gIPower);
            gIRange = Mathf.Max(0.01f, gIRange);
            gIDeadzone = Mathf.Clamp(gIDeadzone, 0.01f, 1);
            gIShadowStrength = Mathf.Clamp01(gIShadowStrength);
            gIShadowSoftness = Mathf.Clamp01(gIShadowSoftness);
            gIShadowDepthBias = Mathf.Max(0, gIShadowDepthBias);
            gIShadowStartBias = Mathf.Max(0, gIShadowStartBias);

            shadowGIIntensityMultiplier = Mathf.Max(0, shadowGIIntensityMultiplier);
            shadowChunksPerFrame = Mathf.Max(1, shadowChunksPerFrame);
            shadowVoxelizationRadius = Mathf.Max(0, shadowVoxelizationRadius);

            renderVolume = Vector3Int.Min(renderVolume, new Vector3Int(1000, 1000, 1000));
            fadeStartDistance = Mathf.Clamp01(fadeStartDistance);

            gIProbeUpdateIntervalMultiplier = Mathf.Max(0.01f, gIProbeUpdateIntervalMultiplier);
            gILightingUpdatesPerSecond = Mathf.Max(1, gILightingUpdatesPerSecond);
            probeResolution = Mathf.Max(0.01f, probeResolution);
            if (lastProbeResolution != probeResolution)
            {
                DisposeProbes();
                InitializeProbes();
                lastProbeResolution = probeResolution;
            }

            float lastDistance = Mathf.Max(0, cascadeDistances[0]);
            for (int i = 0; i < cascadeDistances.Length; i++)
            {
                if (i != 0 && lastDistance > cascadeDistances[i])
                {
                    cascadeDistances[i] = lastDistance;
                }
                cascadeDistances[i] = Mathf.Max(0, cascadeDistances[i]);

                lastDistance = cascadeDistances[i];
            }

            for (int i = 0; i < cascadeUpdateIntervals.Length; i++)
            {
                cascadeUpdateIntervals[i] = Mathf.Max(0.01f, cascadeUpdateIntervals[i]);
            }
        }

        private void UpdateCamera()
        {
            if (overrideCameraTransform != null)
            {
                currentCameraTransform = overrideCameraTransform;
            }
            else if (Camera.main != null)
            {
                currentCameraTransform = Camera.main.transform;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.camera != null)
            {
                currentCameraTransform = UnityEditor.SceneView.lastActiveSceneView.camera.transform;
            }
#endif
            if (currentCameraTransform == null)
            {
                Camera firstSceneCam = FindFirstObjectByType<Camera>();
                if (firstSceneCam != null)
                {
                    currentCameraTransform = firstSceneCam.transform;
                }
            }

            if (currentCameraTransform != null)
            {
                currentCameraPosition = currentCameraTransform.position;
            }
        }

        private void FollowTransform()
        {
            Transform followTransform = followCamera ? currentCameraTransform : transform;

            if (followTransform != null)
            {
                float snapInterval = 1 / lightingResolution;
                gridPosition = new Vector3(
                    Mathf.Round(followTransform.position.x / snapInterval) * snapInterval,
                    Mathf.Round(followTransform.position.y / snapInterval) * snapInterval,
                    Mathf.Round(followTransform.position.z / snapInterval) * snapInterval
                );
            }
        }

        private void UpdateAmbientProbe()
        {
            ambientProbe.Clear();

            ambientProbe.AddTriLight(ambientSkyColor, ambientEquatorColor, ambientGroundColor, ambientIntensity * gIIntensity);
            if (ambientLights != null)
            {
                for (int i = 0; i < ambientLights.Length; i++)
                {
                    ambientProbe.AddDirectionalLight(ambientLights[i].direction, ambientLights[i].color, ambientLights[i].intensity * ambientIntensity * gIIntensity);
                }
            }

            if (directionalLightEnabled)
            {
                fallbackAdditiveColor = directionalLightIntensity * fallbackDirectionalLightIntensity * directionalLightColor;
            }
            else
            {
                fallbackAdditiveColor = 0;
            }
        }

        private void UpdateProbes()
        {
            if (!areProbesInitialized || !areCascadesInitialized)
            {
                return;
            }

            probeRange = gIRange + (probeSpacing * RANGE_FACTOR);

            UpdateCascadeAssignments(ref currentCameraPosition, ref probePositions, ref itemsInCascadeCount, ref cascadeDistancesSq, ref probeCascadeAssignments);

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < gIProbes.Length; i++)
            {
                GIProbe probe = gIProbes[i];
                probe.Lerp(deltaTime);
            }

            UpdateLightData();

            int fastLightGICount = 0;
            int raytracingLightGICount = 0;
            for (int i = 0; i < AdaptiveLightGI.Instances.Count; i++)
            {
                AdaptiveLightGI lightGI = AdaptiveLightGI.Instances[i];
                lightGI.UpdateStatus();
                if (!lightGI.lightIsActiveAndEnabled)
                {
                    continue;
                }

                if (lightGI.useRaytracing)
                {
                    raytracingLightGICount++;
                }
                else
                {
                    fastLightGICount++;
                }
            }

            NativeArray<LightGIToCalculate> fastLightGIData = new(fastLightGICount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            if (raytracingLightGIData.Length < raytracingLightGICount)
            {
                System.Array.Resize(ref raytracingLightGIData, raytracingLightGICount);
            }

            int fastLightIndex = 0;
            int raytracingLightIndex = 0;
            for (int i = 0; i < AdaptiveLightGI.Instances.Count; i++)
            {
                AdaptiveLightGI lightGI = AdaptiveLightGI.Instances[i];
                if (!lightGI.lightIsActiveAndEnabled)
                {
                    continue;
                }
                Color adjustedColor = usingLinearColorSpace ? lightGI.Color.linear : lightGI.Color.gamma;
                if (lightGI.UseColorTemperature)
                {
                    adjustedColor *= Mathf.CorrelatedColorTemperatureToRGB(lightGI.ColorTemperature);
                }

                LightGIToCalculate lightGIToCalculate = new()
                {
                    intensity = lightGI.Intensity,
                    bounceIntensity = lightGI.BounceIntensity,
                    color = new float3(adjustedColor.r, adjustedColor.g, adjustedColor.b),
                    isSpotLight = lightGI.IsSpotLight,
                    innerOuterSpotAngle = lightGI.InnerOuterSpotAngle,
                    position = lightGI.transform.position,
                    direction = lightGI.transform.forward
                };

                if (AdaptiveLightGI.Instances[i].useRaytracing)
                {
                    raytracingLightGIData[raytracingLightIndex] = lightGIToCalculate;
                    raytracingLightIndex++;
                }
                else
                {
                    fastLightGIData[fastLightIndex] = lightGIToCalculate;
                    fastLightIndex++;
                }
            }

            bool maxProbesReached = false;
            probesToUpdateCount = 0;
            // Process each cascade to find probes that need updating
            for (int cascadeIdx = 0; cascadeIdx < cascadeDistances.Length; cascadeIdx++)
            {
                int currentCascadeItemCount = itemsInCascadeCount[cascadeIdx];
                if (currentCascadeItemCount == 0)
                {
                    cascadeAccumulators[cascadeIdx] = 0f; // Reset accumulator if cascade empty
                    cascadeCursors[cascadeIdx] = 0; // Reset cursor
                    continue;
                }

                cascadeAccumulators[cascadeIdx] += deltaTime;
                float updateThreshold = Mathf.Max(0.0001f, cascadeUpdateIntervals[cascadeIdx] * gIProbeUpdateIntervalMultiplier / currentCascadeItemCount);

                // While we have enough accumulated time, collect probes in this cascade
                while (cascadeAccumulators[cascadeIdx] >= updateThreshold)
                {
                    int startProbeIndex = cascadeCursors[cascadeIdx];
                    bool foundProbe = false;

                    // Find the next probe in this cascade
                    for (int k = 0; k < maxProbes; k++)
                    {
                        int probeIndex = (startProbeIndex + k) % maxProbes;
                        if (probeCascadeAssignments[probeIndex] == cascadeIdx && gIProbes[probeIndex].isActive && !gIProbes[probeIndex].isDespawning)
                        {
                            // Found a probe in this cascade - add it to the update list
                            probesToUpdate[probesToUpdateCount] = probeIndex;
                            probesToUpdateCount++;

                            // Mark where to start the next search
                            cascadeCursors[cascadeIdx] = (probeIndex + 1) % maxProbes;
                            foundProbe = true;
                            break;
                        }
                    }

                    if (probesToUpdateCount == maxProbes || probesToUpdateCount == MAX_PROBES_UPDATED_PER_FRAME)
                    {
                        maxProbesReached = true;
                        break;
                    }

                    // If no probe was found, we might have inconsistent state - reset
                    if (!foundProbe)
                    {
                        cascadeAccumulators[cascadeIdx] = 0f;
                        break;
                    }

                    cascadeAccumulators[cascadeIdx] -= updateThreshold;
                }

                if (maxProbesReached)
                {
                    break;
                }
            }

            if (probesToUpdateCount > 0)
            {
                BatchUpdateProbes(fastLightGIData, raytracingLightGIData, raytracingLightGICount, false);
            }

            fastLightGIData.Dispose();
        }

        private void BatchUpdateProbes(NativeArray<LightGIToCalculate> fastLightGIData, LightGIToCalculate[] raytracingLightGIData, int raytracingLightGICount, bool prewarm)
        {
            float time = Time.time;

            if (!prewarm)
            {
                // Check probe proximity for all probes
                NativeArray<OverlapSphereCommand> proximityCommands = new(probesToUpdateCount * 2, Allocator.TempJob);
                NativeArray<ColliderHit> proximityResults = new(probesToUpdateCount * 2, Allocator.TempJob);

                // Setup all proximity checks
                for (int i = 0; i < probesToUpdateCount; i++)
                {
                    int commandIndex = i * 2;
                    GIProbe probe = gIProbes[probesToUpdate[i]];

                    probe.FastGI(fastLightGIData);

                    proximityCommands[commandIndex] = new OverlapSphereCommand(probe.position, 0.5f * probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR, new QueryParameters(layerMask: gIMask));
                    proximityCommands[commandIndex + 1] = new OverlapSphereCommand(probe.position, 2 * probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR, new QueryParameters(layerMask: gIMask));
                }

                // Run all proximity checks in one batch
                OverlapSphereCommand.ScheduleBatch(proximityCommands, proximityResults, proximityCommands.Length / perJobComputeThreads, 1).Complete();

                // Process proximity results and mark inactive probes
                for (int i = 0; i < probesToUpdateCount; i++)
                {
                    int resultIndex = i * 2;
                    GIProbe probe = gIProbes[probesToUpdate[i]];

                    if (proximityResults[resultIndex].instanceID != 0 ||
                        proximityResults[resultIndex + 1].instanceID == 0)
                    {
                        probe.targetColor = 0;
                        probe.targetIntensity = 0;
                        probe.isDespawning = true;
                        float3 oldPosition = probe.position;
                        WorldToCell(ref oldPosition, ref probeSpacingCellSize, out int3 oldCell);
                        _ = probeSet.Remove(oldCell);
                    }
                }

                proximityCommands.Dispose();
                proximityResults.Dispose();
            }

            // Create collections for all ray commands
            int totalRayCount = probesToUpdateCount * (int)gIRayCount;
            NativeArray<RaycastCommand> occlusionCommands = new(totalRayCount, Allocator.TempJob);
            NativeArray<RaycastHit> occlusionResults = new(totalRayCount, Allocator.TempJob);

            // Setup occlusion commands for all active probes
            int occlusionIndex = 0;
            for (int i = 0; i < probesToUpdateCount; i++)
            {
                GIProbe probe = gIProbes[probesToUpdate[i]];
                probe.ScheduleOcclusionCommands(occlusionCommands, occlusionIndex);
                occlusionIndex += (int)gIRayCount;
            }
            RaycastCommand.ScheduleBatch(occlusionCommands, occlusionResults, occlusionCommands.Length / perJobComputeThreads).Complete();

            // Process occlusion results and prepare bounce commands for all probes
            if (probeRayOffsets.Length < probesToUpdateCount)
            {
                System.Array.Resize(ref probeRayOffsets, probesToUpdateCount * 2);
            }
            if (bounceCountsPerProbe.Length < probesToUpdateCount)
            {
                System.Array.Resize(ref bounceCountsPerProbe, probesToUpdateCount * 2);
            }

            occlusionIndex = 0;
            int totalBounceCount = 0;
            for (int i = 0; i < probesToUpdateCount; i++)
            {
                int bounceCount = gIProbes[probesToUpdate[i]].FinishOcclusionResults(occlusionResults, occlusionIndex, raytracingLightGICount);
                probeRayOffsets[i] = occlusionIndex;
                bounceCountsPerProbe[i] = bounceCount;
                totalBounceCount += bounceCount;
                occlusionIndex += (int)gIRayCount;
            }

            // Create arrays for bounce raycasts
            NativeArray<RaycastCommand> bounceCommands = new(totalBounceCount, Allocator.TempJob);
            NativeArray<RaycastHit> bounceResults = new(totalBounceCount, Allocator.TempJob);
            if (bounceLightIndexes.Length < totalBounceCount)
            {
                System.Array.Resize(ref bounceLightIndexes, totalBounceCount * 2);
            }
            if (bounceColors.Length < totalBounceCount)
            {
                System.Array.Resize(ref bounceColors, totalBounceCount * 2);
            }

            // Setup bounce commands for all active probes
            int bounceIndex = 0;
            for (int i = 0; i < probesToUpdateCount; i++)
            {
                int bouncesScheduled = gIProbes[probesToUpdate[i]].ScheduleBounceCommands(
                        occlusionResults,
                        bounceCommands,
                        bounceLightIndexes,
                        bounceColors,
                        probeRayOffsets[i],
                        bounceIndex,
                        raytracingLightGIData,
                        raytracingLightGICount
                );

                bounceIndex += bouncesScheduled;
            }
            RaycastCommand.ScheduleBatch(bounceCommands, bounceResults, bounceCommands.Length / perJobComputeThreads).Complete();

            // Process bounce results for all probes
            bounceIndex = 0;
            for (int i = 0; i < probesToUpdateCount; i++)
            {
                GIProbe probe = gIProbes[probesToUpdate[i]];
                probe.FinishBounceResults(
                        bounceCommands,
                        bounceResults,
                        bounceLightIndexes,
                        bounceColors,
                        bounceIndex,
                        raytracingLightGIData
                );
                probe.Apply(time);

                bounceIndex += bounceCountsPerProbe[i];
            }

            // Clean up native arrays
            occlusionCommands.Dispose();
            occlusionResults.Dispose();
            bounceCommands.Dispose();
            bounceResults.Dispose();
        }

        private void UpdateLightData()
        {
            // Extract the common light data update code from UpdateProbes
            directionalLight = RenderSettings.sun;
            if (directionalLight != null)
            {
                dirLightBack = -directionalLight.transform.forward;
                directionalLightEnabled = directionalLight != null && directionalLight.isActiveAndEnabled;
                if (directionalLightEnabled)
                {
                    Color adjustedColor = usingLinearColorSpace ? directionalLight.color.linear : directionalLight.color.gamma;
                    if (directionalLight.useColorTemperature)
                    {
                        adjustedColor *= Mathf.CorrelatedColorTemperatureToRGB(directionalLight.colorTemperature);
                    }
                    directionalLightColor = new float3(adjustedColor.r, adjustedColor.g, adjustedColor.b);
                    directionalLightIntensity = directionalLight.intensity * directionalLight.bounceIntensity;
                }
            }
            else
            {
                directionalLightEnabled = false;
            }

            rayDirs = RayDirections.GetRayDirections(gIRayCount);
        }

        private void FindProbeLocations(int rayCount)
        {
            if (!areProbesInitialized || currentCameraTransform == null)
            {
                return;
            }

            NativeArray<RaycastCommand> primaryCommands = new(rayCount, Allocator.TempJob);
            NativeArray<RaycastHit> primaryResults = new(rayCount, Allocator.TempJob);

            // Prepare primary rays
            for (int r = 0; r < rayCount; r++)
            {
                primaryCommands[r] = new RaycastCommand(currentCameraPosition, RayDirections._10000[placementRayIndex], new QueryParameters(gIMask));
                placementRayIndex++;
                if (placementRayIndex == 10000)
                {
                    placementRayIndex = 0;
                }
            }

            // Execute primary raycasts
            RaycastCommand.ScheduleBatch(primaryCommands, primaryResults, rayCount / perJobComputeThreads).Complete();

            for (int r = 0; r < rayCount; r++)
            {
                RaycastHit hit = primaryResults[r];
                int colliderId;
#if UNITY_6000_3_OR_NEWER
                colliderId = hit.colliderEntityId;
#else
                colliderId = hit.colliderInstanceID;
#endif
                if (colliderId != 0) // If we hit something
                {
                    TrySpawnProbe(hit);
                }
            }

            primaryCommands.Dispose();
            primaryResults.Dispose();
        }

        private bool IsPositionValid(float3 position, Quaternion rotation)
        {
            int3 cell = int3.zero;
            WorldToCell(ref position, ref probeSpacingCellSize, out cell);
            CheckProbeProximity(ref cell, ref distanceCheckOffsets, ref probeSet, out bool isNear);
            return !isNear && !Physics.CheckBox(position, new Vector3(probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR, probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR, probeSpacing * CONTACT_OFFSET / NEARBY_OBJECT_FACTOR), rotation, gIMask);
        }

        internal void TrySpawnProbe(RaycastHit hit)
        {
            float3 position = hit.point;
            float3 normal = hit.normal;
            position += probeSpacing * CONTACT_OFFSET * NEARBY_OBJECT_FACTOR * normal;
            Quaternion rotation = Quaternion.LookRotation(normal);
            if (IsPositionValid(position, rotation) && (gIProbeMask == ~0 || (gIProbeMask.value & (1 << hit.collider.gameObject.layer)) != 0))
            {
                FindFarthestProbe(ref currentCameraPosition, ref probePositions, out int farthestIndex);
                GIProbe probe = gIProbes[farthestIndex];
                probePositions[probe.probeIndex] = position;
                if (probe.isActive)
                {
                    float3 oldPosition = probe.position;
                    WorldToCell(ref oldPosition, ref probeSpacingCellSize, out int3 oldCell);
                    _ = probeSet.Remove(oldCell);
                }
                float3 newPosition = position;
                WorldToCell(ref newPosition, ref probeSpacingCellSize, out int3 newCell);
                _ = probeSet.Add(newCell);
                probe.intensity = 0;
                probe.color = 0;
                probe.targetIntensity = 0;
                probe.targetColor = 0;
                probe.position = position;
                probe.direction = normal;
                probe.isActive = true;
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void WorldToCell(ref float3 worldPos, ref float probeSpacingCellSize, out int3 result)
        {
            result = (int3)math.floor(worldPos / probeSpacingCellSize);
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void CheckProbeProximity(ref int3 cell, ref NativeArray<int3> distanceCheckOffsets, ref NativeHashSet<int3> probeSet, out bool isNear)
        {
            for (int i = 0; i < distanceCheckOffsets.Length; i++)
            {
                int3 neighbor = cell + distanceCheckOffsets[i];
                if (probeSet.Contains(neighbor))
                {
                    isNear = true;
                    return;
                }
            }

            isNear = false;
            return;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void FindFarthestProbe(ref float3 camPos, ref NativeArray<float3> probePositions, out int result)
        {
            int farthestIndex = 0;
            float maxDistSq = 0;

            for (int i = 0; i < probePositions.Length; i++)
            {
                float distSq = math.distancesq(camPos, probePositions[i]);
                if (distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    farthestIndex = i;
                }
            }

            result = farthestIndex;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void UpdateCascadeAssignments(ref float3 currentCameraPosition, ref NativeArray<float3> probePositions, ref NativeArray<int> itemsInCascadeCount, ref NativeArray<float> cascadeDistancesSq, ref NativeArray<int> probeCascadeAssignments)
        {
            for (int i = 0; i < itemsInCascadeCount.Length; i++)
            {
                itemsInCascadeCount[i] = 0;
            }

            // Update probe cascade assignments
            for (int i = 0; i < probePositions.Length; i++)
            {
                float distSq = math.distancesq(currentCameraPosition, probePositions[i]);
                int assignedCascade = -1; // Default: not in any active cascade (too far)

                for (int c = 0; c < cascadeDistancesSq.Length; c++)
                {
                    if (distSq <= cascadeDistancesSq[c])
                    {
                        assignedCascade = c;
                        itemsInCascadeCount[c]++;
                        break; // Found the closest cascade
                    }
                }

                probeCascadeAssignments[i] = assignedCascade;
            }
        }
    }
}
