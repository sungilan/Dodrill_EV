using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  DebugLogger.cs
//  디버그 커맨드 호출 기록 + 전체 클라이언트 로그 동기화
//
//  기록 항목:
//    - 호출자 클라이언트 ID
//    - 호출 시각 (타임스탬프)
//    - 커맨드 카테고리 / 이름
//    - 인자 (args)
//    - 결과 메시지
// ============================================================

    // ──────────────────────────────────────────
    //  로그 항목 구조체
    // ──────────────────────────────────────────
    [Serializable]
    public struct DebugLogEntry
    {
        public int    callerId;     // 호출한 클라이언트 Connection ID
        public string timestamp;    // HH:mm:ss
        public string category;
        public string commandName;
        public string args;
        public string result;

        public override string ToString() =>
            $"[{timestamp}] Client#{callerId} | {category}/{commandName}" +
            (string.IsNullOrEmpty(args)   ? "" : $" ({args})") +
            (string.IsNullOrEmpty(result) ? "" : $" → {result}");
    }

    // ──────────────────────────────────────────
    //  FishNet 동기화 메시지
    // ──────────────────────────────────────────
    public struct DebugLogBroadcast : IBroadcast
    {
        public DebugLogEntry entry;
    }

    // ──────────────────────────────────────────
    //  DebugLogger
    // ──────────────────────────────────────────
    public class DebugLogger : MonoBehaviour
    {
        public static DebugLogger Instance { get; private set; }

        /// <summary>로그 갱신 시 UI가 구독해서 갱신</summary>
        public static event Action<DebugLogEntry> OnLogAdded;

        private readonly List<DebugLogEntry> _logs = new();
        public IReadOnlyList<DebugLogEntry> Logs => _logs;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            // 서버→클라이언트 로그 수신
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.RegisterBroadcast<DebugLogBroadcast>(OnReceiveLog);
        }

        private void OnDisable()
        {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<DebugLogBroadcast>(OnReceiveLog);
        }

        // ── 서버에서 호출 ─────────────────────

        /// <summary>
        /// 서버: 커맨드 실행 후 로그 기록 + 전체 클라이언트에 브로드캐스트
        /// </summary>
        public void LogAndBroadcast(IDebugCommand command, string[] args, int callerId, string result = "")
        {
            var entry = new DebugLogEntry
            {
                callerId    = callerId,
                timestamp   = DateTime.Now.ToString("HH:mm:ss"),
                category    = command.Category,
                commandName = command.Name,
                args        = string.Join(", ", args),
                result      = result
            };

            AddLog(entry);
            UnityEngine.Debug.Log($"[DebugLogger] {entry}");

            if (InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Broadcast(new DebugLogBroadcast { entry = entry });
        }

        // ── 클라이언트 수신 ───────────────────

        private void OnReceiveLog(DebugLogBroadcast broadcast, Channel channel)
        {
            AddLog(broadcast.entry);
        }

        private void AddLog(DebugLogEntry entry)
        {
            _logs.Add(entry);
            OnLogAdded?.Invoke(entry);
        }

        public void Clear()
        {
            _logs.Clear();
        }
    }