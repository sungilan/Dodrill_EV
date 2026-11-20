using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// ============================================================
//  EmergencyEventManager.cs  (사고 연출)
//
//  TriggerAccident(type, reason, worldPos) — 사고가 난 자리(만지던 부품·볼트 위치)에서 연출이 나온다.
//   · 예전에는 Game 씬에 놓인 이펙트 오브젝트(sparkEffect = PlasmaExplosionEffect)를 그냥 켜서, 사고 지점과 상관없이 씬 한쪽에서 터졌다.
//     이제 그 오브젝트는 '견본'으로만 두고 복제해 사고 지점에 크기를 줄여 세운다(견본은 계속 꺼 둔다).
//   · 감전 연출: ① 아크 번쩍(점광원) ② 불꽃 튀김(스파크 폭발, 여러 번) ③ 플라스마 연기 ④ 카메라 흔들림(렌더 직전에만 — 컨트롤러와 안 부딪침, VR 은 생략)
//                ⑤ 화면 하얀 번쩍 → 빨간 번쩍 ⑥ 경보음 → 실패 팝업
//   · 위치를 안 주면(옛 호출) 지금 보는 화면 앞 1m 에서 나온다.
//   · 열폭주도 연기·불 오브젝트를 사고 지점으로 옮겨 켠다(씬에 연결돼 있을 때).
// ============================================================
public class EmergencyEventManager : MonoBehaviour
{
    public static EmergencyEventManager Instance;

    public enum AccidentType { ThermalRunaway, ElectricShock }

    [Header("VFX Settings")]
    public GameObject smokeEffect;
    public GameObject fireEffect;
    [Tooltip("감전 연출의 견본(PlasmaExplosionEffect) — 켜지 않고 복제해 사고 지점에 세운다")]
    public GameObject sparkEffect;

    [Header("사고 지점 연출")]
    [Tooltip("견본 이펙트를 줄이는 비율(볼트 크기에 맞춰)")] public float sparkScale = 0.22f;
    [Tooltip("복제한 이펙트가 남아 있는 시간(초)")] public float sparkLifetime = 6f;
    [Tooltip("카메라 흔들림 세기(m)·시간(초)")] public float shakeAmplitude = 0.035f;
    public float shakeSeconds = 0.8f;

    [Header("Sound Names (Resources/Sounds/)")]
    public string alarmSoundName = "Siren_Loop";
    public string explosionSoundName = "Explosion_Large";
    public string electricShockSoundName = "Electric_Shock_Spark";
    public string failureBgmName = "Sad_Theme"; // 실패 시 흐를 배경음

    [Header("UI & Screen Effects")]
    public GameObject deathCanvas;
    public TMPro.TextMeshProUGUI failureReasonText;
    public Animation cameraShake;

    [Header("Flash & Fade Settings")]
    public CanvasGroup flashCanvasGroup;
    public float flashSpeed = 5f;
    public float fadeDuration = 2.0f;

    private bool isEventTriggered = false;

    /// <summary>훈련 종료(사고) 창이 떠 있다 — 다른 HUD(감전 게이지·방전 창)는 이때 숨는다</summary>
    public bool PopupUp => deathCanvas != null && deathCanvas.activeInHierarchy;

    /// <summary>이 기기에서 사고가 났다 (사고 팝업이 이 사람의 종료 팝업 역할을 한다)</summary>
    public bool HasAccident => isEventTriggered;

    /// <summary>사고 연출(번쩍임·폭발)이 끝났다 — 평가 결과 화면은 이때부터 띄운다</summary>
    public bool EffectsFinished { get; private set; }

    /// <summary>마지막 사고 지점(월드)</summary>
    public Vector3 AccidentPoint { get; private set; }

    void Awake() => Instance = this;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        RestoreCamera();
    }

    /// <param name="worldPos">사고가 난 월드 좌표(만지던 부품·볼트). 없으면 지금 보는 화면 앞 1m.</param>
    public void TriggerAccident(AccidentType type, string reason, Vector3? worldPos = null)
    {
        if (isEventTriggered)
        {
            // 사고는 한 번만 나는 것이 원칙(훈련 종료)이지만, 자유 연습에서는 팝업을 닫고 계속할 수 있으므로 연출이 끝나고 잠시 뒤 다시 날 수 있게 한다
            // (전에는 이 깃발이 영원히 켜져 있어 두 번째 사고는 아무 연출 없이 무시됐다)
            bool retry = SafetyHooks.FreePractice && EffectsFinished && Time.unscaledTime >= _finishedAt + 3f;
            if (!retry) return;
            ReArm();
        }
        isEventTriggered = true;

        Vector3 pos = worldPos ?? FallbackPoint();
        AccidentPoint = pos;

        // 1. 기존 모든 사운드(BGM 포함) 즉시 정지
        Managers.Sound.StopAll();

        if (type == AccidentType.ThermalRunaway)
            StartCoroutine(ThermalRunawayRoutine(reason, pos));
        else
            StartCoroutine(ElectricShockRoutine(reason, pos));
    }

    private float _finishedAt;

    // 다시 사고가 날 수 있게 상태를 되돌린다(자유 연습) — 열려 있던 사고 팝업·번쩍임을 치운다
    private void ReArm()
    {
        isEventTriggered = false;
        EffectsFinished = false;
        StopAllCoroutines();
        if (deathCanvas != null) deathCanvas.SetActive(false);
        if (flashCanvasGroup != null) flashCanvasGroup.alpha = 0f;
        RestoreCamera();
    }

    private static Vector3 FallbackPoint()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.position + cam.transform.forward * 1.0f : Vector3.zero;
    }

    // ⚡ 감전 사고 루틴
    private IEnumerator ElectricShockRoutine(string reason, Vector3 pos)
    {
        Debug.LogError($"[감전 사고] {reason}  at {pos}");

        // 사고 지점에서 터진다 — 아크 번쩍·불꽃·플라스마 연기
        SpawnShockFx(pos);
        Managers.Sound.Play(electricShockSoundName, Define.Sound.Effect);
        StartShake(shakeSeconds, shakeAmplitude);
        if (cameraShake != null) cameraShake.Play();

        // 하얀 섬광(아크) → 빨간 경고 번쩍
        yield return StartCoroutine(FlashScreenRoutine(Color.white, 1));
        yield return StartCoroutine(FlashScreenRoutine(Color.red, 2));
        Managers.Sound.Play(alarmSoundName, Define.Sound.Effect);
        yield return new WaitForSeconds(1.0f);
        ShowFailureUI("감전 사고 발생!" + reason);
    }

    // 🔥 열폭주(화재) 루틴
    private IEnumerator ThermalRunawayRoutine(string reason, Vector3 pos)
    {
        // 씬에 연기·불 오브젝트가 연결돼 있으면 그것을, 없으면 코드로 만든 연출(흰 연기 → 폭발 → 제트 불꽃·검은 연기·열 왜곡)을 사고 지점에 세운다
        bool sceneFx = smokeEffect != null || fireEffect != null;
        if (sceneFx) PlaceAt(smokeEffect, pos);
        else AccidentFx.Fire(pos, 40f, 2.5f);

        // Managers.Sound 활용: 경고 사이렌 재생 (BGM 타입으로 재생하면 자동 루프 가능)
        Managers.Sound.Play(alarmSoundName, Define.Sound.Effect);

        SafetyUIHandler.Instance?.TriggerWarning("경고: 배터리 내압 상승! 대피하십시오!");

        StartCoroutine(FlashScreenRoutine(new Color(1f, 0.5f, 0f), 1));

        yield return new WaitForSeconds(2.5f);

        // Managers.Sound 활용: 폭발음 재생
        Managers.Sound.Play(explosionSoundName, Define.Sound.Effect);

        PlaceAt(fireEffect, pos);
        StartShake(1.2f, shakeAmplitude * 1.5f);
        if (cameraShake != null) cameraShake.Play();

        yield return StartCoroutine(FlashScreenRoutine(Color.red, 5));

        yield return new WaitForSeconds(1.0f);
        ShowFailureUI("배터리 화재 발생!\n" + reason);
    }

    private static void PlaceAt(GameObject fx, Vector3 pos)
    {
        if (fx == null) return;
        fx.transform.position = pos;
        fx.SetActive(true);
    }

    // ══════════════════════════════════════════
    //  사고 지점 연출
    // ══════════════════════════════════════════

    private void SpawnShockFx(Vector3 pos)
    {
        // 사고 지점이 눈앞의 부품 안쪽이면 안 보이므로, 보는 사람 쪽으로 조금 당긴다
        var cam = Camera.main;
        if (cam != null) pos += (cam.transform.position - pos).normalized * 0.12f;

        Material borrowed = null;
        if (sparkEffect != null)
        {
            // 씬의 견본은 꺼 둔 채 복제본을 사고 지점에 세운다(예전에는 견본을 켜서 씬 한쪽에서 터졌다)
            if (sparkEffect.activeSelf) sparkEffect.SetActive(false);
            var go = Instantiate(sparkEffect, pos, Quaternion.identity);
            go.transform.localScale = Vector3.one * sparkScale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;   // 견본의 자식 이펙트는 Local 이라 부모 크기를 안 따라간다
            }
            var pr = go.GetComponentInChildren<ParticleSystemRenderer>(true);
            if (pr != null) borrowed = pr.sharedMaterial;
            go.SetActive(true);
            StartCoroutine(StopThenDestroy(go, 1.4f, sparkLifetime));
        }

        var sparks = MakeSparkBurst(pos, borrowed);
        StartCoroutine(ArcRoutine(pos, sparks));
    }

    // 견본은 loop 라 계속 뿜는다 → 잠시 뒤 새 입자만 멈추고 남은 입자가 사라진 뒤 지운다
    private static IEnumerator StopThenDestroy(GameObject go, float stopAfter, float destroyAfter)
    {
        yield return new WaitForSeconds(stopAfter);
        if (go == null) yield break;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        yield return new WaitForSeconds(Mathf.Max(0.5f, destroyAfter - stopAfter));
        if (go != null) Destroy(go);
    }

    // 짧은 불꽃(스파크) 폭발 — 선으로 늘어나는 밝은 입자가 중력으로 떨어진다
    private ParticleSystem MakeSparkBurst(Vector3 pos, Material borrowed)
    {
        var go = new GameObject("[AccidentSparks]");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.55f), new Color(0.55f, 0.8f, 1f));
        main.gravityModifier = 0.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.03f;

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2.5f; r.velocityScale = 0.08f;
        Material mat = borrowed;
        if (mat == null) { var sd = Shader.Find("Sprites/Default"); if (sd != null) mat = new Material(sd); }
        if (mat != null) r.sharedMaterial = mat;
        ps.Play();
        Destroy(go, 4f);
        return ps;
    }

    // 아크 번쩍(점광원) + 잇따른 불꽃 — 처음 강하게 번쩍이고 몇 번 깜빡이다 사그라든다
    private IEnumerator ArcRoutine(Vector3 pos, ParticleSystem sparks)
    {
        var lgo = new GameObject("[AccidentArcLight]");
        lgo.transform.position = pos;
        var light = lgo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.7f, 0.85f, 1f); light.range = 5f; light.shadows = LightShadows.None;

        float[] peaks = { 14f, 4f, 10f, 3f, 7f, 2f, 4f };
        for (int i = 0; i < peaks.Length; i++)
        {
            light.intensity = peaks[i];
            if (i > 0 && i % 2 == 0 && sparks != null) sparks.Emit(35);   // 잇따라 튀는 불꽃
            yield return new WaitForSeconds(0.07f + 0.03f * i);
        }
        float t = 0f;
        while (t < 1.2f && light != null)
        {
            t += Time.deltaTime;
            light.intensity = Mathf.Lerp(2f, 0f, t / 1.2f) * (0.6f + 0.4f * Random.value);   // 남은 지직거림
            yield return null;
        }
        if (lgo != null) Destroy(lgo);
    }

    // ── 카메라 흔들림: 렌더 직전에만 위치를 흔들고 바로 되돌린다(컨트롤러의 위치 계산에 섞이지 않는다, VR 은 멀미 때문에 생략) ──
    private float _shakeEnd, _shakeDur = 1f, _shakeAmp;
    private Camera _shookCam;
    private Vector3 _shakeApplied;

    private void StartShake(float seconds, float amplitude)
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive) return;
        _shakeDur = Mathf.Max(0.1f, seconds); _shakeEnd = Time.unscaledTime + _shakeDur; _shakeAmp = amplitude;
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (Time.unscaledTime >= _shakeEnd || cam != Camera.main) return;
        float k = Mathf.Clamp01((_shakeEnd - Time.unscaledTime) / _shakeDur);
        _shakeApplied = Random.insideUnitSphere * _shakeAmp * k;
        _shookCam = cam;
        cam.transform.position += _shakeApplied;
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam) { if (cam == _shookCam) RestoreCamera(); }

    private void RestoreCamera()
    {
        if (_shookCam != null) _shookCam.transform.position -= _shakeApplied;
        _shookCam = null; _shakeApplied = Vector3.zero;
    }

    private void ShowFailureUI(string reason)
    {
        if (failureReasonText != null)
            failureReasonText.text = reason;

        // 실패 전용 BGM 재생 (슬픈 음악 등)
        Managers.Sound.Play(failureBgmName, Define.Sound.Bgm);

        EffectsFinished = true;
        _finishedAt = Time.unscaledTime;
        StartCoroutine(ShowDeathCanvasWhenReady());
    }

    /// <summary>
    /// 평가 모드: 서버가 치명 판정을 하면 결과 화면(EvaluationClientUI)이 먼저 뜨고, 그걸 닫은 뒤 사고 팝업(확인 → 타이틀)을 띄운다.
    /// 결과가 안 오면(서버가 안전 상태로 판정해 기록하지 않은 경우 등) 잠깐 기다렸다가 사고 팝업을 그냥 띄운다.
    /// </summary>
    private IEnumerator ShowDeathCanvasWhenReady()
    {
        if (EvaluationMode.ClientIsEvaluation)
        {
            for (float t = 0f; t < 3f && !SafetyHooks.ResultShowing; t += Time.unscaledDeltaTime) yield return null;
            while (SafetyHooks.ResultShowing) yield return null;
        }
        yield return StartCoroutine(FadeInDeathCanvas());
    }

    // ✨ 화면 번쩍임 로직 (기존과 동일)
    private IEnumerator FlashScreenRoutine(Color flashColor, int repeatCount)
    {
        if (flashCanvasGroup == null) yield break;
        Image img = flashCanvasGroup.GetComponent<Image>();
        if (img != null) img.color = flashColor;

        for (int i = 0; i < repeatCount; i++)
        {
            while (flashCanvasGroup.alpha < 0.8f)
            {
                flashCanvasGroup.alpha += Time.deltaTime * flashSpeed;
                yield return null;
            }
            while (flashCanvasGroup.alpha > 0f)
            {
                flashCanvasGroup.alpha -= Time.deltaTime * flashSpeed;
                yield return null;
            }
        }
    }

    // 💀 데스 캔버스 페이드 인 (기존과 동일)
    private IEnumerator FadeInDeathCanvas()
    {
        if (deathCanvas == null) yield break;
        deathCanvas.SetActive(true);
        // 훈련 종료 창이 다른 화면 UI(감전 게이지·선임 대화·방전 창) 위에 오도록 정렬 순서를 크게
        var dc = deathCanvas.GetComponent<Canvas>();
        if (dc == null) { dc = deathCanvas.AddComponent<Canvas>(); if (deathCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) deathCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>(); }
        dc.overrideSorting = true; dc.sortingOrder = 500;

        CanvasGroup cg = deathCanvas.GetComponent<CanvasGroup>();
        if (cg == null) cg = deathCanvas.AddComponent<CanvasGroup>();

        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, elapsedTime / fadeDuration);
            yield return null;
        }

        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}
