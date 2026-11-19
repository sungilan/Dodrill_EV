using System.Diagnostics;
using System.IO;
using UnityEngine;

/// <summary>
/// Android(Quest·모바일) 빌드의 Debug.Log 를 기기 안 파일로 남긴다 (경과 시간 포함).
///
/// 이 프로젝트의 APK 는 Unity 로그가 logcat 에 안 나와서 헤드셋에서 무슨 일이 일어나는지 볼 수 없었다
/// (VR 메뉴가 5초 늦게 뜨는 원인, VR UI 클릭 문제를 확인하려 해도 근거가 없었다).
///
/// 파일: &lt;persistentDataPath&gt;/Player.log (이전 실행은 Player-prev.log)
///   Quest 는 USB 로 바로 꺼낼 수 있다:
///     adb pull /sdcard/Android/data/com.remoshot.DoDrill.EV/files/Player.log
/// 각 줄 앞의 [12.34] 는 앱 시작 후 경과 초 — 줄 사이 간격으로 어디서 멈췄는지 본다.
/// 에디터·PC 빌드에서는 동작하지 않는다(Player.log 가 이미 있음).
/// 쓰기: 예전엔 AutoFlush 로 로그 한 줄마다 메인 스레드에서 파일에 바로 썼다(VR 프레임 끊김).
///   지금은 메모리 버퍼에 모았다가 1초마다(백그라운드 타이머) 쓴다. 오류·예외는 원인이 남도록 즉시 쓴다.
///   앱이 배경으로 가거나(헤드셋 벗음·홈 버튼) 종료될 때도 비운다.
/// </summary>
public static class AndroidFileLogger
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static StreamWriter _writer;
    private static readonly Stopwatch _clock = new Stopwatch();
    private static readonly object _lock = new object();
    private static System.Threading.Timer _flushTimer;
    private static bool _dirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        try
        {
            string dir = Application.persistentDataPath;
            string path = Path.Combine(dir, "Player.log");
            if (File.Exists(path)) File.Copy(path, Path.Combine(dir, "Player-prev.log"), true);

            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false), 32 * 1024) { AutoFlush = false };
            _clock.Start();
            _writer.WriteLine($"=== {Application.productName} {Application.version} | {SystemInfo.deviceModel} | Unity {Application.unityVersion} | {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Application.logMessageReceivedThreaded += OnLog;
            _writer.Flush();
            Application.quitting += Flush;
            Application.focusChanged += f => { if (!f) Flush(); };
            _flushTimer = new System.Threading.Timer(_ => { if (_dirty) Flush(); }, null, 1000, 1000);
        }
        catch { /* 파일을 못 열면 조용히 포기 */ }
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (_writer == null) return;
        try
        {
            lock (_lock)
            {
                _writer.WriteLine($"[{_clock.Elapsed.TotalSeconds:F2}] {type}: {condition}");
                _dirty = true;
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    if (!string.IsNullOrEmpty(stackTrace)) _writer.WriteLine(stackTrace);
                    _writer.Flush(); _dirty = false;   // 오류는 바로 남긴다(직후 크래시해도 원인이 파일에 있게)
                }
            }
        }
        catch { }
    }

    private static void Flush()
    {
        try { lock (_lock) { _writer?.Flush(); _dirty = false; } } catch { }
    }
#endif
}
