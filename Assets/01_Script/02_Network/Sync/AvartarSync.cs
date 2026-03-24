using AYellowpaper.SerializedCollections;
using FishNet.Object;
using RootMotion.FinalIK;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public enum FingerType
{
    LeftThumb,
    LeftIndex,
    LeftMiddle,
    LeftRing,
    LeftPinky,
    RightThumb,
    RightIndex,
    RightMiddle,
    RightRing,
    RightPinky
}

public class AvatarSync : NetworkBehaviour
{
    [SerializeField]
    [Range(0.005f, 0.1f)]
    private float syncInterval = 0.01f;

    // 소유자가 보낼 헤드/손
    [SerializeField] private Transform syncTargetHead;
    [SerializeField] private Transform syncTargetLeftHand;
    [SerializeField] private Transform syncTargetRightHand;

    // 타인이 위치를 적용할 헤드/손
    [SerializeField] private Transform applyTargetHead;
    [SerializeField] private Transform applyTargetLeftHand;
    [SerializeField] private Transform applyTargetRightHand;

    // 손가락도 같은 방식
    [SerializeField] private SerializedDictionary<FingerType, Transform> syncTargetFingers;
    [SerializeField] private SerializedDictionary<FingerType, Transform> applyTargetFingers;
    [SerializeField] private List<Transform> fingerTargetTransforms;
    [SerializeField] private VRIK vrik;
    [SerializeField] private FingerRig fingerIk;

    // 보간용 변수
    private Vector3 targetHeadPos, targetLeftHandPos, targetRightHandPos;
    private Quaternion targetHeadRot, targetLeftHandRot, targetRightHandRot;
    private List<Vector3> targetFingerPositions = new List<Vector3>();
    private float smoothTime = 0.1f; // 보간 속도 조절

    private Coroutine syncCoroutine;

    public void OnEnable()
    {
        if (IsOwner)
        {
            syncCoroutine = StartCoroutine(SyncAvatarTransform());
        }
        else
        {
            applyTargetHead = new GameObject("ApplyHead").transform;
            applyTargetHead.SetParent(transform);
            vrik.solver.spine.headTarget = applyTargetHead;
            applyTargetLeftHand = new GameObject("ApplyLeftHand").transform;
            applyTargetLeftHand.SetParent(transform);
            vrik.solver.leftArm.target = applyTargetLeftHand;
            applyTargetRightHand = new GameObject("ApplyRightHand").transform;
            applyTargetRightHand.SetParent(transform);
            vrik.solver.rightArm.target = applyTargetRightHand;
            int index = 0;
            fingerTargetTransforms.Clear();
            foreach (var targetFinger in applyTargetFingers.Values)
            {
                string renamed = Regex.Replace(targetFinger.name, @"\d+$", "") + "_Target";
                GameObject go = new GameObject(renamed);
                go.transform.SetParent(targetFinger);
                go.transform.localPosition = Vector3.zero;
                fingerTargetTransforms.Add(go.transform);
                fingerIk.fingers[index].target = go.transform;
                index++;
            }
            // 손가락 목표 위치 리스트 초기화
            targetFingerPositions.Clear();
            for (int i = 0; i < fingerTargetTransforms.Count; i++)
            {
                targetFingerPositions.Add(fingerTargetTransforms[i].position);
            }
        }
    }

    public override void OnStopClient()
    {
        if (syncCoroutine != null)
            StopCoroutine(syncCoroutine);
    }

    private void Update()
    {
        if (!IsOwner)
        {
            // 헤드/손 위치 보간
            applyTargetHead.position = Vector3.Lerp(applyTargetHead.position, targetHeadPos, Time.deltaTime / smoothTime);
            applyTargetLeftHand.position = Vector3.Lerp(applyTargetLeftHand.position, targetLeftHandPos, Time.deltaTime / smoothTime);
            applyTargetRightHand.position = Vector3.Lerp(applyTargetRightHand.position, targetRightHandPos, Time.deltaTime / smoothTime);

            // 헤드/손 회전 보간
            applyTargetHead.rotation = Quaternion.Slerp(applyTargetHead.rotation, targetHeadRot, Time.deltaTime / smoothTime);
            applyTargetLeftHand.rotation = Quaternion.Slerp(applyTargetLeftHand.rotation, targetLeftHandRot, Time.deltaTime / smoothTime);
            applyTargetRightHand.rotation = Quaternion.Slerp(applyTargetRightHand.rotation, targetRightHandRot, Time.deltaTime / smoothTime);

            // 손가락 위치 보간
            for (int i = 0; i < fingerTargetTransforms.Count && i < targetFingerPositions.Count; i++)
            {
                fingerTargetTransforms[i].position = Vector3.Lerp(fingerTargetTransforms[i].position, targetFingerPositions[i], Time.deltaTime / smoothTime);
            }
        }
    }

    IEnumerator SyncAvatarTransform()
    {
        while (true)
        {
            var fingerPositions = GetFingerPositionsToSync();

            SyncTransformReq(
                syncTargetHead.position, syncTargetHead.rotation,
                syncTargetLeftHand.position, syncTargetLeftHand.rotation,
                syncTargetRightHand.position, syncTargetRightHand.rotation,
                fingerPositions
            );

            yield return new WaitForSeconds(syncInterval);
        }
    }

    private List<FingerPositionData> GetFingerPositionsToSync()
    {
        List<FingerPositionData> list = new List<FingerPositionData>();
        foreach (var finger in syncTargetFingers)
        {
            list.Add(new FingerPositionData
            {
                position = finger.Value.position
            });
        }
        return list;
    }

    [ServerRpc(RequireOwnership = true)]
    private void SyncTransformReq(Vector3 headPos,
                                  Quaternion headRot,
                                  Vector3 leftPos,
                                  Quaternion leftRot,
                                  Vector3 rightPos,
                                  Quaternion rightRot,
                                  List<FingerPositionData> fingerList)
    {
        SyncTransformAck(headPos, headRot, leftPos, leftRot, rightPos, rightRot, fingerList);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void SyncTransformAck(Vector3 headPos,
                                  Quaternion headRot,
                                  Vector3 leftPos,
                                  Quaternion leftRot,
                                  Vector3 rightPos,
                                  Quaternion rightRot,
                                  List<FingerPositionData> fingerList)
    {
        targetHeadPos = headPos;
        targetHeadRot = headRot;
        targetLeftHandPos = leftPos;
        targetLeftHandRot = leftRot;
        targetRightHandPos = rightPos;
        targetRightHandRot = rightRot;

        // 손가락 목표 위치 갱신
        if (targetFingerPositions.Count != fingerList.Count)
        {
            targetFingerPositions.Clear();
            for (int i = 0; i < fingerList.Count; i++)
                targetFingerPositions.Add(fingerList[i].position);
        }
        else
        {
            for (int i = 0; i < fingerList.Count; i++)
                targetFingerPositions[i] = fingerList[i].position;
        }
    }
}

public struct FingerPositionData
{
    public Vector3 position;
}