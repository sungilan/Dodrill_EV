using UnityEngine;

/// <summary>
/// 앱 시작 후 처음 90초 동안 '프레임이 오래 걸린 순간(끊김)'을 로그로 남긴다.
///
/// 목적: VR/PC 접속 때 마스터 서버 연결 직후 7~9초 동안 로그가 비는 구간이 있다(두 번째 "connected" 이벤트가 늦게 옴).
///       그 동안 앱이 멈춰 있었는지(메인 스레드가 막힘 → 이 로그에 큰 프레임 시간이 찍힘) 아니면 서버 응답을 기다린 것인지(프레임은 정상) 가른다.
/// 해석: "[Hitch] frame=N dt=7.5s" 가 찍히면 그 프레임에서 7.5초 막힌 것, 안 찍히면 서버/네트워크 대기.
/// 각 줄 앞의 [12.34]는 AndroidFileLogger(Quest) 또는 Player.log(PC) 기준 시각.
/// </summary>
public static class StartupHitchLogger
{
    private const float WatchSeconds = 90f;
    private const float HitchThreshold = 0.35f;   // 이 시간(초)보다 오래 걸린 프레임만 기록

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[StartupHitchLogger]");
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Watcher>();
    }

    private class Watcher : MonoBehaviour
    {
        private int _frames;
        private float _lastRealtime;

        private void Awake()
        {
            _lastRealtime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            _frames++;
            float now = Time.realtimeSinceStartup;
            float dt = now - _lastRealtime;
            _lastRealtime = now;

            if (dt >= HitchThreshold)
                Debug.Log($"[Hitch] frame={_frames} dt={dt:F2}s at={now:F2}s scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

            if (now > WatchSeconds)
            {
                Debug.Log($"[Hitch] 감시 종료 (frames={_frames})");
                Destroy(gameObject);
            }
        }
    }
}
