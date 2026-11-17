using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================
//  EquipmentBuilder.cs   (메뉴: Dodrill/장비/크레인·특수공구 프리팹 만들기)
//  매뉴얼 '특수공구'·'(P)HEV/EV 전용 장비' 표의 크레인 자키와 특수공구를 도형(프리미티브)으로 만든 네트워크 장비 프리팹을 만든다.
//  기존 장비 프리팹을 복제해서 잡기·동기화 설정(NetworkObject · SyncGrab · LocalNetworkTransform · Grabbable · TaskItem)을 그대로 물려받는다.
//    Equip_ShopCrane       ← Equip_Creeper(바닥에서 끄는 장비)  + ShopCrane
//    Equip_Tool_*          ← Equip_Spanner(손에 드는 공구)      + SpecialTool
//  결과는 07_Resources/Prefabs/Equipment 에 저장. 같은 이름이 있으면 덮어쓴다(멱등).
//  메뉴 2: '장비 자리 배치' — 열려 있는 씬의 Equip_Creeper EquipmentSlot 옆에 새 장비 자리를 만든다(저장은 직접).
// ============================================================
public static class EquipmentBuilder
{
    private const string Dir = "Assets/07_Resources/Prefabs/Equipment/";
    private const string Mats = Dir + "Materials/";

    private static Material M(string name) => AssetDatabase.LoadAssetAtPath<Material>(Mats + name + ".mat");

    private static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, string mat, Vector3? euler = null)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
        var m = M(mat);
        if (m != null) g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    private static GameObject Clone(string srcName, string newName, params System.Type[] drop)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + srcName + ".prefab");
        if (src == null) { Debug.LogError("[EquipmentBuilder] 원본 프리팹 없음: " + srcName); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = newName;
        for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        foreach (var t in drop)
            foreach (var c in go.GetComponents(t)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        return go;
    }

    private static void FitCollider(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        var bc = root.AddComponent<BoxCollider>();
        bc.center = root.transform.InverseTransformPoint(b.center);
        bc.size = b.size;
    }

    private static void Save(GameObject go, string name)
    {
        var ti = go.GetComponent<TaskItem>();
        if (ti != null) ti.prefabId = name;
        string path = Dir + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log("[EquipmentBuilder] 저장: " + path);
    }

    // ── 크레인 자키 ──
    private static void BuildCrane()
    {
        var go = Clone("Equip_Creeper", "Equip_ShopCrane", typeof(CreeperRide));
        if (go == null) return;
        var t = go.transform;

        // 다리(H 형): 좌우 다리 2 + 뒤 가로대 + 바퀴 4
        Prim(PrimitiveType.Cube, "LegL", t, new Vector3(-0.34f, 0.10f, 0.25f), new Vector3(0.07f, 0.09f, 1.15f), "Equip_Blue");
        Prim(PrimitiveType.Cube, "LegR", t, new Vector3(0.34f, 0.10f, 0.25f), new Vector3(0.07f, 0.09f, 1.15f), "Equip_Blue");
        Prim(PrimitiveType.Cube, "CrossBar", t, new Vector3(0f, 0.10f, -0.30f), new Vector3(0.75f, 0.09f, 0.08f), "Equip_Blue");
        foreach (var (x, z) in new[] { (-0.34f, -0.30f), (0.34f, -0.30f), (-0.34f, 0.78f), (0.34f, 0.78f) })
            Prim(PrimitiveType.Cylinder, "Caster", t, new Vector3(x, 0.035f, z), new Vector3(0.07f, 0.02f, 0.07f), "Equip_Black", new Vector3(0, 0, 90));

        // 마스트 + 보강
        Prim(PrimitiveType.Cube, "Mast", t, new Vector3(0f, 0.95f, -0.30f), new Vector3(0.09f, 1.7f, 0.09f), "Equip_Blue");
        Prim(PrimitiveType.Cube, "Brace", t, new Vector3(0f, 0.45f, -0.05f), new Vector3(0.05f, 0.05f, 0.55f), "Equip_Blue", new Vector3(-38, 0, 0));

        // 붐(Arm): 마스트를 따라 오르내린다. 자식으로 팔 + 갈고리
        var arm = new GameObject("Arm").transform;
        arm.SetParent(t, false);
        arm.localPosition = new Vector3(0f, 0.30f, -0.30f);
        Prim(PrimitiveType.Cube, "Collar", arm, Vector3.zero, new Vector3(0.14f, 0.12f, 0.14f), "Equip_Red");
        Prim(PrimitiveType.Cube, "Boom", arm, new Vector3(0f, 0f, 0.55f), new Vector3(0.07f, 0.07f, 1.1f), "Equip_Red");
        var hook = new GameObject("Hook").transform;
        hook.SetParent(arm, false);
        hook.localPosition = new Vector3(0f, -0.30f, 1.05f);
        Prim(PrimitiveType.Cylinder, "Chain", hook, new Vector3(0f, 0.15f, 0f), new Vector3(0.014f, 0.15f, 0.014f), "Equip_Chrome");
        Prim(PrimitiveType.Sphere, "HookBody", hook, Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f), "Equip_Yellow");

        // 펌프(손잡이) · 밸브
        var pump = new GameObject("Pump").transform;
        pump.SetParent(t, false);
        pump.localPosition = new Vector3(0.18f, 0.35f, -0.36f);
        Prim(PrimitiveType.Cylinder, "Handle", pump, new Vector3(0f, 0.16f, -0.20f), new Vector3(0.03f, 0.22f, 0.03f), "Equip_Black", new Vector3(-62, 0, 0));
        Prim(PrimitiveType.Cylinder, "Cylinder", pump, Vector3.zero, new Vector3(0.07f, 0.12f, 0.07f), "Equip_Chrome");
        var pc = pump.gameObject.AddComponent<BoxCollider>(); pc.center = new Vector3(0f, 0.18f, -0.22f); pc.size = new Vector3(0.12f, 0.2f, 0.5f);
        var valve = new GameObject("Valve").transform;
        valve.SetParent(t, false);
        valve.localPosition = new Vector3(-0.18f, 0.35f, -0.36f);
        Prim(PrimitiveType.Sphere, "Knob", valve, Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f), "Equip_Red");

        FitCollider(go);
        // 바퀴로 끌어 옮기는 장비 — 무겁게
        var fd = go.GetComponent<FloorDragProp>();
        if (fd != null) fd.maxSpeed = 1.0f;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.mass = 60f;
        go.AddComponent<ShopCrane>();
        Save(go, "Equip_ShopCrane");
    }

    // ── 특수공구 ──
    private static void BuildTool(string prefab, string id, string partNo, string name, System.Action<Transform> shape)
    {
        var go = Clone("Equip_Spanner", prefab, typeof(ManualTool));
        if (go == null) return;
        shape(go.transform);
        FitCollider(go);
        var st = go.AddComponent<SpecialTool>();
        st.toolId = id; st.partNo = partNo; st.toolName = name;
        Save(go, prefab);
    }

    // 바닥에서 끌어 옮기는 무거운 장비(Creeper 복제 — FloorDragProp 유지)
    private static void BuildFloorProp(string prefab, string id, string partNo, string name, float mass, System.Action<Transform> shape)
    {
        var go = Clone("Equip_Creeper", prefab, typeof(CreeperRide));
        if (go == null) return;
        shape(go.transform);
        FitCollider(go);
        var fd = go.GetComponent<FloorDragProp>();
        if (fd != null) fd.maxSpeed = 1.1f;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.mass = mass;
        var st = go.AddComponent<SpecialTool>();
        st.toolId = id; st.partNo = partNo; st.toolName = name;
        Save(go, prefab);
    }

    [MenuItem("Dodrill/장비/배터리 팩 장비 프리팹 만들기")]
    public static void BuildBatteryEquipment()
    {
        // 이송 행어 고정 핸들 09375-K4104: 금색 손잡이 4
        BuildTool("Equip_Tool_HangerKnob", "hangerknob", "09375-K4104", "이송 행어 고정 핸들", t =>
        {
            foreach (var (x, z) in new[] { (-0.05f, -0.05f), (0.05f, -0.05f), (-0.05f, 0.05f), (0.05f, 0.05f) })
            {
                Prim(PrimitiveType.Cylinder, "Knob", t, new Vector3(x, 0f, z), new Vector3(0.07f, 0.025f, 0.07f), "Equip_Yellow");
                Prim(PrimitiveType.Cylinder, "Neck", t, new Vector3(x, 0.03f, z), new Vector3(0.035f, 0.02f, 0.035f), "Equip_Yellow");
            }
        });

        // 배터리 모듈 행어 세트 09375-BF300: 노란 긴 프레임 + 갈고리 6
        BuildTool("Equip_Tool_ModuleHanger", "modhanger", "09375-BF300", "배터리 모듈 행어 세트", t =>
        {
            Prim(PrimitiveType.Cube, "FrameL", t, new Vector3(0f, 0f, -0.12f), new Vector3(0.95f, 0.03f, 0.035f), "Equip_Yellow");
            Prim(PrimitiveType.Cube, "FrameR", t, new Vector3(0f, 0f, 0.12f), new Vector3(0.95f, 0.03f, 0.035f), "Equip_Yellow");
            for (int i = 0; i < 6; i++)
            {
                float x = -0.40f + i * 0.16f;
                Prim(PrimitiveType.Cube, "Rung", t, new Vector3(x, 0f, 0f), new Vector3(0.03f, 0.03f, 0.26f), "Equip_Yellow");
                Prim(PrimitiveType.Cylinder, "Hook", t, new Vector3(x, -0.05f, 0f), new Vector3(0.012f, 0.05f, 0.012f), "Equip_Chrome");
            }
        });

        // 배터리 모듈 가이드 09375-GI900: 노란 블록 2
        BuildTool("Equip_Tool_ModuleGuide", "modguide", "09375-GI900", "배터리 모듈 가이드", t =>
        {
            Prim(PrimitiveType.Cube, "BlockA", t, new Vector3(-0.09f, 0f, 0f), new Vector3(0.14f, 0.04f, 0.04f), "Equip_Yellow");
            Prim(PrimitiveType.Cube, "BlockB", t, new Vector3(0.09f, 0f, 0f), new Vector3(0.14f, 0.04f, 0.04f), "Equip_Yellow");
        });

        // 배터리 모듈 면압 지그 09375-GI800: 긴 압착 바 + 양끝 고정구
        BuildTool("Equip_Tool_PressJig", "pressjig", "09375-GI800", "배터리 모듈 면압 지그", t =>
        {
            Prim(PrimitiveType.Cube, "Bar", t, Vector3.zero, new Vector3(0.60f, 0.04f, 0.05f), "Equip_Yellow");
            Prim(PrimitiveType.Cube, "ClampL", t, new Vector3(-0.28f, -0.04f, 0f), new Vector3(0.05f, 0.06f, 0.07f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "ClampR", t, new Vector3(0.28f, -0.04f, 0f), new Vector3(0.05f, 0.06f, 0.07f), "Equip_DarkMetal");
        });

        // 고전압 배터리 시스템 어셈블리 장착 가이드 핀 09375-BF000: 은색 핀 4
        BuildTool("Equip_Tool_GuidePin", "guidepin", "09375-BF000", "배터리 장착 가이드 핀", t =>
        {
            for (int i = 0; i < 4; i++)
            {
                Prim(PrimitiveType.Cylinder, "Pin", t, new Vector3(-0.09f + i * 0.06f, 0f, 0f), new Vector3(0.025f, 0.13f, 0.025f), "Equip_Chrome", new Vector3(0, 0, 90));
                Prim(PrimitiveType.Cylinder, "Tip", t, new Vector3(-0.09f + i * 0.06f, 0.14f, 0f), new Vector3(0.015f, 0.02f, 0.015f), "Equip_DarkMetal", new Vector3(0, 0, 90));
            }
        });

        // 갭 필러 고정 틀 09375-GI100: 알루미늄 프레임
        BuildTool("Equip_Tool_GapFrame", "gapframe", "09375-GI100", "갭 필러 고정 틀", t =>
        {
            foreach (var (x, z) in new[] { (-0.32f, -0.22f), (0.32f, -0.22f), (-0.32f, 0.22f), (0.32f, 0.22f) })
                Prim(PrimitiveType.Cube, "Post", t, new Vector3(x, 0f, z), new Vector3(0.035f, 0.30f, 0.035f), "Equip_Chrome");
            Prim(PrimitiveType.Cube, "TopA", t, new Vector3(0f, 0.15f, -0.22f), new Vector3(0.66f, 0.035f, 0.035f), "Equip_Chrome");
            Prim(PrimitiveType.Cube, "TopB", t, new Vector3(0f, 0.15f, 0.22f), new Vector3(0.66f, 0.035f, 0.035f), "Equip_Chrome");
            Prim(PrimitiveType.Cube, "Base", t, new Vector3(0f, -0.15f, 0f), new Vector3(0.66f, 0.03f, 0.46f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "Motor", t, new Vector3(0.34f, 0.12f, 0f), new Vector3(0.06f, 0.06f, 0.06f), "Equip_Blue");
        });

        // 갭 필러 작업 건 09375-GI200: 검은 본체 + 기둥 4
        BuildTool("Equip_Tool_GapGun", "gapgun", "09375-GI200", "갭 필러 작업 건", t =>
        {
            Prim(PrimitiveType.Cube, "Body", t, Vector3.zero, new Vector3(0.14f, 0.12f, 0.08f), "Equip_Black");
            Prim(PrimitiveType.Cube, "Plate", t, new Vector3(0f, 0.07f, 0f), new Vector3(0.15f, 0.015f, 0.09f), "Equip_Chrome");
            foreach (var x in new[] { -0.05f, 0.05f })
                Prim(PrimitiveType.Cylinder, "Rod", t, new Vector3(x, -0.12f, 0f), new Vector3(0.015f, 0.10f, 0.015f), "Equip_Chrome");
        });

        // 갭 필러 리본 09375-GI300: 파란 캡 + 나사 막대 + 삼각 날
        BuildTool("Equip_Tool_GapRibbon", "gapribbon", "09375-GI300", "갭 필러 리본", t =>
        {
            Prim(PrimitiveType.Cylinder, "Cap", t, new Vector3(0f, 0.14f, 0f), new Vector3(0.03f, 0.03f, 0.03f), "Equip_Blue");
            Prim(PrimitiveType.Cylinder, "Rod", t, Vector3.zero, new Vector3(0.012f, 0.14f, 0.012f), "Equip_Chrome");
            Prim(PrimitiveType.Cube, "Blade", t, new Vector3(0f, -0.14f, 0f), new Vector3(0.06f, 0.015f, 0.04f), "Equip_Chrome");
        });

        // 갭 필러 도포 튜브 09375-GI240: 흰 튜브 2
        BuildTool("Equip_Tool_GapTube", "gaptube", "09375-GI240", "갭 필러 도포 튜브", t =>
        {
            Prim(PrimitiveType.Cylinder, "TubeA", t, new Vector3(0f, 0f, -0.04f), new Vector3(0.07f, 0.17f, 0.07f), "Equip_Foam", new Vector3(0, 0, 90));
            Prim(PrimitiveType.Cylinder, "TubeB", t, new Vector3(0f, 0f, 0.04f), new Vector3(0.07f, 0.17f, 0.07f), "Equip_Foam", new Vector3(0, 0, 90));
        });

        // 모듈 충·방전 장비 xMB-9640(MIDTRONICS): 파란 하드케이스 + 위 표시창
        BuildTool("Equip_Tool_Balancer", "balancer", "xMB-9640", "모듈 충·방전 장비", t =>
        {
            Prim(PrimitiveType.Cube, "Case", t, Vector3.zero, new Vector3(0.50f, 0.28f, 0.34f), "Equip_Blue");
            Prim(PrimitiveType.Cube, "Panel", t, new Vector3(0f, 0.15f, 0f), new Vector3(0.28f, 0.03f, 0.20f), "Equip_Black");
            Prim(PrimitiveType.Cube, "Lcd", t, new Vector3(0f, 0.172f, 0f), new Vector3(0.18f, 0.005f, 0.10f), "Equip_Green");
            Prim(PrimitiveType.Cube, "Handle", t, new Vector3(0f, 0.14f, -0.20f), new Vector3(0.22f, 0.03f, 0.03f), "Equip_Black");
        });

        // 헬륨 가스·압력 조절기: 회색 가스통 + 조절기
        BuildTool("Equip_Tool_Helium", "helium", "-", "헬륨 가스·압력 조절기", t =>
        {
            Prim(PrimitiveType.Cylinder, "Tank", t, Vector3.zero, new Vector3(0.14f, 0.30f, 0.14f), "Equip_DarkMetal");
            Prim(PrimitiveType.Sphere, "Cap", t, new Vector3(0f, 0.30f, 0f), new Vector3(0.14f, 0.10f, 0.14f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "Regulator", t, new Vector3(0f, 0.38f, 0f), new Vector3(0.12f, 0.08f, 0.08f), "Equip_Chrome");
            Prim(PrimitiveType.Cylinder, "Gauge", t, new Vector3(0.06f, 0.40f, 0f), new Vector3(0.06f, 0.012f, 0.06f), "Equip_Red", new Vector3(0, 0, 90));
        });

        // 헬륨 가스 감지기: 흰 본체 + 파란 화면 + 프로브
        BuildTool("Equip_Tool_HeliumDetector", "heliumdet", "-", "헬륨 가스 감지기", t =>
        {
            Prim(PrimitiveType.Cube, "Body", t, Vector3.zero, new Vector3(0.07f, 0.22f, 0.04f), "Equip_Foam");
            Prim(PrimitiveType.Cube, "Screen", t, new Vector3(0f, 0.03f, 0.021f), new Vector3(0.05f, 0.07f, 0.004f), "Equip_Blue");
            Prim(PrimitiveType.Cylinder, "Probe", t, new Vector3(0f, 0.20f, 0f), new Vector3(0.008f, 0.10f, 0.008f), "Equip_Black");
        });

        // 벤팅 밸브 막음 지그: 은색 원판 + 손잡이(상부) / 원판 + 가로 레버(측면)
        BuildTool("Equip_Tool_VentJigTop", "ventjig_top", "-", "상부 벤팅 밸브 막음 지그", t =>
        {
            Prim(PrimitiveType.Cylinder, "Disc", t, Vector3.zero, new Vector3(0.14f, 0.012f, 0.14f), "Equip_Chrome");
            Prim(PrimitiveType.Sphere, "Knob", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.07f, 0.05f, 0.07f), "Equip_Black");
        });
        BuildTool("Equip_Tool_VentJigSide", "ventjig_side", "-", "측면 벤팅 밸브 막음 지그", t =>
        {
            Prim(PrimitiveType.Cylinder, "Disc", t, Vector3.zero, new Vector3(0.16f, 0.012f, 0.16f), "Equip_Chrome");
            Prim(PrimitiveType.Cube, "Lever", t, new Vector3(0f, 0.025f, 0f), new Vector3(0.18f, 0.02f, 0.02f), "Equip_Black");
        });

        // 고전압 배터리 모듈 분해 지그 TMS-1907: 바퀴 달린 검은 테이블 프레임
        BuildFloorProp("Equip_Tool_DisassemblyJig", "disjig", "TMS-1907", "배터리 모듈 분해 지그", 80f, t =>
        {
            foreach (var (x, z) in new[] { (-0.55f, -0.30f), (0.55f, -0.30f), (-0.55f, 0.30f), (0.55f, 0.30f) })
            {
                Prim(PrimitiveType.Cube, "Leg", t, new Vector3(x, 0.45f, z), new Vector3(0.05f, 0.90f, 0.05f), "Equip_Black");
                Prim(PrimitiveType.Cylinder, "Caster", t, new Vector3(x, 0.035f, z), new Vector3(0.07f, 0.02f, 0.07f), "Equip_Black", new Vector3(0, 0, 90));
            }
            Prim(PrimitiveType.Cube, "TopA", t, new Vector3(0f, 0.90f, -0.30f), new Vector3(1.15f, 0.05f, 0.05f), "Equip_Black");
            Prim(PrimitiveType.Cube, "TopB", t, new Vector3(0f, 0.90f, 0.30f), new Vector3(1.15f, 0.05f, 0.05f), "Equip_Black");
            Prim(PrimitiveType.Cube, "Shelf", t, new Vector3(0f, 0.25f, 0f), new Vector3(1.10f, 0.03f, 0.55f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "Plate", t, new Vector3(0f, 0.93f, 0f), new Vector3(0.50f, 0.02f, 0.45f), "Equip_Blue");
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("배터리 팩 장비", "배터리 팩 작업 장비 프리팹 15종을 만들었습니다.\n(07_Resources/Prefabs/Equipment)\n\n[Dodrill/장비/장비 자리 배치]를 다시 실행하면 씬에 자리가 만들어집니다.", "확인");
    }

    [MenuItem("Dodrill/장비/크레인·특수공구 프리팹 만들기")]
    public static void BuildAll()
    {
        BuildCrane();

        // 볼 조인트 풀러 09568-GI000: 위아래 판 + 볼트 두 개 + 가운데 나사
        BuildTool("Equip_Tool_Puller", "puller", "09568-GI000", "볼 조인트 풀러", t =>
        {
            Prim(PrimitiveType.Cube, "PlateTop", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.22f, 0.02f, 0.05f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "PlateBot", t, new Vector3(0f, -0.05f, 0f), new Vector3(0.22f, 0.02f, 0.05f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cylinder, "BoltL", t, new Vector3(-0.07f, 0f, 0f), new Vector3(0.02f, 0.06f, 0.02f), "Equip_Chrome");
            Prim(PrimitiveType.Cylinder, "BoltR", t, new Vector3(0.07f, 0f, 0f), new Vector3(0.02f, 0.06f, 0.02f), "Equip_Chrome");
            Prim(PrimitiveType.Cylinder, "Screw", t, new Vector3(0f, 0.09f, 0f), new Vector3(0.035f, 0.05f, 0.035f), "Equip_Black");
        });

        // 스트럿 너트 장착·탈거 공구 0K546-F6100: 굵은 소켓
        BuildTool("Equip_Tool_StrutNut", "strutnut", "0K546-F6100", "스트럿 너트 공구", t =>
        {
            Prim(PrimitiveType.Cylinder, "Socket", t, Vector3.zero, new Vector3(0.07f, 0.045f, 0.07f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "Drive", t, new Vector3(0f, 0.065f, 0f), new Vector3(0.03f, 0.03f, 0.03f), "Equip_Chrome");
        });

        // 쇽 업소버 록 너트 리무버 09546-3X100: 검은 케이스 + 노란 손잡이
        BuildTool("Equip_Tool_ShockNut", "shocknut", "09546-3X100", "쇽 록 너트 리무버", t =>
        {
            Prim(PrimitiveType.Cube, "Case", t, Vector3.zero, new Vector3(0.30f, 0.07f, 0.20f), "Equip_Black");
            Prim(PrimitiveType.Cube, "Latch", t, new Vector3(0f, 0.01f, 0.10f), new Vector3(0.08f, 0.03f, 0.015f), "Equip_Yellow");
        });

        // 엔진 서포트 지지대(빔) 09200-3N000: 긴 빔 + 양끝 서포트 + 갈고리 체인
        BuildTool("Equip_Tool_Beam", "beam", "09200-3N000", "엔진 서포트 지지대(빔)", t =>
        {
            Prim(PrimitiveType.Cube, "Beam", t, Vector3.zero, new Vector3(1.20f, 0.04f, 0.05f), "Equip_Yellow");
            Prim(PrimitiveType.Cube, "SupportL", t, new Vector3(-0.50f, -0.04f, 0f), new Vector3(0.06f, 0.07f, 0.12f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cube, "SupportR", t, new Vector3(0.50f, -0.04f, 0f), new Vector3(0.06f, 0.07f, 0.12f), "Equip_DarkMetal");
            Prim(PrimitiveType.Cylinder, "HookL", t, new Vector3(-0.20f, -0.10f, 0f), new Vector3(0.012f, 0.08f, 0.012f), "Equip_Chrome");
            Prim(PrimitiveType.Cylinder, "HookR", t, new Vector3(0.20f, -0.10f, 0f), new Vector3(0.012f, 0.08f, 0.012f), "Equip_Chrome");
        });

        // 고전압 배터리 이송 행어 09375-K4400: 파란 H 프레임 + 모서리 갈고리 4
        BuildTool("Equip_Tool_Hanger", "hanger", "09375-K4400", "고전압 배터리 이송 행어", t =>
        {
            Prim(PrimitiveType.Cube, "BarA", t, new Vector3(0f, 0f, -0.30f), new Vector3(0.90f, 0.035f, 0.05f), "Equip_Blue");
            Prim(PrimitiveType.Cube, "BarB", t, new Vector3(0f, 0f, 0.30f), new Vector3(0.90f, 0.035f, 0.05f), "Equip_Blue");
            Prim(PrimitiveType.Cube, "Cross", t, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.035f, 0.65f), "Equip_Blue");
            foreach (var (x, z) in new[] { (-0.42f, -0.30f), (0.42f, -0.30f), (-0.42f, 0.30f), (0.42f, 0.30f) })
                Prim(PrimitiveType.Cylinder, "Hook", t, new Vector3(x, -0.07f, z), new Vector3(0.015f, 0.07f, 0.015f), "Equip_Chrome");
        });

        // 타이어 휠 탈장착 소켓 09529-N1000: 주황 긴 소켓
        BuildTool("Equip_Tool_WheelSocket", "wheelsock", "09529-N1000", "타이어 휠 탈장착 소켓", t =>
        {
            Prim(PrimitiveType.Cylinder, "Body", t, Vector3.zero, new Vector3(0.04f, 0.06f, 0.04f), "Equip_HVOrange");
            Prim(PrimitiveType.Cylinder, "Tip", t, new Vector3(0f, -0.06f, 0f), new Vector3(0.035f, 0.02f, 0.035f), "Equip_DarkMetal");
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("장비 프리팹", "크레인 자키와 특수공구 프리팹을 만들었습니다.\n(07_Resources/Prefabs/Equipment)\n배터리 팩 장비는 [Dodrill/장비/배터리 팩 장비 프리팹 만들기]로 따로 만듭니다.\n\n이어서 [Dodrill/장비/장비 자리 배치]로 씬에 놓을 수 있습니다.", "확인");
    }

    [MenuItem("Dodrill/장비/장비 자리 배치 (열린 씬)")]
    public static void PlaceSlots()
    {
        // 기준: Equip_Creeper 를 쓰는 EquipmentSlot
        EquipmentSlot anchor = null;
        foreach (var s in Object.FindObjectsByType<EquipmentSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (s.prefab != null && s.prefab.name == "Equip_Creeper") { anchor = s; break; }
        if (anchor == null) { EditorUtility.DisplayDialog("장비 자리", "열린 씬에서 Equip_Creeper 자리를 못 찾았습니다.\n프리팹을 직접 씬에 EquipmentSlot 으로 놓아 주세요.", "확인"); return; }

        var items = new List<(string prefab, string label, Vector3 offset)>
        {
            ("Equip_ShopCrane", "Slot_ShopCrane", new Vector3(1.6f, 0f, 0f)),
            ("Equip_Tool_Puller", "Slot_Tool_Puller", new Vector3(3.0f, 0.9f, 0f)),
            ("Equip_Tool_StrutNut", "Slot_Tool_StrutNut", new Vector3(3.3f, 0.9f, 0f)),
            ("Equip_Tool_ShockNut", "Slot_Tool_ShockNut", new Vector3(3.6f, 0.9f, 0f)),
            ("Equip_Tool_Beam", "Slot_Tool_Beam", new Vector3(3.0f, 0.9f, 0.4f)),
            ("Equip_Tool_Hanger", "Slot_Tool_Hanger", new Vector3(3.3f, 0.9f, 0.4f)),
            ("Equip_Tool_WheelSocket", "Slot_Tool_WheelSocket", new Vector3(3.6f, 0.9f, 0.4f)),
            // 배터리 팩 장비 — 두 번째 줄(공구) · 바닥 장비
            ("Equip_Tool_HangerKnob", "Slot_Tool_HangerKnob", new Vector3(4.2f, 0.9f, 0f)),
            ("Equip_Tool_ModuleHanger", "Slot_Tool_ModuleHanger", new Vector3(4.5f, 0.9f, 0f)),
            ("Equip_Tool_ModuleGuide", "Slot_Tool_ModuleGuide", new Vector3(4.8f, 0.9f, 0f)),
            ("Equip_Tool_PressJig", "Slot_Tool_PressJig", new Vector3(5.1f, 0.9f, 0f)),
            ("Equip_Tool_GuidePin", "Slot_Tool_GuidePin", new Vector3(4.2f, 0.9f, 0.4f)),
            ("Equip_Tool_GapFrame", "Slot_Tool_GapFrame", new Vector3(4.5f, 0.9f, 0.4f)),
            ("Equip_Tool_GapGun", "Slot_Tool_GapGun", new Vector3(4.8f, 0.9f, 0.4f)),
            ("Equip_Tool_GapRibbon", "Slot_Tool_GapRibbon", new Vector3(5.1f, 0.9f, 0.4f)),
            ("Equip_Tool_GapTube", "Slot_Tool_GapTube", new Vector3(4.2f, 0.9f, 0.8f)),
            ("Equip_Tool_Balancer", "Slot_Tool_Balancer", new Vector3(4.6f, 0.9f, 0.8f)),
            ("Equip_Tool_Helium", "Slot_Tool_Helium", new Vector3(5.1f, 0.9f, 0.8f)),
            ("Equip_Tool_HeliumDetector", "Slot_Tool_HeliumDetector", new Vector3(5.4f, 0.9f, 0.8f)),
            ("Equip_Tool_VentJigTop", "Slot_Tool_VentJigTop", new Vector3(4.2f, 0.9f, 1.2f)),
            ("Equip_Tool_VentJigSide", "Slot_Tool_VentJigSide", new Vector3(4.5f, 0.9f, 1.2f)),
            ("Equip_Tool_DisassemblyJig", "Slot_Tool_DisassemblyJig", new Vector3(2.8f, 0f, 1.6f)),
        };
        int n = 0;
        foreach (var it in items)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + it.prefab + ".prefab");
            var nob = prefab != null ? prefab.GetComponent<FishNet.Object.NetworkObject>() : null;
            if (nob == null) { Debug.LogWarning("[EquipmentBuilder] 프리팹 없음 — 먼저 '프리팹 만들기'를 실행하세요: " + it.prefab); continue; }
            var existing = GameObject.Find(it.label);
            if (existing != null) Object.DestroyImmediate(existing);
            var g = new GameObject(it.label);
            g.transform.SetParent(anchor.transform.parent, false);
            g.transform.position = anchor.transform.position + it.offset;
            g.transform.rotation = anchor.transform.rotation;
            g.AddComponent<EquipmentSlot>().prefab = nob;
            n++;
        }
        EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
        EditorUtility.DisplayDialog("장비 자리", $"{n}개 자리를 Equip_Creeper 옆에 만들었습니다.\n위치는 씬에서 옮기고 씬을 저장하세요.", "확인");
    }
}
