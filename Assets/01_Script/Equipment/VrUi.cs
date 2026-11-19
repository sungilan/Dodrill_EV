using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  VrUi.cs — VR 화면 UI 공통 도우미
//
//  · NewHudCanvas : PC·모바일은 화면 오버레이 캔버스, VR 은 머리를 따라다니는 월드 캔버스(1920×1080 기준)를 만든다.
//                   감전 위험 게이지·볼트 진행 막대처럼 'PC 에서 화면 가운데 아래에 뜨는 막대'를 VR 에서도 같은 자리·모양으로 쓰게 한다.
//  · OnTop        : 월드 캔버스의 글자·그림이 3D 물체(가이드 화살표·차체)에 가려지지 않게 항상 앞에 그린다(ZTest Always).
//                   Image → Dodrill/UIOnTop 셰이더, TMP → TextMeshPro/Distance Field Overlay(폰트 속성 복사).
// ============================================================
public static class VrUi
{
    public static bool IsVR => GameScenePlatformManager.IsVR;

    /// <summary>HUD 캔버스. VR 이면 월드 캔버스 + VrHeadHud(시선 따라다님), 아니면 ScreenSpaceOverlay.</summary>
    public static Canvas NewHudCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name);
        var cv = go.AddComponent<Canvas>();
        if (IsVR)
        {
            cv.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);
            rt.localScale = Vector3.one * VrHeadHud.Scale;
            go.AddComponent<VrHeadHud>();
        }
        else
        {
            cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = sortingOrder;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        }
        return cv;
    }

    /// <summary>HUD 를 다 만든 뒤 부른다 — VR 이면 항상 앞에 그리고 VR 카메라가 그리는 레이어로 맞춘다.</summary>
    public static void Finish(GameObject root)
    {
        if (root == null || !IsVR) return;
        OnTop(root);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
    }

    private static Material s_imgMat;
    private static Material ImgMat()
    {
        if (s_imgMat != null) return s_imgMat;
        var sh = Resources.Load<Shader>("Shaders/UIOnTop") ?? Shader.Find("Dodrill/UIOnTop");
        if (sh == null) return null;
        s_imgMat = new Material(sh) { name = "UIOnTop (runtime)" };
        return s_imgMat;
    }

    private static readonly Dictionary<TMP_FontAsset, Material> s_tmp = new Dictionary<TMP_FontAsset, Material>();
    private static Material TmpMat(TMP_FontAsset font)
    {
        if (font == null) return null;
        if (s_tmp.TryGetValue(font, out var m) && m != null) return m;
        var src = Resources.Load<Material>("Shaders/TMP_Overlay");
        Shader sh = src != null ? src.shader : Shader.Find("TextMeshPro/Distance Field Overlay");
        if (sh == null) return null;
        m = new Material(sh) { name = font.name + " Overlay (runtime)" };
        var fm = font.material;
        if (fm != null) { m.CopyPropertiesFromMaterial(fm); m.shaderKeywords = fm.shaderKeywords; }
        s_tmp[font] = m;
        return m;
    }

    /// <summary>root 아래 모든 그림·글자를 깊이 검사 없이 맨 앞에 그리게 한다(VR 월드 UI).</summary>
    public static void OnTop(GameObject root)
    {
        if (root == null) return;
        var im = ImgMat();
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMP_Text t)
            {
                var m = TmpMat(t.font);
                if (m != null) t.fontSharedMaterial = m;
            }
            else if (im != null) g.material = im;
        }
    }
}

/// <summary>VR HUD 캔버스를 시선 앞(1.25 m)에 부드럽게 따라붙인다. 화면 가운데 기준 PC 배치(y 오프셋 px)를 그대로 쓴다.</summary>
public class VrHeadHud : MonoBehaviour
{
    public const float Scale = 0.0009f;      // 1920 px → 약 1.7 m
    private const float Distance = 1.25f;
    private bool _placed;

    private void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 pos = cam.transform.position + cam.transform.forward * Distance;
        Quaternion rot = Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up);
        if (!_placed) { transform.SetPositionAndRotation(pos, rot); _placed = true; return; }
        float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, k), Quaternion.Slerp(transform.rotation, rot, k));
    }
}
