using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ============================================================
//  PalmIconGenerator.cs — VR 손바닥 메뉴 아이콘(흰색 선 아이콘 128×128)을 코드로 그려 Resources/PalmIcons 에 PNG 로 저장한다.
//  메뉴: Tools > VR > 손바닥 메뉴 아이콘 생성. 같은 이름의 PNG 를 직접 만든 그림으로 바꿔 넣으면 그대로 쓰인다(흰색 투명 배경 권장 — 런타임이 색을 입힌다).
//  기존 PC/VR 메뉴바에 쓰던 아이콘(home·sound_on/off·exit·mr·tasks·tray·practice)은 02_UI 원본을 그대로 복사해 쓴다 — 여기서 만들지 않는다.
//  거리장(SDF) 합성 — 모서리가 부드럽고 어떤 크기에서도 선 굵기가 일정하다.
// ============================================================
public static class PalmIconGenerator
{
    public const string Folder = "Assets/Resources/PalmIcons";
    private const int S = 128;
    private const float W = 0.075f;   // 기본 선 굵기(0..1)

    private delegate float F(float x, float y);

    // ── 거리 도우미 (좌표 0..1, y 위쪽) ──
    private static float SegD(float x, float y, float ax, float ay, float bx, float by)
    {
        float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
        float t = Mathf.Clamp01((px * dx + py * dy) / Mathf.Max(1e-6f, dx * dx + dy * dy));
        float cx = ax + dx * t - x, cy = ay + dy * t - y;
        return Mathf.Sqrt(cx * cx + cy * cy);
    }
    private static float Line(float x, float y, float ax, float ay, float bx, float by, float w = W) => SegD(x, y, ax, ay, bx, by) - w * 0.5f;
    private static float Disc(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
    private static float Ring(float x, float y, float cx, float cy, float r, float w = W) => Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - w * 0.5f;
    private static float RectD(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - (hx - r), qy = Mathf.Abs(y - cy) - (hy - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }
    private static float Box(float x, float y, float cx, float cy, float hx, float hy, float r, float w = W) => Mathf.Abs(RectD(x, y, cx, cy, hx, hy, r)) - w * 0.5f;
    private static float BoxFill(float x, float y, float cx, float cy, float hx, float hy, float r) => RectD(x, y, cx, cy, hx, hy, r);
    private static float Tri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d = Mathf.Min(SegD(x, y, ax, ay, bx, by), Mathf.Min(SegD(x, y, bx, by, cx, cy), SegD(x, y, cx, cy, ax, ay)));
        float s1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
        float s2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
        float s3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
        bool inside = (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
        return inside ? -d : d;
    }
    // 중심 cx,cy 기준 각도(도, 0=오른쪽) 가 [a0,a1] 안인가 (a0<=a1, -180..180)
    private static bool InArc(float x, float y, float cx, float cy, float a0, float a1)
    {
        float a = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
        return a >= a0 && a <= a1;
    }
    private static float RingArc(float x, float y, float cx, float cy, float r, float a0, float a1, float w = W)
        => InArc(x, y, cx, cy, a0, a1) ? Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - w * 0.5f : 1f;
    private static float Ell(float x, float y, float cx, float cy, float a, float b, float w = W)
    {
        float k = Mathf.Sqrt(((x - cx) / a) * ((x - cx) / a) + ((y - cy) / b) * ((y - cy) / b));
        return Mathf.Abs(k - 1f) * Mathf.Min(a, b) - w * 0.5f;
    }
    private static float U(params float[] d) { float m = 9f; foreach (var v in d) if (v < m) m = v; return m; }

    private static readonly Dictionary<string, F> Icons = new Dictionary<string, F>
    {
        { "minimap", (x, y) => U(Box(x, y, .5f, .5f, .36f, .34f, .08f), Disc(x, y, .5f, .58f, .09f), Tri(x, y, .41f, .53f, .59f, .53f, .5f, .3f)) },
        { "skip", (x, y) => U(Tri(x, y, .24f, .26f, .24f, .74f, .62f, .5f), Line(x, y, .76f, .26f, .76f, .74f)) },
        { "recenter", (x, y) => U(Ring(x, y, .5f, .5f, .26f), Disc(x, y, .5f, .5f, .06f), Line(x, y, .5f, .9f, .5f, .76f), Line(x, y, .5f, .1f, .5f, .24f), Line(x, y, .1f, .5f, .24f, .5f), Line(x, y, .9f, .5f, .76f, .5f)) },
        { "xray", (x, y) => U(Ell(x, y, .5f, .5f, .4f, .25f), Disc(x, y, .5f, .5f, .11f)) },
        { "focus", (x, y) => U(Ring(x, y, .5f, .5f, .36f), Ring(x, y, .5f, .5f, .18f), Disc(x, y, .5f, .5f, .05f)) },
        { "record", (x, y) => U(Box(x, y, .5f, .45f, .27f, .35f, .06f), BoxFill(x, y, .5f, .83f, .11f, .05f, .02f), Line(x, y, .36f, .6f, .64f, .6f), Line(x, y, .36f, .44f, .64f, .44f), Line(x, y, .36f, .28f, .54f, .28f)) },
        { "hints", (x, y) => U(Box(x, y, .5f, .48f, .38f, .2f, .1f), Disc(x, y, .33f, .48f, .045f), Line(x, y, .6f, .48f, .74f, .48f), Line(x, y, .67f, .41f, .67f, .55f)) },
        { "menubar", (x, y) => U(Box(x, y, .5f, .63f, .38f, .14f, .05f), Disc(x, y, .3f, .63f, .045f), Disc(x, y, .5f, .63f, .045f), Disc(x, y, .7f, .63f, .045f), Line(x, y, .2f, .3f, .8f, .3f), Line(x, y, .2f, .18f, .6f, .18f)) },
        { "infocards", (x, y) => U(Box(x, y, .5f, .56f, .36f, .25f, .08f), Tri(x, y, .3f, .32f, .46f, .32f, .27f, .16f), Disc(x, y, .36f, .56f, .04f), Disc(x, y, .5f, .56f, .04f), Disc(x, y, .64f, .56f, .04f)) },
        { "steppopup", (x, y) => U(Box(x, y, .5f, .5f, .36f, .31f, .06f), Line(x, y, .14f, .67f, .86f, .67f), Disc(x, y, .24f, .76f, .035f)) },
    };

    [MenuItem("Tools/VR/손바닥 메뉴 아이콘 생성")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder);
        foreach (var kv in Icons)
        {
            var px = new Color32[S * S];
            for (int j = 0; j < S; j++)
                for (int i = 0; i < S; i++)
                {
                    float d = kv.Value((i + 0.5f) / S, (j + 0.5f) / S);
                    byte a = (byte)(Mathf.Clamp01(0.5f - d * S * 0.9f) * 255f);
                    px[j * S + i] = new Color32(255, 255, 255, a);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(Path.Combine(Folder, kv.Key + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        AssetDatabase.Refresh();
        Debug.Log($"[PalmIconGenerator] 아이콘 {Icons.Count}개 생성: {Folder}");
    }
}

/// <summary>Resources/PalmIcons 의 PNG 를 Sprite(2D)·밉맵 끔으로 가져온다.</summary>
public class PalmIconImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/PalmIcons/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.maxTextureSize = 256;
    }
}
