using Autohand;
using UnityEditor;

/// <summary>
/// Auto Hand 잡기 포즈(GrabbablePose 등)에서 한쪽 손 포즈만 지우는 메뉴.
/// 기본 'Clear Saved Poses' 는 왼손·오른손을 모두 지운다.
/// 사용: 컴포넌트 제목줄 우클릭(또는 ⋮ 메뉴) → "왼손 포즈만 지우기" / "오른손 포즈만 지우기". Ctrl+Z 로 되돌릴 수 있다.
/// </summary>
public static class HandPoseClearMenu
{
    [MenuItem("CONTEXT/HandPoseDataContainer/왼손 포즈만 지우기")]
    private static void ClearLeft(MenuCommand cmd) => Clear(cmd.context as HandPoseDataContainer, left: true);

    [MenuItem("CONTEXT/HandPoseDataContainer/오른손 포즈만 지우기")]
    private static void ClearRight(MenuCommand cmd) => Clear(cmd.context as HandPoseDataContainer, left: false);

    [MenuItem("CONTEXT/HandPoseDataContainer/왼손 포즈만 지우기", true)]
    private static bool CanClearLeft(MenuCommand cmd) => cmd.context is HandPoseDataContainer c && c.leftPoseSet;

    [MenuItem("CONTEXT/HandPoseDataContainer/오른손 포즈만 지우기", true)]
    private static bool CanClearRight(MenuCommand cmd) => cmd.context is HandPoseDataContainer c && c.rightPoseSet;

    private static void Clear(HandPoseDataContainer c, bool left)
    {
        if (c == null) return;
        Undo.RecordObject(c, left ? "왼손 포즈 지우기" : "오른손 포즈 지우기");
        if (left) { c.leftPoseSet = false; c.leftPose = new HandPoseData(); }
        else { c.rightPoseSet = false; c.rightPose = new HandPoseData(); }
        EditorUtility.SetDirty(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);

        if (c.poseScriptable != null)
            UnityEngine.Debug.LogWarning($"[HandPose] {c.name}: Pose Scriptable 이 연결되어 있어 그쪽에 저장된 {(left ? "왼손" : "오른손")} 포즈가 계속 쓰일 수 있습니다. 필요하면 Pose Scriptable 연결을 비우세요.", c);
    }
}
