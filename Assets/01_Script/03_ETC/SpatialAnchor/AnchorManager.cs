using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.XR.CoreUtils.Collections;
using UnityEngine.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

public class AnchorManager : MonoBehaviour
{
    private ARAnchorManager anchorManager;
    private List<XRAnchor> anchorList = new();
    private TrackableId currentTrackableId;

#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
    public ARAnchor currentAnchor; // 최신 앵커만 추적
#endif

    private void OnEnable() => ARSession.stateChanged += OnARSessionStateChanged;
    private void OnDisable() => ARSession.stateChanged -= OnARSessionStateChanged;

    private void Awake()
    {
        anchorManager = GetComponent<ARAnchorManager>();
        anchorManager.trackablesChanged.AddListener(OnAnchorsChanged);
    }
    public Pose GetAnchorPose()
    {
        Pose pose = new Pose(); 
        foreach(var anchor in anchorList)
        {
            pose = anchor.pose;
        }
        return pose;
    }

    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        Debug.Log($"ARSession.state changed: {args.state}");
    
        if (args.state == ARSessionState.SessionTracking)
        {
            ARSession.stateChanged -= OnARSessionStateChanged;
        }
    }


    // 앵커 생성 & 공유
    public async void CreateStartupAnchor(Pose position, Action<string> successCallback)
    {
        Debug.Log("ds");
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
        SetSharedAnchorsGroupIdSafe();
        Debug.Log("ds2");
        // 기존 앵커 있으면 삭제
        if (currentAnchor != null)
        {
            Destroy(currentAnchor.gameObject);
            currentAnchor = null;
        }

        var anchor = await PlaceAnchorAsync(position.position, position.rotation);

        if (anchor != null)
        {
            currentAnchor = anchor;
            Debug.Log("앵커 준비 완료!");
            //ShareAnchorsAsync(anchorManager, anchors);
            ShareAnchorAsync(anchor, successCallback);
            UserInfo.anchorSetted = true;
        }
        else
            UserInfo.anchorSetted = false;
#endif
    }

#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
    private void SetSharedAnchorsGroupIdSafe()
    {
        if (anchorManager?.subsystem is MetaOpenXRAnchorSubsystem meta)
        {
            // 항상 새 GUID 발급
            meta.sharedAnchorsGroupId = new SerializableGuid(Guid.NewGuid());
            UserInfo.anchorGUID = meta.sharedAnchorsGroupId.guid.ToString();
            Debug.Log("Shared Anchors Group ID 새로 생성 완료: " + meta.sharedAnchorsGroupId);
        }
        else
        {
            Debug.LogWarning("MetaOpenXRAnchorSubsystem 준비 안 됨");
        }
    }
#endif
    public void SetAnchorGroupGuid(string newGuid)
    {
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
        if (anchorManager?.subsystem is MetaOpenXRAnchorSubsystem meta)
        {
            Guid guid = Guid.Parse(newGuid);
            var groupId = new SerializableGuid(guid);
            meta.sharedAnchorsGroupId = groupId;
            Debug.Log("Shared Anchors Group ID 호스트 Guid로 동기화 완료 : " + meta.sharedAnchorsGroupId);
            Debug.Log("running state : " + meta.running);
        }
#endif
    }
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
    public async Task<ARAnchor> PlaceAnchorAsync(Vector3 position, Quaternion rotation)
    {
        Pose pose = new(position, rotation);
        try
        {
            var result = await anchorManager.TryAddAnchorAsync(pose);

            if (result.status.IsSuccess())
            {
                Debug.Log("앵커 생성 성공: " + result.value.trackableId);
                return result.value;
            }
            else
            {
                Debug.LogError("앵커 생성 실패: " + result.status);
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return null;
        }
    }

    private async void ShareAnchorAsync(ARAnchor anchor, Action<string> successCallback)
    {
        var resultStatus = await anchorManager.TryShareAnchorAsync(anchor);

        if (resultStatus.IsError())
        {
            Debug.LogError("앵커 공유 실패");
            successCallback?.Invoke("앵커 공유 실패");
        }
        else
        {
            Debug.Log($"앵커 공유 성공 : {anchor.name}");
            successCallback?.Invoke("앵커 공유 성공");
        }
    }

    async void ShareAnchorsAsync(
      ARAnchorManager anchorManager, IEnumerable<ARAnchor> anchors)
    {
        var results = new List<XRShareAnchorResult>();
        await anchorManager.TryShareAnchorsAsync(anchors, results);
        foreach (var result in results)
        {
            if (result.resultStatus.IsSuccess())
            {
                // Anchor with results.anchorId was successfully shared.
            }
            else
            {
                // Anchor with results.anchorId failed to share.
            }
        }
    }

#endif 


    public void LoadAllAnchors(Action<List<XRAnchor>, ARAnchor> loadFinishCallback)
    {
        LoadAllSharedAnchorsAsync(anchorManager, loadFinishCallback);
    }

    private async void LoadAllSharedAnchorsAsync(ARAnchorManager anchorManager, Action<List<XRAnchor>, ARAnchor> loadFinishCallback)
    {
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
        var loadedXRAnchors = new List<XRAnchor>();
    
        if (anchorManager?.subsystem is MetaOpenXRAnchorSubsystem meta)
        {
            Debug.Log($"Supported is {meta.isSharedAnchorsSupported}");
        }

        var resultStatus = await anchorManager.TryLoadAllSharedAnchorsAsync(
            loadedXRAnchors, OnIncrementalResultsAvailable);
        if (resultStatus.IsError())
        {
            // Handle error here.
            return;
        }
        if (resultStatus.IsSuccess())
        {
            var result = resultStatus.AsXrResult();
            Debug.Log(result.ToString());
        }
        loadFinishCallback?.Invoke(loadedXRAnchors, currentAnchor);
        // Request completed successfully.
#endif
    }
    private void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARAnchor> args)
    {
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
        foreach (var added in args.added)
        {
            currentAnchor = added;
        }
#endif
    }
    private void OnIncrementalResultsAvailable(ReadOnlyListSpan<XRAnchor> xrAnchors)
    {
#if !UNITY_SERVER && (UNITY_ANDROID || UNITY_EDITOR) && (ENABLE_VR || ENABLE_OPENXR)
        foreach (var xrAnchor in xrAnchors)
        {
            var anchorId = xrAnchor.trackableId;
        }
#endif
    }
}