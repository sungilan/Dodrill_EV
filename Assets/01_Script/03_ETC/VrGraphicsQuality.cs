using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// VR 헤드셋(Quest) 전용 화질 보정. 씬·공용 에셋은 건드리지 않고 실행 중에만 바꾼다(휴대폰·PC 영향 없음).
///
/// 원인: Android 기본 품질 레벨이 'Very Low' 라 Quest 도 그대로 받았다.
///   · globalTextureMipmapLimit 1 → 모든 텍스처가 절반 해상도(가까이 보면 뭉개짐)
///   · MSAA 꺼짐 → 차체·선반 가장자리 계단·반짝임(VR 에서 특히 눈에 띔)
///   · lodBias 0.3 → 가까운 거리에서도 저폴리 LOD 로 바뀜
///   · 씬에 톤매핑이 없고 실시간 직사광(1.0) + HDR 앰비언트(1.06×0.8)가 겹쳐 흰 면이 하얗게 날아감
/// 대책(비용 낮은 것만):
///   · 텍스처 원해상도, 이방성 필터 최대 4x, lodBias 1
///   · MSAA 4x — Quest 타일 GPU 는 MSAA 를 타일 안에서 처리해 비용이 작다
///   · 고정 포비에이티드 렌더링(가장자리 해상도↓)으로 MSAA 비용을 상쇄
///   · 앰비언트·직사광을 조금 낮춰 흰 면이 날아가지 않게(후처리 패스 추가 없음)
/// </summary>
public class VrGraphicsQuality : MonoBehaviour
{
    // 앰비언트(환경광)·직사광·반사 배율 — 너무 어둡거나 밝으면 여기만 조정
    public static float AmbientScale = 0.75f;
    public static float SunScale = 0.85f;
    public static float ReflectionScale = 0.8f;
    private const int Msaa = 4;
    private const float Foveation = 0.5f;   // 0 끔 ~ 1 최대

    // 26.11.16 VR 테스트: MSAA 켠 뒤 아웃라인 주변에 네모 테두리가 생기고 전체 프레임이 떨어짐 → 보정 전부 끔.
    // 다시 비교해 보려면 true 로.
    public static bool Enabled = false;

    private static VrGraphicsQuality s_inst;
    private bool _globalApplied;
    private readonly HashSet<int> _scenesApplied = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Enabled) return;
        if (Application.isBatchMode || !Application.isMobilePlatform || s_inst != null) return;
        var go = new GameObject("[VrGraphicsQuality]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<VrGraphicsQuality>();
    }

    private static bool IsHeadset => XRSettings.isDeviceActive || UserInfo.PlatformType == XRAirpotrSecurity.PlatformType.VR;

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) { _scenesApplied.Remove(s.handle); }

    private float _giveUpAt = -1f;

    private void Update()
    {
        // XR 초기화가 늦을 수 있어 잠깐 기다린다. 30초 안에 헤드셋이 아니면(휴대폰) 끝낸다
        if (!IsHeadset)
        {
            if (_giveUpAt < 0f) _giveUpAt = Time.unscaledTime + 30f;
            if (Time.unscaledTime > _giveUpAt) Destroy(gameObject);
            return;
        }
        if (!_globalApplied) { _globalApplied = true; ApplyGlobal(); }
        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && _scenesApplied.Add(scene.handle)) ApplyLighting(scene);
    }

    private static void ApplyGlobal()
    {
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        Texture.SetGlobalAnisotropicFilteringLimits(4, 4);
        QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1f);

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            urp.msaaSampleCount = Msaa;

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
            if (d != null && d.running) d.foveatedRenderingLevel = Foveation;

        Debug.Log($"[VrGraphics] 텍스처 원해상도 · 이방성 4x · lodBias {QualitySettings.lodBias:0.0} · MSAA {Msaa}x · 포비에이션 {Foveation}");
    }

    private static void ApplyLighting(Scene scene)
    {
        // 씬 앰비언트는 Skybox 모드인데 스카이박스가 비어 있어 환경광을 다시 계산(DynamicGI.UpdateEnvironment)하면 검게 될 수 있다
        // → 구워진 앰비언트 프로브 값을 그대로 낮춘다(모드와 무관)
        var probe = RenderSettings.ambientProbe;
        probe *= AmbientScale;
        RenderSettings.ambientProbe = probe;
        RenderSettings.reflectionIntensity *= ReflectionScale;
        var sun = RenderSettings.sun;
        if (sun != null && sun.type == LightType.Directional) sun.intensity *= SunScale;
        else
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != null && l.type == LightType.Directional && l.gameObject.scene == scene) l.intensity *= SunScale;
        Debug.Log($"[VrGraphics] '{scene.name}' 조명 보정 — 앰비언트 ×{AmbientScale}, 직사광 ×{SunScale}, 반사 ×{ReflectionScale}");
    }
}
