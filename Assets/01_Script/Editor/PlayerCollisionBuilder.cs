#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Game 씬에 '플레이어가 못 지나가는' 단순 콜라이더(작업장 벽 + 바닥에 서 있는 가구)를 만든다.
///   Tools ▸ DoDrill ▸ 플레이어 충돌 만들기
///
/// · 만들어진 콜라이더는 모두 'Ignore Raycast' 레이어(2) — 기본 Raycast(클릭·호버)에는 안 잡힌다.
/// · 물리 충돌 행렬에서 이 레이어는 'Player'(VR AutoHandPlayer 캡슐)하고만 충돌시킨다. (도구·부품·손이 가구 콜라이더 안에서 튕기지 않게)
/// · PC·모바일은 PlayerCollision 의 캡슐 스윕으로 막는다.
/// · 바닥·천장 콜라이더는 만들지 않는다(플레이어는 수평 이동만 하고, PC 루트 접지/발 IK 가 바닥 높이를 직접 쓴다).
/// · 차 리프트·차량은 일부러 뺐다(차 밑에서 작업해야 하고, 리프트가 오르내린다).
///
/// 다시 실행하면 기존 '[PlayerCollision]' 루트를 지우고 새로 만든다.
/// </summary>
public static class PlayerCollisionBuilder
{
    public const string RootName = "[PlayerCollision]";

    // 작업장 안쪽 면(월드 좌표). 벽 렌더러 위치에서 측정: 왼쪽 -5.0(차고 문 안쪽) · 오른쪽 6.85 · 뒤 -5.45(유리벽) · 앞 9.45(창문 벽 안쪽)
    public static float XMin = -5.0f, XMax = 6.85f, ZMin = -5.45f, ZMax = 9.45f;
    public const float WallThickness = 0.5f;
    public const float WallBottom = -1f, WallTop = 7f;

    // 막지 않을 이름(문·작은 물건)
    private static readonly string[] ExcludeNameContains = { "Door", "TrafficCone", "Decal", "CarLift", "Lift", "ElectricCar", "Ioniq" };

    [MenuItem("Tools/DoDrill/플레이어 충돌 만들기 (Game 씬)")]
    public static void BuildMenu()
    {
        var log = Build(saveScene: true);
        Debug.Log(log);
    }

    public static string Build(bool saveScene)
    {
        var sb = new StringBuilder();
        var scene = FindOrOpenGameScene(out bool openedHere);
        if (!scene.IsValid()) return "Game 씬을 열 수 없습니다.";

        // 기존 것 삭제
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == RootName) Object.DestroyImmediate(r);

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.layer = PlayerCollision.BlockLayer;

        // ── 벽 4면 ──
        float zSpan = (ZMax - ZMin) + WallThickness * 2f, xSpan = (XMax - XMin) + WallThickness * 2f;
        float h = WallTop - WallBottom, cy = (WallTop + WallBottom) * 0.5f;
        MakeBox(root.transform, "Wall_Left",  new Vector3(XMin - WallThickness * 0.5f, cy, (ZMin + ZMax) * 0.5f), new Vector3(WallThickness, h, zSpan));
        MakeBox(root.transform, "Wall_Right", new Vector3(XMax + WallThickness * 0.5f, cy, (ZMin + ZMax) * 0.5f), new Vector3(WallThickness, h, zSpan));
        MakeBox(root.transform, "Wall_Back",  new Vector3((XMin + XMax) * 0.5f, cy, ZMin - WallThickness * 0.5f), new Vector3(xSpan, h, WallThickness));
        MakeBox(root.transform, "Wall_Front", new Vector3((XMin + XMax) * 0.5f, cy, ZMax + WallThickness * 0.5f), new Vector3(xSpan, h, WallThickness));
        sb.AppendLine($"[PlayerCollision] 벽 4면: x[{XMin},{XMax}] z[{ZMin},{ZMax}]");

        // ── 바닥에 서 있는 가구 ──
        int made = 0;
        var props = new Transform[0];
        var env = FindPath(scene, "MR_Object/Environment_Objects");
        var candidates = new List<Transform>();
        if (env != null) foreach (Transform c in env) candidates.Add(c);
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == "FBX_ToolChest") candidates.Add(r.transform);   // 씬 루트에 놓인 공구함

        foreach (var c in candidates)
        {
            if (c == null || !c.gameObject.activeSelf) continue;
            if (IsExcluded(c.name)) continue;
            if (!TryGetBounds(c, out Bounds b)) continue;

            float height = b.size.y, top = b.max.y, bottom = b.min.y, foot = Mathf.Max(b.size.x, b.size.z);
            // 바닥에 서 있고(밑면 0.35m 이하), 사람 몸에 걸릴 만한 높이(0.6m 이상·윗면 0.8m 이상), 어느 정도 크기(한 변 0.35m 이상)
            if (!(height >= 0.6f && top >= 0.8f && bottom <= 0.35f && foot >= 0.35f)) continue;

            MakeBox(root.transform, "Prop_" + c.name, b.center, b.size);
            made++;
            sb.AppendLine($"   + {c.name}  center=({b.center.x:F2},{b.center.y:F2},{b.center.z:F2}) size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})");
        }
        sb.AppendLine($"[PlayerCollision] 가구 {made}개");

        // ── 물리 충돌 행렬 ──
        // ① Ignore Raycast(2) 는 VR 몸체하고만 충돌. VR 몸체(AutoHandPlayer 캡슐)는 시작할 때 레이어를 'HandPlayer'(9)로 바꾼다
        //    (AutoHandPlayer.cs:237) — 프리팹에는 Player(16) 로 저장돼 있어 예전에는 16 만 허용해서 VR 몸이 벽·가구를 뚫었다.
        int player = LayerMask.NameToLayer("Player");
        int handPlayer = LayerMask.NameToLayer("HandPlayer");
        for (int j = 0; j < 32; j++)
            Physics.IgnoreLayerCollision(PlayerCollision.BlockLayer, j, j != player && j != handPlayer);

        // ② VR 몸체는 상호작용 오브젝트와는 충돌하지 않는다 — 차 해치·문(Interactable), 볼트(Bolt), Door 의 솔리드 콜라이더가
        //    열리고 닫히는 동안 VR 몸을 밀어내던 문제. (손·도구와의 충돌은 그대로)
        if (handPlayer >= 0)
            foreach (var n in new[] { "Interactable", "Bolt", "Door" })
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0) Physics.IgnoreLayerCollision(handPlayer, l, true);
            }
        sb.AppendLine("[PlayerCollision] 충돌 행렬: 레이어 2(Ignore Raycast) ↔ Player(" + player + ")·HandPlayer(" + handPlayer + "), HandPlayer ↮ Interactable·Bolt·Door");

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
        return sb.ToString();
    }

    private static bool IsExcluded(string name)
    {
        foreach (var s in ExcludeNameContains)
            if (name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static Scene FindOrOpenGameScene(out bool openedHere)
    {
        openedHere = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.name == "Game" && s.isLoaded) return s;
        }
        openedHere = true;
        return EditorSceneManager.OpenScene("Assets/00_Scene/Game.unity", OpenSceneMode.Additive);
    }

    private static Transform FindPath(Scene scene, string path)
    {
        var parts = path.Split('/');
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.name != parts[0]) continue;
            Transform t = r.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t;
        }
        return null;
    }

    // 그림자 전용 큐브(Shadow*)와 파티클·라인은 제외하고 눈에 보이는 렌더러만 합친 월드 경계
    private static bool TryGetBounds(Transform t, out Bounds b)
    {
        b = default; bool any = false;
        foreach (var rd in t.GetComponentsInChildren<Renderer>(false))
        {
            if (rd.name.StartsWith("Shadow")) continue;
            if (rd is ParticleSystemRenderer || rd is LineRenderer || rd is TrailRenderer) continue;
            if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds);
        }
        return any;
    }

    private static void MakeBox(Transform parent, string name, Vector3 worldCenter, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldCenter;
        go.layer = PlayerCollision.BlockLayer;
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
    }
}
#endif
