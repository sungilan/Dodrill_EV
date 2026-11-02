#if UNITY_EDITOR
using System.Collections.Generic;
using FishNet.Component.Animating;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// PC / 모바일 플레이어 프리팹에 아바타(기본: 정비사 Mechanic)와 아바타 리그를 연결한다.
/// 메뉴: Dodrill ▸ 아바타 ▸ PC·모바일 플레이어에 아바타 적용 (Mechanic)
///
/// 처음 적용(PcMobileAvatarController 없음):
///   1. 루트에 붙어 있던 카메라(Camera·AudioListener·URP 데이터·Outliner)를 제거하고,
///      자식 카메라를 새 'CameraPivot'(눈높이) 아래로 옮긴다  → 루트는 좌우만, 상하는 피벗만 돈다.
///   2. 'HoldPoint'를 CameraPivot 아래로 옮겨 시선을 따라가게 한다.
///   3. 아바타 프리팹을 'Avatar'로 배치, 기존 'SyncAvatar'(정지한 정비사 모델)는 끈다.
///   4. 루트에 PcMobileAvatarController + NetworkAnimator 추가 후 참조 연결.
/// 이미 적용된 프리팹이면 'Avatar'만 새 프리팹으로 교체하고 참조를 다시 연결한다(Swat → Mechanic 등).
/// 되돌리기: git 으로 프리팹 2개를 복원하면 된다.
/// </summary>
public static class PcAvatarInstaller
{
    public const string MechanicAvatarPath = "Assets/03_3D/Character/C_Prefabs/PcAvatar_Mechanic.prefab";
    public const string SwatAvatarPath = "Assets/03_3D/Character/C_Prefabs/PcAvatar_Swat.prefab";
    public const string AvatarPrefabPath = MechanicAvatarPath;
    public const string PcPrefabPath = "Assets/07_Resources/Prefabs/Player/PC_Player.prefab";
    public const string MobilePrefabPath = "Assets/07_Resources/Prefabs/Player/Mobile_Player.prefab";

    /// <summary>모델 크기 배율. 1 = 정비사 눈높이 약 1.65m.</summary>
    public const float AvatarScale = 1f;
    public const float EyeHeight = 1.7f;

    [MenuItem("Dodrill/아바타/PC·모바일 플레이어에 아바타 적용 (Mechanic)")]
    public static void InstallAll()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath) == null)
            PcAvatarBuilder.Build();

        int done = 0;
        foreach (var p in new[] { PcPrefabPath, MobilePrefabPath })
            if (Install(p)) done++;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("아바타 적용", $"{done}개 프리팹에 적용했습니다.\n자세한 내용은 Console 로그를 확인하세요.", "확인");
    }

    public static bool Install(string prefabPath, string avatarPath = AvatarPrefabPath)
    {
        var avatarAsset = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
        if (avatarAsset == null) { Debug.LogError($"[PcAvatarInstaller] 아바타 프리팹이 없습니다: {avatarPath}"); return false; }

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var look = root.GetComponent<IPcLookController>() as MonoBehaviour;
            if (look == null)
            {
                Debug.LogError($"[PcAvatarInstaller] {prefabPath}: 루트에 IPcLookController 구현체(FreeLookController / FreeLockControllerMobile)가 없습니다. 스크립트 Missing 여부를 확인하세요.");
                return false;
            }

            var ctrl = root.GetComponent<PcMobileAvatarController>();
            Transform pivot;
            Camera childCam;

            if (ctrl == null)
            {
                if (!PrepareCamera(root, prefabPath, out pivot, out childCam)) return false;
                ctrl = root.AddComponent<PcMobileAvatarController>();
                var oldAvatar = root.transform.Find("SyncAvatar");
                if (oldAvatar != null) oldAvatar.gameObject.SetActive(false);
            }
            else
            {
                // 교체 모드 — 기존 Avatar 제거
                pivot = root.transform.Find("CameraPivot");
                childCam = pivot != null ? pivot.GetComponentInChildren<Camera>(true) : null;
                if (pivot == null || childCam == null) { Debug.LogError($"[PcAvatarInstaller] {prefabPath}: CameraPivot/Camera 를 찾지 못해 교체할 수 없습니다."); return false; }
                var old = root.transform.Find("Avatar");
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // 아바타 배치
            var avatarGo = (GameObject)PrefabUtility.InstantiatePrefab(avatarAsset, root.transform);
            avatarGo.name = "Avatar";
            avatarGo.transform.localPosition = Vector3.zero;
            avatarGo.transform.localRotation = Quaternion.identity;
            avatarGo.transform.localScale = Vector3.one * AvatarScale;

            var anim = avatarGo.GetComponent<Animator>();
            var locomotion = avatarGo.GetComponent<PcLocomotionAnimator>();
            var armReach = avatarGo.GetComponent<PcArmReach>();
            var headAim = avatarGo.GetComponent<PcHeadAim>();
            if (anim == null || locomotion == null || armReach == null)
            {
                Debug.LogError("[PcAvatarInstaller] 아바타 프리팹에 Animator / PcLocomotionAnimator / PcArmReach 가 없습니다.");
                return false;
            }
            SetRef(headAim, "ownerCamera", childCam.transform);
            SetRef(armReach, "bodyRef", root.transform);

            // 소유자 화면에서 숨길 머리 부위 렌더러 (1인칭 카메라가 머리 안쪽을 보는 문제)
            var heads = new List<Renderer>();
            foreach (var r in avatarGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (IsHeadPart(r.name)) heads.Add(r);
            if (heads.Count == 0) Debug.LogWarning("[PcAvatarInstaller] 머리 부위 렌더러를 찾지 못했습니다 — 1인칭에서 머리 안쪽이 보일 수 있습니다.");

            // 컨트롤러 연결
            var so = new SerializedObject(ctrl);
            so.FindProperty("avatarAnimator").objectReferenceValue = anim;
            so.FindProperty("cameraPivot").objectReferenceValue = pivot;
            so.FindProperty("lookControllerBehaviour").objectReferenceValue = look;
            so.FindProperty("locomotion").objectReferenceValue = locomotion;
            so.FindProperty("armReach").objectReferenceValue = armReach;
            so.FindProperty("eyeHeight").floatValue = EyeHeight;
            // Dodrill: 스포너가 바닥(스폰 지점)에 생성하고 수직 비행이 없으므로 접지 보정은 끔.
            so.FindProperty("pinRootToGround").boolValue = false;
            var hide = so.FindProperty("hideRenderersForOwner");
            hide.arraySize = heads.Count;
            for (int i = 0; i < heads.Count; i++) hide.GetArrayElementAtIndex(i).objectReferenceValue = heads[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            SetRef(look, "cameraPivot", pivot);

            var na = root.GetComponent<NetworkAnimator>();
            if (na == null) na = root.AddComponent<NetworkAnimator>();
            var nso = new SerializedObject(na);
            SetProp(nso, "_animator", anim);
            SetBool(nso, "_smoothFloats", true);
            SetInt(nso, "_interpolation", 2);
            SetBool(nso, "_clientAuthoritative", true);
            SetBool(nso, "_sendToOwner", false);
            nso.ApplyModifiedPropertiesWithoutUndo();

            var no = root.GetComponent<NetworkObject>();
            if (no != null) EditorUtility.SetDirty(no);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[PcAvatarInstaller] {prefabPath}: 적용 완료 (아바타 {avatarAsset.name}, 머리 숨김 렌더러 {heads.Count}개)");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 카메라 정리 (처음 적용 시) ───────────────────────

    private static bool PrepareCamera(GameObject root, string prefabPath, out Transform pivot, out Camera childCam)
    {
        pivot = null; childCam = null;
        Camera rootCam = root.GetComponent<Camera>();
        foreach (var c in root.GetComponentsInChildren<Camera>(true))
        {
            if (c.gameObject == root) continue;
            if (c.name.Contains("UI")) continue;
            childCam = c; break;
        }
        if (childCam == null)
        {
            Debug.LogError($"[PcAvatarInstaller] {prefabPath}: 자식 카메라를 찾지 못했습니다.");
            return false;
        }

        pivot = new GameObject("CameraPivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, EyeHeight * AvatarScale, 0f);
        pivot.localRotation = Quaternion.identity;

        childCam.transform.SetParent(pivot, false);
        childCam.transform.localPosition = Vector3.zero;
        childCam.transform.localRotation = Quaternion.identity;

        if (rootCam != null)
        {
            var rootListener = root.GetComponent<AudioListener>();
            var childListener = childCam.GetComponent<AudioListener>();
            RepointReferences(root, rootCam, childCam);
            if (rootListener != null && childListener != null) RepointReferences(root, rootListener, childListener);

            // ⚠ 자식 카메라의 컬링 마스크는 루트 카메라와 달라 Bolt·MeasurePoint·Player 레이어를 그리지 않는다(볼트·측정점·가이드가 안 보임).
            //   실제로 쓰이던 루트 카메라의 렌더 설정을 그대로 넘긴다. near 는 컨트롤러가 런타임에 낮추므로 자식 값(0.01) 유지.
            childCam.cullingMask = rootCam.cullingMask;
            childCam.farClipPlane = rootCam.farClipPlane;
            childCam.forceIntoRenderTexture = rootCam.forceIntoRenderTexture;
            var rootUrp = rootCam.GetComponent<UniversalAdditionalCameraData>();
            var childUrp = childCam.GetComponent<UniversalAdditionalCameraData>();
            if (rootUrp != null && childUrp != null)
            {
                // 자식 카메라는 SMAA+후처리가 켜져 있어 (루트 카메라로 보던 때와 달리) 윤곽선·가장자리가 번져 보였다 → 원래 값으로
                childUrp.antialiasing = rootUrp.antialiasing;
                childUrp.renderPostProcessing = rootUrp.renderPostProcessing;
            }

            if (!Mathf.Approximately(rootCam.fieldOfView, childCam.fieldOfView))
                Debug.LogWarning($"[PcAvatarInstaller] 루트 카메라 FOV({rootCam.fieldOfView})와 자식 카메라 FOV({childCam.fieldOfView})가 다릅니다. 자식 카메라 값을 사용합니다.");

            // 의존 순서대로 제거: Outliner / URP 데이터 → AudioListener → Camera
            // ⚠ 루트 카메라의 Outliner 는 볼트·부품 가이드 윤곽선을 그리는 컴포넌트다. 지우기만 하면 가이드가 안 보이므로
            //   새 카메라(자식)로 값을 그대로 옮긴 뒤 지운다.
            var rootOutliner = root.GetComponent<EPOOutline.Outliner>();
            if (rootOutliner != null && childCam.GetComponent<EPOOutline.Outliner>() == null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(rootOutliner);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(childCam.gameObject);
            }
            DestroyIfAny<EPOOutline.Outliner>(root);
            DestroyIfAny<UniversalAdditionalCameraData>(root);
            DestroyIfAny<AudioListener>(root);
            Object.DestroyImmediate(rootCam);
        }

        // HoldPoint → 시선 따라가게
        var hold = root.transform.Find("HoldPoint");
        if (hold != null)
        {
            Vector3 lp = hold.localPosition; Quaternion lr = hold.localRotation;
            hold.SetParent(pivot, false);
            hold.localPosition = lp; hold.localRotation = lr;   // 예전엔 카메라=루트 원점이었으므로 카메라 기준 오프셋 유지
        }
        else Debug.LogWarning($"[PcAvatarInstaller] {prefabPath}: 'HoldPoint'가 없습니다.");
        return true;
    }

    private static bool IsHeadPart(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("head") || n.Contains("hair") || n.Contains("eyes") || n.Contains("facemask")
            || n.Contains("faceshield") || n.Contains("glasses");
    }

    // ── 유틸 ─────────────────────────────────────────────

    private static void DestroyIfAny<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Object.DestroyImmediate(c);
    }

    private static void SetRef(Object target, string field, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[PcAvatarInstaller] {target.GetType().Name}.{field} 필드를 찾지 못했습니다."); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetProp(SerializedObject so, string name, Object v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.objectReferenceValue = v; else Debug.LogWarning($"[PcAvatarInstaller] NetworkAnimator.{name} 없음");
    }
    private static void SetBool(SerializedObject so, string name, bool v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.boolValue = v; else Debug.LogWarning($"[PcAvatarInstaller] NetworkAnimator.{name} 없음");
    }
    private static void SetInt(SerializedObject so, string name, int v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.intValue = v; else Debug.LogWarning($"[PcAvatarInstaller] NetworkAnimator.{name} 없음");
    }

    /// <summary>root 계층의 모든 컴포넌트에서 from 을 가리키는 오브젝트 참조를 to 로 바꾼다.</summary>
    private static void RepointReferences(GameObject root, Object from, Object to)
    {
        foreach (var comp in root.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || comp == from || comp is Transform) continue;
            var so = new SerializedObject(comp);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == from)
                {
                    it.objectReferenceValue = to;
                    changed = true;
                    Debug.Log($"[PcAvatarInstaller] 참조 교체: {comp.GetType().Name}.{it.propertyPath} → {to.name}");
                }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
