using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  DebugCommandRegistry.cs
//  커맨드 수동 등록 + 실행 관리
//  새 커맨드 추가 시 DebugBootstrapper.RegisterCommands() 에 한 줄 추가
// ============================================================

    public class DebugCommandRegistry
    {
        private readonly Dictionary<string, IDebugCommand> _commands = new();

        public IReadOnlyDictionary<string, IDebugCommand> Commands => _commands;

        public void Register(IDebugCommand command)
        {
            string key = $"{command.Category}/{command.Name}";

            if (_commands.ContainsKey(key))
            {
                UnityEngine.Debug.LogWarning($"[DebugCommandRegistry] 중복 커맨드 키: {key}");
                return;
            }

            _commands[key] = command;
            UnityEngine.Debug.Log($"[DebugCommandRegistry] 등록: {key}");
        }

        /// <summary>
        /// 서버에서 커맨드 실행
        /// </summary>
        public void Execute(string commandKey, string[] args, int callerId)
        {
            if (!_commands.TryGetValue(commandKey, out var command))
            {
                UnityEngine.Debug.LogError($"[DebugCommandRegistry] 없는 커맨드: {commandKey}");
                return;
            }

            command.Execute(args, callerId);

            DebugLogger.Instance?.LogAndBroadcast(command, args, callerId);
        }
    }