#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VRPlayer))]
public class VRPlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VRPlayer player = (VRPlayer)target;

        if (GUILayout.Button("Apply Finger Tracking Transforms (World)"))
        {
            ApplyFingerTransforms(player);
        }
    }

    private void ApplyFingerTransforms(VRPlayer player)
    {
        if (player.trackingSetup == null)
        {
            Debug.LogWarning("TrackingSetup is null!");
            return;
        }

        ApplyHandFingerTransforms(player.trackingSetup.leftHand);
        ApplyHandFingerTransforms(player.trackingSetup.rightHand);

        Debug.Log("Applied finger tracking transforms (world)!");
    }

    private void ApplyHandFingerTransforms(HandData hand)
    {
        if (hand == null) return;

        for (int f = 0; f < hand.fingers.Count; f++)
        {
            FingerData finger = hand.fingers[f];
            for (int i = 0; i < finger.applyBones.Count && i < finger.trackingBones.Count; i++)
            {
                if (finger.applyBones[i] != null && finger.trackingBones[i] != null)
                {
                    Undo.RecordObject(finger.applyBones[i], "Apply Finger Transform");
                    finger.applyBones[i].position = finger.trackingBones[i].position; // 월드 기준 위치
                    finger.applyBones[i].rotation = finger.trackingBones[i].rotation; // 월드 기준 회전
                }
            }
        }
    }
}
#endif
