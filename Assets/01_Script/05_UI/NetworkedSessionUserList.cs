using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Transporting;
using MasterServerToolkit.MasterServer;
using UnityEngine;
using XRAirpotrSecurity;

/// <summary>
/// SessionPlayersListBroadcast 수신 → UserStateRow UI 렌더링
/// 서버 로직은 SessionPlayerServer.cs 가 담당합니다.
/// </summary>
[DisallowMultipleComponent]
public class NetworkedSessionUserList : MonoBehaviour
{
    private const string TemplateChildName = "User-State";

    [SerializeField] private RectTransform userStateTemplate;
    [SerializeField] private bool findTemplateByName = true;

    [Header("프리팹 인스턴스 부모 (Hierarchy에서 직접 지정)")]
    [Tooltip("스폰된 User-State 행이 이 Transform의 자식으로 생성됩니다.")]
    [SerializeField] private Transform rowParent;

    private UserStateRow _templateRow;
    private readonly List<GameObject> _spawnedRows = new();

    // ── 외부 구독용 이벤트 ─────────────────────────────────────
    /// <summary>activePlayerId 가 변경될 때 호출 (clientId, displayName)</summary>
    public static event System.Action<int, string> OnActivePlayerChanged;

    /// <summary>SessionPlayersListBroadcast 수신 시 호출 — NicknameTag 등에서 구독</summary>
    public static event System.Action<SessionPlayersListBroadcast> OnPlayersBroadcastReceived;

    private void Awake()
    {
        if (userStateTemplate == null && findTemplateByName)
        {
            var t = transform.Find(TemplateChildName);
            if (t != null)
                userStateTemplate = t as RectTransform;
        }

        if (userStateTemplate != null)
            _templateRow = userStateTemplate.GetComponent<UserStateRow>();

        if (rowParent == null && userStateTemplate != null)
            rowParent = userStateTemplate.parent;
    }

    private void OnEnable()
    {
        if (InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.RegisterBroadcast<SessionPlayersListBroadcast>(OnPlayersListReceived);

        if (InstanceFinder.ClientManager != null && InstanceFinder.IsClientStarted)
            StartCoroutine(SendLocalSessionInfoWhenReady());

        
    }

    private void OnDisable()
    {
        if (InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.UnregisterBroadcast<SessionPlayersListBroadcast>(OnPlayersListReceived);

        StopAllCoroutines();


    }

    // ── 펌프 하이라이트 ────────────────────────────────────────

    private void OnPumpStarted(int clientId)
    {
        foreach (var go in _spawnedRows)
        {
            if (go == null) continue;
            if (go.TryGetComponent<UserStateRow>(out var row))
                row.UpdateHighlight(clientId); // 해당 플레이어 노란색 ON
        }
    }

    private void OnPumpCompleted(int clientId)
    {
        foreach (var go in _spawnedRows)
        {
            if (go == null) continue;
            if (go.TryGetComponent<UserStateRow>(out var row))
                row.UpdateHighlight(-1); // 전부 꺼짐
        }
    }

    // ── 수신 및 UI ─────────────────────────────────────────

    private void OnPlayersListReceived(SessionPlayersListBroadcast msg, Channel channel)
    {
        var list = msg.players ?? new List<SessionPlayerEntry>();
        Debug.Log($"[UserList][OnPlayersListReceived] players={list.Count} activePlayerId={msg.activePlayerId}");

        RebuildUi(list.OrderBy(e => e.clientId).ToList(), msg.activePlayerId);
        OnPlayersBroadcastReceived?.Invoke(msg);
    }

    //private void RebuildUi(List<SessionPlayerEntry> ordered, int activePlayerId)
    //{
    //    if (userStateTemplate == null)
    //    {
    //        Debug.LogWarning("[NetworkedSessionUserList] userStateTemplate 없음");
    //        return;
    //    }
    //    if (rowParent == null)
    //    {
    //        Debug.LogWarning("[NetworkedSessionUserList] rowParent 없음");
    //        return;
    //    }
    //    if (_templateRow == null)
    //    {
    //        Debug.LogWarning("[NetworkedSessionUserList] UserStateRow 컴포넌트 없음");
    //        return;
    //    }

    //    foreach (var go in _spawnedRows)
    //        if (go != null) Destroy(go);
    //    _spawnedRows.Clear();

    //    foreach (var entry in ordered)
    //    {
    //        var row = Instantiate(userStateTemplate);
    //        row.SetParent(rowParent, false);
    //        row.anchoredPosition = userStateTemplate.anchoredPosition;
    //        row.anchorMax = userStateTemplate.anchorMax;
    //        row.anchorMin = userStateTemplate.anchorMin;
    //        row.pivot = userStateTemplate.pivot;
    //        row.sizeDelta = userStateTemplate.sizeDelta;
    //        row.localScale = userStateTemplate.localScale;
    //        row.anchoredPosition3D = Vector3.zero;
    //        row.gameObject.SetActive(true);
    //        row.SetAsLastSibling();
    //        _spawnedRows.Add(row.gameObject);

    //        if (row.TryGetComponent<UserStateRow>(out var rowComp))
    //            rowComp.Apply(entry.clientId, entry.displayName, entry.platform,
    //                entry.clientId == activePlayerId);
    //    }

    //    var active = ordered.FirstOrDefault(e => e.clientId == activePlayerId);
    //    OnActivePlayerChanged?.Invoke(activePlayerId, active.displayName ?? string.Empty);
    //}

    private void RebuildUi(List<SessionPlayerEntry> ordered, int activePlayerId)
    {
        if(userStateTemplate == null || rowParent == null || _templateRow == null)
        {
            Debug.LogWarning("[NetworkedSessionUserList] 필요한 참조가 누락되었습니다.");
            return;
        }

        // 1. 필요한 개수만큼 생성하거나, 남는 것은 비활성화 (오브젝트 풀링)
        int requiredCount = ordered.Count;
        int currentCount = _spawnedRows.Count;

        // 부족하면 생성
        if(currentCount < requiredCount)
        {
            for(int i = 0; i < requiredCount - currentCount; i++)
            {
                // 부모를 지정하지 않고 생성 후 SetParent 하는 것이 UI 에러 방지에 좋습니다.
                var row = Instantiate(userStateTemplate);
                row.SetParent(rowParent, false);
                _spawnedRows.Add(row.gameObject);
            }
        }

        // 2. 데이터 적용 및 활성화/비활성화
        for(int i = 0; i < _spawnedRows.Count; i++)
        {
            GameObject rowGo = _spawnedRows[i];

            if(i < requiredCount)
            {
                var entry = ordered[i];
                rowGo.SetActive(true);

                // UI 데이터 갱신
                if(rowGo.TryGetComponent<UserStateRow>(out var rowComp))
                {
                    rowComp.Apply(entry.clientId, entry.displayName, entry.platform,
                        entry.clientId == activePlayerId);
                }

                // 레이아웃 순서 보장
                rowGo.transform.SetAsLastSibling();
            }
            else
            {
                // 남는 오브젝트는 파괴하는 대신 비활성화 (에러 방지 핵심)
                rowGo.SetActive(false);
            }
        }

        // 3. 이벤트 발생
        // 닉네임 기본값을 설정해둡니다.
        string activeName = string.Empty;

        // 조건에 맞는 요소를 찾습니다.
        var active = ordered.FirstOrDefault(e => e.clientId == activePlayerId);

        // active가 구조체라면 null 체크 대신 유효한 데이터인지 확인해야 합니다.
        // 보통 clientId가 0이 아니거나, 특정 필드가 비어있지 않은지 확인합니다.
        if(active.clientId == activePlayerId)
        {
            activeName = active.displayName ?? string.Empty;
        }

        OnActivePlayerChanged?.Invoke(activePlayerId, activeName);
    }

    // ── 닉네임 전송 ────────────────────────────────────────

    private IEnumerator SendLocalSessionInfoWhenReady()
    {
        yield return null;
        float timeout = 15f;
        float t = 0f;
        while (t < timeout)
        {
            if (InstanceFinder.ClientManager?.Connection?.IsActive == true)
            {
                string name = ResolveLocalDisplayName();
                if (string.IsNullOrWhiteSpace(name)) name = "Player";

                Debug.Log($"[UserList] 닉네임 전송 name='{name}'");
                InstanceFinder.ClientManager.Broadcast(new ClientSessionInfoBroadcast
                {
                    displayName = name,
                    platform = PlatformToByte(Utils.GetPlatformType())
                });
                yield break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Debug.LogWarning("[UserList] 닉네임 전송 타임아웃");
    }

    private static string ResolveLocalDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(UserInfo.UserName))
            return UserInfo.UserName.Trim();
        if (Mst.Client.Auth.IsSignedIn && Mst.Client.Auth.AccountInfo != null &&
            !string.IsNullOrWhiteSpace(Mst.Client.Auth.AccountInfo.Username))
            return Mst.Client.Auth.AccountInfo.Username.Trim();
        return null;
    }

    private static byte PlatformToByte(PlatformType pt)
    {
        switch (pt)
        {
            case PlatformType.VR: return 0;
            case PlatformType.PC: return 1;
            case PlatformType.Mobile: return 2;
            default: return 1;
        }
    }

    /// <summary>
    /// 외부(ScenarioStateReceiver 등)에서 activePlayerId 변경 이벤트를 강제로 발생시키기 위한 헬퍼
    /// </summary>
    public static void InvokeActivePlayerChanged(int clientId, string displayName)
    {
        OnActivePlayerChanged?.Invoke(clientId, displayName);
    }
}