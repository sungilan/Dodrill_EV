using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// 화면 UI 가 기기 비율에 맞춰 짤리지 않게 한다 (공군 MobileRuntimeOptimizer 의 화면 비율 처리와 같은 방식).
///
/// 문제: 화면 UI 캔버스가 1920×1080 기준의 '가로 폭 맞춤(match 0)' 이라, 20:9 같은 긴 폰(또는 초와이드 모니터)에서는
///       세로가 모자라 위·아래 UI 가 화면 밖으로 잘렸다. 로비의 '뒤로 가기·새로고침·로그아웃' 버튼이 위쪽에 있어
///       훈련방이 없는 화면에서 돌아갈 수 없었던 것이 그 예.
/// 해결: ScreenMatchMode.Expand — 기준 영역(1920×1080)이 항상 화면 안에 다 들어오게 한다
///       (4:3·16:10 은 지금과 같은 배율, 긴 화면은 세로 기준으로 맞추고 남는 좌우는 여백).
///
/// 씬·프리팹은 건드리지 않고 실행 중에만 적용한다. 월드 스페이스(VR) 캔버스와 세로 기준 캔버스는 건드리지 않는다.
/// 실행 중 새로 생기는 캔버스(씬 전환·팝업)도 1초마다 다시 잡는다.
/// </summary>
public class ResponsiveCanvasScaler : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode) return;
        if (XRSettings.isDeviceActive) return;                 // 헤드셋은 월드 스페이스 UI — 해당 없음
        if (FindFirstObjectByType<ResponsiveCanvasScaler>() != null) return;

        var go = new GameObject("[ResponsiveCanvasScaler]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<ResponsiveCanvasScaler>();
    }

    private IEnumerator Start()
    {
        SceneManager.sceneLoaded += (s, m) => Apply();
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            Apply();
            yield return wait;
        }
    }

    /// <summary>
    /// 버튼이 있는데 GraphicRaycaster 가 없는 화면 UI 캔버스(PC_UI·CNV_Controller_PC·CNV_TaskList_PC 등)는
    /// 마우스·터치를 받지 못한다 → PC 에서 상단 아이콘·완료 버튼이 눌리지 않던 원인. 실행 중 자동으로 붙여 준다.
    /// </summary>
    private static void EnsureRaycasters()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.GetComponent<GraphicRaycaster>() != null) continue;
            if (canvas.GetComponentInChildren<Selectable>(true) == null) continue;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    // ── EventSystem 정리 ──
    // 로비(Client) 씬의 EventSystem 은 VR 용 AutoInputModule(손 레이저)만 달려 있다. 게임 씬이 더해지면 EventSystem 이 둘이 되고
    // (로그 ‘There can be only one active Event System.’), 먼저 켜진 로비 것이 ‘현재’ 가 되어 마우스·터치가 UI 에 전달되지 않는다
    // → PC 에서 위쪽 아이콘이 눌리지 않던 또 하나의 원인. 마우스/터치를 처리하는 모듈이 있는 EventSystem 하나만 남긴다.
    private static string _lastEsLog;

    private static bool HasPointerModule(EventSystem es)
    {
        foreach (var m in es.GetComponents<BaseInputModule>())
        {
            if (m == null || !m.enabled) continue;
            if (m is StandaloneInputModule || m.GetType().Name == "InputSystemUIInputModule") return true;
        }
        return false;
    }

    private static void EnsureWorkingEventSystem()
    {
        var all = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (all.Length == 0) return;

        // 현재 활성 씬(게임 씬)에 있는 것을 먼저 고른다. 로비 플레이어 프리팹에 딸린 EventSystem_01 은
        // 로비 전환 때 같이 꺼지거나 사라질 수 있어 후순위.
        EventSystem keep = null;
        var active = SceneManager.GetActiveScene();
        foreach (var es in all)
            if (es.gameObject.scene == active && HasPointerModule(es)) { keep = es; break; }
        if (keep == null)
            foreach (var es in all)
                if (es.enabled && HasPointerModule(es)) { keep = es; break; }
        if (keep != null && !keep.enabled) keep.enabled = true;

        if (keep == null)
        {
            // 마우스/터치 모듈이 있는 EventSystem 이 없다 → 하나를 골라 표준 모듈을 붙인다
            keep = EventSystem.current != null ? EventSystem.current : all[0];
            keep.enabled = true;
            if (keep.GetComponent<StandaloneInputModule>() == null) keep.gameObject.AddComponent<StandaloneInputModule>();
        }

        bool changed = false;
        foreach (var es in all)
            if (es != keep && es.enabled) { es.enabled = false; changed = true; }
        if (EventSystem.current != keep) { EventSystem.current = keep; changed = true; }

        if (changed)
        {
            string msg = $"[UI 입력] EventSystem {all.Length}개 → '{keep.name}' 하나만 사용 (모듈: {string.Join(",", System.Array.ConvertAll(keep.GetComponents<BaseInputModule>(), x => x.GetType().Name))})";
            if (msg != _lastEsLog) { _lastEsLog = msg; Debug.Log(msg); }
        }
    }

    private static void Apply()
    {
        EnsureWorkingEventSystem();
        EnsureRaycasters();
        foreach (var scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) continue;

            var canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;

            var r = scaler.referenceResolution;
            if (r.x < r.y) continue;                            // 세로 기준 캔버스는 대상 아님

            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
    }
}
