#if UNITY_EDITOR
using System.Linq;
using RootMotion.FinalIK;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 정비사(Mechanic) 휴머노이드 모델로 PC/모바일 아바타 프리팹(PcAvatar_Mechanic)을 만든다.
/// 공군 프로젝트의 Swat 아바타와 같은 구성(LookAtIK + 다리·팔 LimbIK + Pc* 스크립트)을
/// 휴머노이드 본 매핑으로 다시 조립한다. 애니메이터 컨트롤러는 휴머노이드 로코모션이라 그대로 재사용.
/// 메뉴: Dodrill ▸ 아바타 ▸ Mechanic 아바타 프리팹 생성
/// </summary>
public static class PcAvatarBuilder
{
    public const string FbxPath = "Assets/03_3D/Character/C_FBX/Player_Character/Rig_Player_Mechanic.fbx";
    public const string ControllerPath = "Assets/03_3D/Character/C_FBX/Player_Character/Swat Animator Controller.controller";
    public const string OutPath = "Assets/03_3D/Character/C_Prefabs/PcAvatar_Mechanic.prefab";

    // 기본으로 켜 두는 파츠(기존 PC 프리팹의 Mechanic과 동일). 나머지(FaceShield/Helmet/InsulatedShoes/SafetyGlasses)는 PPE 연출용으로 꺼 둔다.
    private static readonly string[] DefaultOff = { "SM_Mechanic_FaceShield", "SM_Mechanic_Hair_Helmet", "SM_Mechanic_InsulatedShoes", "SM_Mechnic_SafetyGlasses" };

    [MenuItem("Dodrill/아바타/Mechanic 아바타 프리팹 생성")]
    public static void BuildMenu()
    {
        var p = Build();
        if (p != null) EditorUtility.DisplayDialog("Mechanic 아바타", "생성 완료:\n" + OutPath, "확인");
    }

    /// <summary>
    /// 이미 만든 아바타 프리팹의 손 쥠 값만 다시 굽는다 (FBX 휴머노이드 매핑을 고친 뒤 사용).
    /// 프리팹의 다른 수정 사항은 그대로 둔다.
    /// </summary>
    [MenuItem("Dodrill/아바타/Mechanic 손 쥠 다시 굽기")]
    public static void RebakeGrip()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var contents = PrefabUtility.LoadPrefabContents(OutPath);
        try
        {
            var anim = contents.GetComponent<Animator>();
            var grip = contents.GetComponent<PcHandGrip>();
            if (fbx == null || anim == null || grip == null) { Debug.LogError("[PcAvatarBuilder] 손 쥠 다시 굽기 실패: FBX/Animator/PcHandGrip 없음"); return; }
            anim.Rebind(); anim.Update(0f);
            BakeGrip(grip, anim, fbx);
            PrefabUtility.SaveAsPrefabAsset(contents, OutPath);
            Debug.Log("[PcAvatarBuilder] 손 쥠 다시 굽기 완료: " + OutPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    public static GameObject Build()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (fbx == null || controller == null) { Debug.LogError("[PcAvatarBuilder] FBX 또는 애니메이터 컨트롤러를 찾지 못했습니다."); return null; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        go.name = "PcAvatar_Mechanic";
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;

        try
        {
            var anim = go.GetComponent<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) { Debug.LogError("[PcAvatarBuilder] 휴머노이드 아바타가 아닙니다."); return null; }
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Rebind(); anim.Update(0f);   // 기본 자세로 본 위치 확정

            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var root = go.transform;

            // 파츠 기본 활성 상태
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.gameObject.SetActive(System.Array.IndexOf(DefaultOff, r.name) < 0);

            // ── LookAtIK (머리·상체 시선) ──
            var look = go.AddComponent<LookAtIK>();
            var spine = new System.Collections.Generic.List<Transform>();
            if (B(HumanBodyBones.Chest)) spine.Add(B(HumanBodyBones.Chest));
            if (B(HumanBodyBones.UpperChest)) spine.Add(B(HumanBodyBones.UpperChest));
            if (spine.Count == 0 && B(HumanBodyBones.Spine)) spine.Add(B(HumanBodyBones.Spine));
            look.solver.SetChain(spine.ToArray(), B(HumanBodyBones.Head), new Transform[0], root);
            look.solver.clampWeightHead = 0.5f; look.solver.clampWeightEyes = 0.5f; look.solver.clampSmoothing = 2;

            // ── 다리 LimbIK + 벤드 골 ──
            var legL = MakeLimb(go, B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), root, AvatarIKGoal.LeftFoot, goal: true);
            var legR = MakeLimb(go, B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), root, AvatarIKGoal.RightFoot, goal: true);
            legL.solver.bendGoal = MakeBendGoal(root, "BendGoal_L", B(HumanBodyBones.LeftLowerLeg));
            legR.solver.bendGoal = MakeBendGoal(root, "BendGoal_R", B(HumanBodyBones.RightLowerLeg));

            // ── 팔 LimbIK (손 뻗기용, 평소 가중치 0) ──
            var armL = MakeLimb(go, B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), root, AvatarIKGoal.LeftHand, goal: false);
            var armR = MakeLimb(go, B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), root, AvatarIKGoal.RightHand, goal: false);

            // ── Pc* 스크립트 ──
            var loco = go.AddComponent<PcLocomotionAnimator>();
            var head = go.AddComponent<PcHeadAim>();
            var crouch = go.AddComponent<PcCrouchIK>();
            var driver = go.AddComponent<PcAvatarIKDriver>();
            var reach = go.AddComponent<PcArmReach>();

            Set(head, "lookAt", look);
            Set(crouch, "hips", B(HumanBodyBones.Hips)); Set(crouch, "leftLeg", legL); Set(crouch, "rightLeg", legR);
            Set(reach, "leftArm", armL); Set(reach, "rightArm", armR);
            Set(driver, "anim", anim); Set(driver, "crouch", crouch); Set(driver, "headAim", head); Set(driver, "armReach", reach);

            // ── 물체 잡기: 손가락 쥠 + 손 방향 ──
            var grip = go.AddComponent<PcHandGrip>();
            BakeGrip(grip, anim, fbx);
            Set(driver, "handGrip", grip);

            var saved = PrefabUtility.SaveAsPrefabAsset(go, OutPath);
            Debug.Log("[PcAvatarBuilder] 생성: " + OutPath);
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ══════════════════════════════════════════════════════
    //  손 쥠 포즈 굽기 — 휴머노이드 근육 값으로 "쥔 손"을 만들고, 기본 포즈와의 로컬 회전 차이를 저장한다
    // ══════════════════════════════════════════════════════
    private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

    private static HumanBodyBones[] FingerBones(bool right)
    {
        string s = right ? "Right" : "Left";
        var list = new System.Collections.Generic.List<HumanBodyBones>();
        foreach (var f in FingerNames)
            foreach (var seg in new[] { "Proximal", "Intermediate", "Distal" })
                list.Add((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), s + f + seg));
        return list.ToArray();
    }

    /// <summary>쥠 정도(근육 값, -1 이 최대로 굽힘). 손잡이를 한 손으로 쥐는 정도.</summary>
    private static float CurlValue(string finger, int segment)
    {
        if (finger == "Thumb") return segment == 1 ? -0.30f : -0.55f;
        return segment == 1 ? -0.60f : segment == 2 ? -0.75f : -0.70f;
    }

    private static void BakeGrip(PcHandGrip grip, Animator anim, GameObject fbx)
    {
        // 1) 임시 인스턴스에서 기본 포즈 / 쥔 포즈의 손가락 로컬 회전을 구한다 (실제 프리팹 본은 건드리지 않음)
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        try
        {
            var tAnim = tmp.GetComponent<Animator>();
            var handler = new HumanPoseHandler(tAnim.avatar, tAnim.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            var neutralPose = pose;
            neutralPose.muscles = (float[])pose.muscles.Clone();

            var muscleIndex = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < HumanTrait.MuscleName.Length; i++) muscleIndex[HumanTrait.MuscleName[i]] = i;

            foreach (bool right in new[] { true, false })
            {
                string side = right ? "Right" : "Left";
                var bones = FingerBones(right);
                var neutral = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++) neutral[i] = tAnim.GetBoneTransform(bones[i]).localRotation;

                // 쥔 포즈
                var curled = new HumanPose { bodyPosition = neutralPose.bodyPosition, bodyRotation = neutralPose.bodyRotation, muscles = (float[])neutralPose.muscles.Clone() };
                foreach (var f in FingerNames)
                    for (int seg = 1; seg <= 3; seg++)
                    {
                        string name = side + " " + f + " " + seg + " Stretched";
                        if (muscleIndex.TryGetValue(name, out int mi)) curled.muscles[mi] = CurlValue(f, seg);
                        else Debug.LogWarning("[PcAvatarBuilder] 근육을 찾지 못함: " + name);
                    }
                handler.SetHumanPose(ref curled);

                var delta = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    delta[i] = Quaternion.Inverse(neutral[i]) * tAnim.GetBoneTransform(bones[i]).localRotation;

                handler.SetHumanPose(ref neutralPose);   // 되돌림

                // 2) 실제 아바타의 본 참조 + 쥠 값 저장
                var hand = right ? grip.right : grip.left;
                hand.wrist = anim.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                hand.bones = bones.Select(b => anim.GetBoneTransform(b)).ToArray();
                hand.curl = delta;

                // 3) 손 본 로컬 기준 축: 손가락 방향 / 손바닥 방향 (기본 포즈에서 손바닥이 아래를 향한다고 보고 부호를 정함)
                Transform wrist = hand.wrist;
                Transform mid = anim.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                Transform idx = anim.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                Transform lit = anim.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                Vector3 fW = (mid.position - wrist.position).normalized;
                Vector3 across = (idx.position - lit.position).normalized;
                Vector3 nW = Vector3.Cross(across, fW).normalized;
                if (nW.y > 0f) nW = -nW;
                hand.fingerDirLocal = wrist.InverseTransformDirection(fW);
                hand.palmNormalLocal = wrist.InverseTransformDirection(nW);
                Debug.Log($"[PcAvatarBuilder] {side} 손: 손가락 방향(월드) {fW:F2}, 손바닥 방향(월드) {nW:F2}, 손가락 본 {bones.Length}개 쥠 저장");
            }
        }
        finally { Object.DestroyImmediate(tmp); }
    }

    private static LimbIK MakeLimb(GameObject go, Transform b1, Transform b2, Transform b3, Transform root, AvatarIKGoal goalType, bool goal)
    {
        var ik = go.AddComponent<LimbIK>();
        ik.solver.SetChain(b1, b2, b3, root);
        ik.solver.goal = goalType;
        ik.solver.bendModifier = goal ? IKSolverLimb.BendModifier.Goal : IKSolverLimb.BendModifier.Arm;
        ik.solver.bendModifierWeight = 1f;
        ik.solver.IKPositionWeight = 0f;     // 런타임(PcCrouchIK / PcArmReach)이 매 프레임 설정
        ik.solver.IKRotationWeight = 0f;
        return ik;
    }

    private static Transform MakeBendGoal(Transform root, string name, Transform knee)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        Vector3 k = root.InverseTransformPoint(knee.position);
        t.localPosition = new Vector3(k.x, k.y, k.z + 0.2f);   // 무릎 앞쪽 — 무릎이 앞으로 접히도록
        return t;
    }

    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[PcAvatarBuilder] {target.GetType().Name}.{field} 필드 없음"); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
