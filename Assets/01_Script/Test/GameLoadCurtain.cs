using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
//  GameLoadCurtain.cs — 게임 씬이 준비될 때까지 화면을 검게 덮는다 (2026-11-20, PC·모바일·VR 공통)
//
//  문제: 게임 씬이 로드되는 찰나에 씬에 구워져 있던 UI(단계 설명 팝업의 '12V 배터리'·'현재 수행할 Task 설명이 표시됩니다', 키·지도 패널 등)가
//  카메라가 켜지기 전 검은 화면 위에 그대로 보였다(오버레이 캔버스는 카메라가 없어도 그려진다). 각 UI 를 따로 숨기는 대신(GuideSystem.Start 에서
//  팝업을 숨기는 것은 Start 가 늦게 돌면 소용없다) 씬이 로드되는 즉시 검은 막을 올리고, 시나리오가 도착하고 카메라가 켜지면 서서히 걷는다.
//  · PC·모바일: 화면 오버레이 캔버스(검은 Image).
//  · VR: 오버레이 캔버스는 헤드셋에 안 보이므로, 메인 카메라 바로 앞(0.3m)에 큰 검은 쿼드를 붙인다(깊이 무시, 맨 마지막에 그림 — 월드 캔버스 UI 도 가린다).
//  · 서버(배치 모드)에서는 만들지 않는다. 10 초가 지나도 준비가 안 되면 그냥 걷는다(막이 영영 남지 않게).
// ============================================================
public class GameLoadCurtain : MonoBehaviour
{
    private const string GameScene = "Game";
    private const float MinShow = 0.5f, MaxShow = 10f, FadeTime = 0.4f;
    private const float VrDistance = 0.3f, VrSize = 6f;

    private static GameLoadCurtain s_inst;
    private CanvasGroup _group;          // PC·모바일
    private bool _vr;
    private Transform _quad;             // VR
    private Material _quadMat;
    private Camera _quadCam;
    private float _t;
    private bool _fading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        s_inst = null;
    }

    // sceneLoaded 는 씬 오브젝트의 Awake/OnEnable 뒤, Start·첫 렌더 앞에 불린다 — 첫 화면부터 막이 덮이게
    private static void OnLoaded(Scene sc, LoadSceneMode mode)
    {
        if (sc.name != GameScene || s_inst != null) return;
        if (Application.isBatchMode) return;

        var go = new GameObject("[GameLoadCurtain]");
        DontDestroyOnLoad(go);
        var inst = go.AddComponent<GameLoadCurtain>();
        inst._vr = VrDevice.IsActive;
        if (!inst._vr)
        {
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 32000;
            go.AddComponent<GraphicRaycaster>();
            var img = new GameObject("Black").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.color = Color.black;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            inst._group = go.AddComponent<CanvasGroup>();
        }
        else inst.AttachVrQuad();   // 카메라가 아직 없으면 Update 에서 붙는다
        s_inst = inst;
    }

    private void OnDestroy()
    {
        if (_quad != null) Destroy(_quad.gameObject);
        if (_quadMat != null) Destroy(_quadMat);
        if (s_inst == this) s_inst = null;
    }

    // ── VR: 카메라 앞 검은 쿼드 ──
    private void AttachVrQuad()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (_quad == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
            q.name = "[GameLoadCurtain_Quad]";
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            _quadMat = new Material(sh) { color = Color.black };
            _quadMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);   // 깊이 무시
            _quadMat.renderQueue = 5000;                                                                // 맨 마지막에 그려 월드 캔버스 UI 도 가린다
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _quadMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _quad = q.transform;
        }
        if (_quadCam == cam) return;
        _quadCam = cam;
        _quad.SetParent(cam.transform, false);
        _quad.localPosition = new Vector3(0f, 0f, VrDistance);
        _quad.localRotation = Quaternion.identity;
        _quad.localScale = new Vector3(VrSize, VrSize, 1f);
    }

    private void SetAlpha(float a)
    {
        if (_vr)
        {
            if (_quadMat != null) { var c = _quadMat.color; c.a = a; _quadMat.color = c; }
        }
        else if (_group != null) _group.alpha = a;
    }

    private bool Ready()
    {
        var rec = ScenarioStateReceiver.Instance;
        return Camera.main != null && rec != null && rec.CurrentScenario != null;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_vr && !_fading) AttachVrQuad();   // 카메라가 늦게 생기거나 바뀌어도 따라붙는다
        if (_fading) return;

        // 게임 씬을 벗어났으면(홈으로 등) 바로 치운다
        if (!SceneManager.GetSceneByName(GameScene).isLoaded) { Destroy(gameObject); return; }

        if ((_t >= MinShow && Ready()) || _t >= MaxShow)
        {
            _fading = true;
            StartCoroutine(FadeOut());
        }
    }

    private System.Collections.IEnumerator FadeOut()
    {
        float a = 1f;
        while (a > 0f)
        {
            a = Mathf.MoveTowards(a, 0f, Time.unscaledDeltaTime / FadeTime);
            SetAlpha(a);
            yield return null;
        }
        Destroy(gameObject);
    }
}
