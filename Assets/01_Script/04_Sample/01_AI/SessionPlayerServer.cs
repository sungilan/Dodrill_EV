using DoDrill;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 데디케이티드 서버 전용 — 씬에 1개 배치
/// 플레이어 접속/닉네임 수신 → SessionPlayersListBroadcast 전체 클라이언트에 송신
/// activePlayerId는 ScenarioRunner.BroadcastState() 안에서 NotifyActivePlayer()로 갱신
/// </summary>
public class SessionPlayerServer : MonoBehaviour
{
    /// <summary>
    /// ScenarioRunner.BroadcastState() 에서 호출.
    /// activePlayerId = -1 이면 진행자 없음.
    /// </summary>
    public static void NotifyActivePlayer(int activePlayerId)
    {
        _pendingActivePlayerId = activePlayerId;
        _activePlayerDirty = true;
    }

    private static int _pendingActivePlayerId = -1;
    private static bool _activePlayerDirty = false;

    private readonly Dictionary<int, SessionPlayerEntry> _players = new();
    private readonly Dictionary<int, Coroutine> _pendingNicknameCoroutines = new();
    private bool _registered;
    private int _activePlayerId = -1;

    private void OnEnable()
    {
        if (InstanceFinder.ServerManager != null && InstanceFinder.IsServerStarted)
            Register();
        else if (InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
    }

    private void OnDisable()
    {
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            if (_registered)
            {
                InstanceFinder.ServerManager.UnregisterBroadcast<ClientSessionInfoBroadcast>(OnClientSessionInfo);
                InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                _registered = false;
            }
        }
        StopAllCoroutines();
    }

    private void Update()
    {
        // static 플래그로 ScenarioRunner → SessionPlayerServer 단방향 통신
        if (!_activePlayerDirty) return;
        _activePlayerDirty = false;

        if (_activePlayerId != _pendingActivePlayerId)
        {
            _activePlayerId = _pendingActivePlayerId;
            Debug.Log($"[SessionPlayerServer] activePlayerId 갱신 → {_activePlayerId}");
            Broadcast();
        }
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started) return;
        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        Register();
    }

    private void Register()
    {
        if (_registered) return;

        Debug.Log("[SessionPlayerServer] 등록");
        InstanceFinder.ServerManager.RegisterBroadcast<ClientSessionInfoBroadcast>(OnClientSessionInfo, requireAuthentication: false);
        InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        _registered = true;

        foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
        {
            if (conn != null && conn.IsValid)
                EnsurePlaceholder(conn.ClientId);
        }

        StartCoroutine(BroadcastNextFrame());
    }

    private IEnumerator BroadcastNextFrame()
    {
        yield return null;
        Broadcast();
    }

    // ── 접속/해제 ──────────────────────────────────────────

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (!conn.IsValid) return;

        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            EnsurePlaceholder(conn.ClientId);
            Broadcast();
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            _pendingNicknameCoroutines.Remove(conn.ClientId);
            _players.Remove(conn.ClientId);
            Broadcast();
        }
    }

    private void EnsurePlaceholder(int clientId)
    {
        if (_players.ContainsKey(clientId)) return;
        _players[clientId] = new SessionPlayerEntry
        {
            clientId = clientId,
            displayName = $"Player {clientId}",
            platform = 1
        };
        _pendingNicknameCoroutines[clientId] = StartCoroutine(NicknameTimeout(clientId));
    }

    private IEnumerator NicknameTimeout(int clientId)
    {
        yield return new WaitForSeconds(8f);
        _pendingNicknameCoroutines.Remove(clientId);
        Broadcast();
    }

    // ── 닉네임 수신 ────────────────────────────────────────

    private void OnClientSessionInfo(NetworkConnection conn, ClientSessionInfoBroadcast msg, Channel channel)
    {
        if (!conn.IsValid) return;
        int id = conn.ClientId;

        string name = msg.displayName;
        if (string.IsNullOrWhiteSpace(name))
            name = $"Player {id}";
        else
        {
            name = name.Trim();
            if (name.Length > 48) name = name.Substring(0, 48);
        }

        _players[id] = new SessionPlayerEntry
        {
            clientId = id,
            displayName = name,
            platform = msg.platform
        };

        if (_pendingNicknameCoroutines.TryGetValue(id, out var co))
        {
            StopCoroutine(co);
            _pendingNicknameCoroutines.Remove(id);
        }

        Debug.Log($"[SessionPlayerServer] 닉네임 등록 clientId={id} name='{name}'");
        Broadcast();
    }

    // ── 브로드캐스트 ───────────────────────────────────────

    private void Broadcast()
    {
        if (!InstanceFinder.IsServerStarted) return;

        var ordered = _players.Keys.OrderBy(k => k).Select(k => _players[k]).ToList();
        Debug.Log($"[SessionPlayerServer] Broadcast players={ordered.Count} activePlayerId={_activePlayerId}");

        InstanceFinder.ServerManager.Broadcast(new SessionPlayersListBroadcast
        {
            players = ordered,
            activePlayerId = _activePlayerId
        }, requireAuthenticated: false);
    }
}
