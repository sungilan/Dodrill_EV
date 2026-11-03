using FishNet.Object;
using RootMotion.FinalIK;
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

    // 손가락은 관절별 로컬 회전만 받는다 (A-2). 위치는 아바타 뼈 길이가 결정하므로 건드리지 않는다.
    [Header("손가락 보간")]
    [SerializeField] private float fingerFollowSpeed = 18f;
    private Quaternion[] _leftFingerPose;
    private Quaternion[] _rightFingerPose;

    // 머리·손목 보간 계수(positionLerp/rotationLerp)는 프레임당 고정값이라 30fps 모바일과 72~90Hz Quest 의 수렴 속도가 2~3배 달랐다(A-5).
    // 인스펙터 값을 '60fps 기준 프레임당 계수'로 보고 실제 dt 에 맞게 환산한다. (손가락은 fingerFollowSpeed 로 이미 시간 기반)
    private static float Blend(float perFrame60)
    {
        return Mathf.Clamp01(1f - Mathf.Pow(1f - Mathf.Clamp01(perFrame60), Time.deltaTime * 60f));
    }

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
            // Anchor 가 아직 없으면(씬 로드 순서) 재탐색만 하고 건너뜀 — 예전에는 여기서 NullReferenceException
            if (anchor == null)
            {
                anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
                if (anchor == null) return;
            }

            if (trackingSetup.applyHead != null)
            {
                Vector3 worldHeadPos = anchor.position + anchor.rotation * targetHeadPos;
                Quaternion worldHeadRot = anchor.rotation * targetHeadRot;

                trackingSetup.applyHead.position = Vector3.Lerp(trackingSetup.applyHead.position, worldHeadPos, Blend(positionLerp));
                trackingSetup.applyHead.rotation = Quaternion.Slerp(trackingSetup.applyHead.rotation, worldHeadRot, Blend(rotationLerp));

                FollowAvatarRoot(trackingSetup.applyHead.position, trackingSetup.applyHead.rotation);
            }

            ApplyWristSmooth(trackingSetup.leftHand, targetLeftWristPos, targetLeftWristRot);
            ApplyWristSmooth(trackingSetup.rightHand, targetRightWristPos, targetRightWristRot);
            return;
        }

        syncTimer += Time.deltaTime;
        if (syncTimer >= syncInterval)
        {
            syncTimer = 0f;
            SendTrackingData();
        }
    }

    // Animator 가 Update 뒤에 손가락을 덮어쓸 수 있으므로 LateUpdate 에서 적용한다.
    // 보간 상태를 뼈가 아닌 별도 배열에 두어야 Animator 가 매 프레임 되돌려도 목표에 수렴한다.
    private void LateUpdate()
    {
        if (IsServerInitialized == true || IsOwner) return;

        ApplyFingers(trackingSetup.leftHand, targetLeftFingerRots, ref _leftFingerPose);
        ApplyFingers(trackingSetup.rightHand, targetRightFingerRots, ref _rightFingerPose);
    }

    // ── 원격 아바타 몸통 추종 (A-1) ─────────────────────────────
    // VR_Player 는 루트를 동기화하지 않고 머리·손 목표만 보낸다. 아바타 루트가 스폰 지점에 고정된 채
    // 목표만 멀어지면 VRIK 이 척추·팔을 억지로 늘리거나 꺾어 기괴한 자세가 된다.
    // → 아바타 루트(VRIK.references.root)를 머리의 수평 위치·좌우 방향으로 따라가게 하면
    //   VRIK 의 보행(locomotion) 이 발을 옮기며 자연스럽게 서 있는다.
    [Header("원격 아바타 몸통 추종")]
    [SerializeField] private bool followAvatarRoot = true;
    [SerializeField] private float rootFollowSpeed = 10f;
    [SerializeField] private float rootTurnSpeed = 6f;
    private Transform _avatarRoot;
    private float _avatarFloorY;

    private void FollowAvatarRoot(Vector3 headPos, Quaternion headRot)
    {
        if (!followAvatarRoot || applyAvatar == null) return;

        if (_avatarRoot == null)
        {
            var vrik = applyAvatar.GetComponentInChildren<VRIK>(true);
            if (vrik == null) { followAvatarRoot = false; return; }
            _avatarRoot = vrik.references.root != null ? vrik.references.root : vrik.transform;
            _avatarFloorY = _avatarRoot.position.y;     // 처음 위치의 높이 = 바닥
        }

        float dt = Time.deltaTime;
        Vector3 target = new Vector3(headPos.x, _avatarFloorY, headPos.z);
        _avatarRoot.position = Vector3.Lerp(_avatarRoot.position, target, 1f - Mathf.Exp(-rootFollowSpeed * dt));

        // 위/아래를 보고 있으면 forward 의 수평 성분이 작아 방향이 튀므로 충분히 클 때만 회전
        Vector3 fwd = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
        if (fwd.sqrMagnitude > 0.05f)
        {
            Quaternion want = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            _avatarRoot.rotation = Quaternion.Slerp(_avatarRoot.rotation, want, 1f - Mathf.Exp(-rootTurnSpeed * dt));
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
        Quaternion[] leftFingerRots = GetFingerLocalRotations(trackingSetup.leftHand);

        // Right Hand
        Vector3 rightWristPos = trackingSetup.rightHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * (trackingSetup.rightHand.trackingWrist.position - anchorT.position) : Vector3.zero;
        Quaternion rightWristRot = trackingSetup.rightHand.trackingWrist ? Quaternion.Inverse(anchorT.rotation) * trackingSetup.rightHand.trackingWrist.rotation : Quaternion.identity;
        Quaternion[] rightFingerRots = GetFingerLocalRotations(trackingSetup.rightHand);

        ApplyTrackingToServer(headPos, headRot, leftWristPos, leftWristRot, leftFingerRots,
                              rightWristPos, rightWristRot, rightFingerRots);
    }

    private Quaternion[] GetFingerLocalRotations(HandData hand)
    {
        List<Quaternion> rots = new List<Quaternion>();
        foreach (var finger in hand.fingers)
            for (int i = 0; i < finger.trackingBones.Count; i++)
                rots.Add(finger.trackingBones[i] != null ? finger.trackingBones[i].localRotation : Quaternion.identity);
        return rots.ToArray();
    }

    [ServerRpc(RequireOwnership = true)]
    private void ApplyTrackingToServer(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Quaternion[] rightFingerRots)
    {
        ApplyTrackingToClients(headPos, headRot, leftWristPos, leftWristRot, leftFingerRots,
                               rightWristPos, rightWristRot, rightFingerRots);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ApplyTrackingToClients(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Quaternion[] rightFingerRots)
    {
        if (anchor == null)
        {
            anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
            return;
        }
        ApplyTrackingData(headPos, headRot, leftWristPos, leftWristRot, leftFingerRots,
                          rightWristPos, rightWristRot, rightFingerRots);
    }

    private void ApplyTrackingData(
        Vector3 headPos, Quaternion headRot,
        Vector3 leftWristPos, Quaternion leftWristRot, Quaternion[] leftFingerRots,
        Vector3 rightWristPos, Quaternion rightWristRot, Quaternion[] rightFingerRots)
    {
        targetHeadPos = headPos;
        targetHeadRot = headRot;
        targetLeftWristPos = leftWristPos;
        targetLeftWristRot = leftWristRot;
        targetRightWristPos = rightWristPos;
        targetRightWristRot = rightWristRot;
        targetLeftFingerRots = leftFingerRots;
        targetRightFingerRots = rightFingerRots;
    }

    private void ApplyWristSmooth(HandData hand, Vector3 wristPos, Quaternion wristRot)
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

            hand.applyWrist.position = Vector3.Lerp(hand.applyWrist.position, targetWristPos, Blend(positionLerp));
            hand.applyWrist.rotation = Quaternion.Slerp(hand.applyWrist.rotation, targetWristRot, Blend(rotationLerp));
        }
    }

    private void ApplyFingers(HandData hand, Quaternion[] targets, ref Quaternion[] pose)
    {
        if (targets == null) return;
        if (pose == null || pose.Length != targets.Length) pose = (Quaternion[])targets.Clone();

        float t = 1f - Mathf.Exp(-fingerFollowSpeed * Time.deltaTime);
        int rotIdx = 0;
        for (int f = 0; f < hand.fingers.Count; f++)
        {
            var finger = hand.fingers[f];
            for (int i = 0; i < finger.applyBones.Count; i++)
            {
                if (finger.applyBones[i] != null && rotIdx < targets.Length)
                {
                    pose[rotIdx] = Quaternion.Slerp(pose[rotIdx], targets[rotIdx], t);
                    finger.applyBones[i].localRotation = pose[rotIdx];
                }
                rotIdx++;
            }
        }
    }
}
