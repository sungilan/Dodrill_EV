using FishNet.Object;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum FingerName { Thumb, Index, Middle, Ring, Pinky }

[Serializable]
public enum FingerBone { Metacarpal, Proximal, Intermediate, Distal, Tip }

[Serializable]
public class FingerData
{
    public FingerName fingerName;
    [Header("Apply Transforms (Avatar)")]
    public List<Transform> applyBones = new List<Transform>(5);
    [Header("Tracking Transforms (Input)")]
    public List<Transform> trackingBones = new List<Transform>(5);
}

[Serializable]
public class HandData
{
    [Header("Apply Transform")]
    public Transform applyWrist;
    [Header("Tracking Transform")]
    public Transform trackingWrist;
    public List<FingerData> fingers = new List<FingerData>();
}

[Serializable]
public class VRTrackingSetup
{
    [Header("Apply Head")]
    public Transform applyHead;
    [Header("Tracking Head")]
    public Transform trackingHead;
    public HandData leftHand = new HandData();
    public HandData rightHand = new HandData();
}

public class VRPlayer : BasePlayer
{ 
    [Header("VR Tracking Setup")]
    public VRTrackingSetup trackingSetup;
    [Header("Local Tracking Object (Owner Only)")]
    public GameObject[] localTrackingInfos;
    public GameObject applyAvatar;
    public Transform anchor;

    [Header("Sync Interval")]
    [Range(0.01f, 0.1f)]
    public float syncInterval = 0.05f;
    private float syncTimer = 0f;

    [Header("Smoothing")]
    [Range(0.01f, 10f)]
    public float positionLerp = 0.2f;
    [Range(0.01f, 10f)]
    public float rotationLerp = 0.2f;

    private Vector3 targetHeadPos;
    private Quaternion targetHeadRot;
    private Vector3 targetLeftWristPos;
    private Quaternion targetLeftWristRot;
    private Vector3 targetRightWristPos;
    private Quaternion targetRightWristRot;
    private Quaternion[] targetLeftFingerRots;
    private Quaternion[] targetRightFingerRots;
    private Vector3[] targetLeftFingerPoss;
    private Vector3[] targetRightFingerPoss;

    public override void OnStartClient()
    {
        anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;

        if (IsOwner)
        {
            Destroy(applyAvatar);
            foreach (var go in localTrackingInfos) go.SetActive(true);
        }
        else
        {
            foreach (var go in localTrackingInfos)
            {
                applyAvatar.SetActive(true);
                Destroy(go);
            }
        }
    }

    private void Start()
    {
        anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
    }

    private void Update()
    {
        if (IsServerInitialized == true)
            return;
        if (!IsOwner)
        {
            if (trackingSetup.applyHead != null)
            {
                Vector3 worldHeadPos = anchor.position + anchor.rotation * targetHeadPos;
                Quaternion worldHeadRot = anchor.rotation * targetHeadRot;

                trackingSetup.applyHead.position = Vector3.Lerp(trackingSetup.applyHead.position, worldHeadPos, positionLerp);
                trackingSetup.applyHead.rotation = Quaternion.Slerp(trackingSetup.applyHead.rotation, worldHeadRot, rotationLerp);
            }

            ApplyHandSmooth(trackingSetup.leftHand, targetLeftWristPos, targetLeftWristRot, targetLeftFingerPoss, targetLeftFingerRots);
            ApplyHandSmooth(trackingSetup.rightHand, targetRightWristPos, targetRightWristRot, targetRightFingerPoss, targetRightFingerRots);
            return;
        }

        syncTimer += Time.deltaTime;
        if (syncTimer >= syncInterval)
        {
            syncTimer = 0f;
            SendTrackingData();
        }
    }

    public void SendTrackingData()
    {
        if (!IsOwner) return;
        Transform anchorT = anchor != null ? anchor : transform;

        // Head
        Vector3 headPos = trackingSetup.trackingHead ? Quaternion.Inverse(anchorT.rotation) * (trackingSetup.trackingHead.position - anchorT.position) : Vector3.zero;
        Quaternion headRot = trackingSetup.trackingHead ? Quaternion.Inverse(anchorT.rotation) * trackingSetup.trackingHead.rotation : Quaternion.identity;

        // Left Hand
        Vector3 leftWristPos = trackingSetup.leftHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * (trackingSetup.leftHand.trackingWrist.position - anchorT.position) : Vector3.zero;
        Quaternion leftWristRot = trackingSetup.leftHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * trackingSetup.leftHand.trackingWrist.rotation : Quaternion.identity;
        Quaternion[] leftFingerRots = GetFingerLocalRotations(trackingSetup.leftHand, anchorT);
        Vector3[] leftFingerPoss = GetFingerLocalPositions(trackingSetup.leftHand, anchorT);

        // Right Hand
        Vector3 rightWristPos = trackingSetup.rightHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * (trackingSetup.rightHand.trackingWrist.position - anchorT.position) : Vector3.zero;
        Quaternion rightWristRot = trackingSetup.rightHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * trackingSetup.rightHand.trackingWrist.rotation : Quaternion.identity;
        Quaternion[] rightFingerRots = GetFingerLocalRotations(trackingSetup.rightHand, anchorT);
        Vector3[] rightFingerPoss = GetFingerLocalPositions(trackingSetup.rightHand, anchorT);

        ApplyTrackingToServer(headPos, headRot, leftWristPos, leftWristRot, leftFingerPoss, leftFingerRots,
                              rightWristPos, rightWristRot, rightFingerPoss, rightFingerRots);
    }

    private Vector3[] GetFingerLocalPositions(HandData hand, Transform anchorT)
    {
        List<Vector3> poss = new List<Vector3>();
        foreach (var finger in hand.fingers)
            for (int i = 0; i < finger.trackingBones.Count; i++)
                poss.Add(finger.trackingBones[i] != null ? Quaternion.Inverse(anchorT.rotation) * (finger.trackingBones[i].position - anchorT.position) : Vector3.zero);
        return poss.ToArray();
    }

    private Quaternion[] GetFingerLocalRotations(HandData hand, Transform anchorT)
    {
        List<Quaternion> rots = new List<Quaternion>();
        foreach (var finger in hand.fingers)
            for (int i = 0; i < finger.trackingBones.Count; i++)
                rots.Add(finger.trackingBones[i] != null ? Quaternion.Inverse(anchorT.rotation) * finger.trackingBones[i].rotation : Quaternion.identity);
        return rots.ToArray();
    }

    [ServerRpc(RequireOwnership = true)]
    private void ApplyTrackingToServer(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Vector3[] leftFingerPoss, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Vector3[] rightFingerPoss, Quaternion[] rightFingerRots)
    {
        ApplyTrackingToClients(headPos, headRot, leftWristPos, leftWristRot, leftFingerPoss, leftFingerRots,
                               rightWristPos, rightWristRot, rightFingerPoss, rightFingerRots);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ApplyTrackingToClients(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Vector3[] leftFingerPoss, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Vector3[] rightFingerPoss, Quaternion[] rightFingerRots)
    {
        if (anchor == null)
        {
            anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
            return;
        }
        ApplyTrackingData(headPos, headRot, leftWristPos, leftWristRot, leftFingerPoss, leftFingerRots,
                          rightWristPos, rightWristRot, rightFingerPoss, rightFingerRots);
    }

    private void ApplyTrackingData(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Vector3[] leftFingerPoss, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Vector3[] rightFingerPoss, Quaternion[] rightFingerRots)
    {
        targetHeadPos = headPos;
        targetHeadRot = headRot;
        targetLeftWristPos = leftWristPos;
        targetLeftWristRot = leftWristRot;
        targetRightWristPos = rightWristPos;
        targetRightWristRot = rightWristRot;
        targetLeftFingerPoss = leftFingerPoss;
        targetLeftFingerRots = leftFingerRots;
        targetRightFingerPoss = rightFingerPoss;
        targetRightFingerRots = rightFingerRots;
    }

    private void ApplyHandSmooth(HandData hand, Vector3 wristPos, Quaternion wristRot, Vector3[] fingerPoss, Quaternion[] fingerRots)
    {
        if (anchor == null)
        {
            anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
            return;
        }

        if (hand.applyWrist != null)
        {
            Vector3 targetWristPos = anchor.position + anchor.rotation * wristPos;
            Quaternion targetWristRot = anchor.rotation * wristRot;

            hand.applyWrist.position = Vector3.Lerp(hand.applyWrist.position, targetWristPos, positionLerp);
            hand.applyWrist.rotation = Quaternion.Slerp(hand.applyWrist.rotation, targetWristRot, rotationLerp);
        }

        int rotIdx = 0;
        for (int f = 0; f < hand.fingers.Count; f++)
        {
            var finger = hand.fingers[f];
            for (int i = 0; i < finger.applyBones.Count; i++)
            {
                if (finger.applyBones[i] != null && fingerRots != null && rotIdx < fingerRots.Length && fingerPoss != null && rotIdx < fingerPoss.Length)
                {
                    Vector3 targetFingerPos = anchor.position + anchor.rotation * fingerPoss[rotIdx];
                    Quaternion targetFingerRot = anchor.rotation * fingerRots[rotIdx];

                    finger.applyBones[i].position = Vector3.Lerp(finger.applyBones[i].position, targetFingerPos, positionLerp);
                    finger.applyBones[i].rotation = Quaternion.Slerp(finger.applyBones[i].rotation, targetFingerRot, rotationLerp);
                }
                rotIdx++;
            }
        }
    }
}
