using FishNet.Object;
using MasterServerToolkit.MasterServer;
using UnityEngine;

/// <summary>
/// <see cref="Loading_BG"/> 루트를 외부 이벤트에 맞춰 켜고 끕니다.
/// 플레이어 프리팹 UI마다 붙이고 <see cref="loadingBgRoot"/>만 각자 연결하면 되며,
/// <see cref="onlyDriveHudForOwner"/>로 다른 클라이언트용 복제 프리팹 HUD는 로딩을 켜지 않게 할 수 있습니다.
/// </summary>
public class LoadingBgVisibilityBinder : MonoBehaviour
{
    [Tooltip("비우면 Client에 둔 PersistentClientLoadingRoot(Loading_BG)를 자동 사용. 플레이어 프리팹에는 비워 두고 이 컴포넌트만 붙여도 됨.")]
    [SerializeField] private GameObject loadingBgRoot;

    [Header("플레이어 프리팹 (FishNet)")]
    [Tooltip("켜면: 부모(또는 ownerScope)의 NetworkObject가 IsOwner일 때만 로딩 표시. 꺼면 씬에 하나뿐일 때처럼 항상 반응.")]
    [SerializeField] private bool onlyDriveHudForOwner = true;
    [Tooltip("비우면 GetComponentInParent<NetworkObject> — UI가 플레이어 루트 밑에 있으면 자동.")]
    [SerializeField] private NetworkObject ownerScope;

    [Header("게임 씬: LayoutClientLoader HTTP 맵")]
    [SerializeField] private bool reactToLayoutHttpLoad = true;

    [Header("로비 등: MST 룸·씬 로딩")]
    [SerializeField] private bool reactToMstLoadingInfo;

    private NetworkObject ResolveOwnerScope()
    {
        if (ownerScope != null)
            return ownerScope;
        return GetComponentInParent<NetworkObject>(true);
    }

    private bool MayShowLoadingForThisHud()
    {
        if (!onlyDriveHudForOwner)
            return true;
        var nob = ResolveOwnerScope();
        if (nob == null)
            return true;
        return nob.IsOwner;
    }

    private void OnEnable()
    {
        if (loadingBgRoot == null)
        {
            var persistent = PersistentClientLoadingRoot.GetInstanceIncludeInactive();
            if (persistent != null)
                loadingBgRoot = persistent.gameObject;
        }

        if (reactToLayoutHttpLoad && loadingBgRoot == null)
            Debug.LogWarning(
                "[LoadingBgVisibilityBinder] Layout 로딩 UI를 켤 대상이 없습니다. " +
                "Client 씬의 Loading_BG에 PersistentClientLoadingRoot을 붙이거나, 이 필드에 Loading_BG를 직접 넣으세요.");

        if (reactToLayoutHttpLoad)
        {
            //LayoutClientLoaderEvents.OnLoadStarted += OnLayoutLoadStarted;
            //LayoutClientLoaderEvents.OnLoadFinished += OnLayoutLoadFinished;
        }

        // 맵 로드가 플레이어 스폰보다 먼저 끝나면 OnLoadStarted를 놓침 → 진행 중이면 바로 켬
        //if (reactToLayoutHttpLoad && LayoutClientLoaderEvents.IsLoadInProgress && MayShowLoadingForThisHud())
            //SetLoadingVisible(true);

        if (reactToMstLoadingInfo)
        {
            Mst.Events.AddListener(MstEventKeys.showLoadingInfo, OnMstShowLoading);
            Mst.Events.AddListener(MstEventKeys.hideLoadingInfo, OnMstHideLoading);
        }
    }

    private void OnDisable()
    {
        //LayoutClientLoaderEvents.OnLoadStarted -= OnLayoutLoadStarted;
        //LayoutClientLoaderEvents.OnLoadFinished -= OnLayoutLoadFinished;
        Mst.Events.RemoveListener(MstEventKeys.showLoadingInfo, OnMstShowLoading);
        Mst.Events.RemoveListener(MstEventKeys.hideLoadingInfo, OnMstHideLoading);
    }

    private void OnLayoutLoadStarted()
    {
        if (!MayShowLoadingForThisHud())
            return;
        SetLoadingVisible(true);
    }

    private void OnLayoutLoadFinished(bool _)
    {
        SetLoadingVisible(false);
        // Notify는 TrainingSessionLoadingFlow가 LayoutClientLoaderEvents에 전역 구독하여 처리
    }

    private void OnMstShowLoading(EventMessage _)
    {
        if (!MayShowLoadingForThisHud())
            return;
        SetLoadingVisible(true);
    }

    private void OnMstHideLoading(EventMessage _)
    {
        //if (TrainingSessionLoadingFlow.BlockMstHideUntilMapResolved)
            //return;
        SetLoadingVisible(false);
    }

    private void SetLoadingVisible(bool visible)
    {
        if (loadingBgRoot == null) return;
        loadingBgRoot.SetActive(visible);
    }
}
