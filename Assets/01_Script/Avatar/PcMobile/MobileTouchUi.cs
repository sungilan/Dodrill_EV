using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ============================================================
//  MobileTouchUi.cs — 모바일 UI 터치 보강 (모듈화 ①, 2026-11-20 — FreeLockControllerMobile 에서 분리)
//
//  모바일에서 버튼이 거의 안 눌리던 문제:
//  ① 컨트롤러는 시선·이동·상호작용을 레거시 Input.touches 로 읽는데 EventSystem 은 Input System UI 모듈이라
//     같은 손가락을 두 입력 시스템이 따로 해석한다 → 모바일에서는 레거시 터치 기반 StandaloneInputModule 로 맞춘다.
//  ② 오버레이 캔버스 중 GraphicRaycaster 가 없는 곳은 버튼이 아예 터치를 못 받으므로 추가한다.
// ============================================================
public static class MobileTouchUi
{
    /// <summary>모바일 기기에서만 UI 입력 모듈을 레거시 터치(StandaloneInputModule)로 바꾼다</summary>
    public static void UseLegacyTouchModule(EventSystem es, Action<string> log = null)
    {
        if (es == null || !Application.isMobilePlatform) return;   // 에디터·PC 에서는 기존 모듈 유지

        StandaloneInputModule legacy = es.GetComponent<StandaloneInputModule>();
        bool changed = false;
        foreach (var m in es.GetComponents<BaseInputModule>())
        {
            if (m == null || m is StandaloneInputModule) continue;
            if (m.GetType().Name == "InputSystemUIInputModule" && m.enabled) { m.enabled = false; changed = true; }
        }
        if (legacy == null) { legacy = es.gameObject.AddComponent<StandaloneInputModule>(); changed = true; }
        if (!legacy.enabled) { legacy.enabled = true; changed = true; }
        if (changed) log?.Invoke("UI 입력 모듈을 레거시 터치(StandaloneInputModule)로 전환");
    }

    /// <summary>버튼·스크롤이 있는 오버레이 루트 캔버스에 GraphicRaycaster 가 없으면 붙인다</summary>
    public static void EnsureRaycasters(Action<string> log = null)
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if (c.GetComponent<GraphicRaycaster>() != null) continue;
            if (c.GetComponentInChildren<Selectable>(true) == null && c.GetComponentInChildren<ScrollRect>(true) == null) continue;
            c.gameObject.AddComponent<GraphicRaycaster>();
            log?.Invoke($"GraphicRaycaster 없는 캔버스에 추가: {c.name}");
        }
    }
}
