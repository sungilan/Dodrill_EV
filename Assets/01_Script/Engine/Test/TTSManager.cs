using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

// ============================================================
//  TTSManager.cs  — Google Cloud TTS 완성본
//  Speak(text) 한 줄로 한국어 AI 음성 출력
// ============================================================
[RequireComponent(typeof(AudioSource))]
public class TTSManager : MonoBehaviour
{
    public static TTSManager Instance;

    [Header("API Settings")]
    [SerializeField] private string apiKey = "YOUR_GOOGLE_API_KEY";
    [SerializeField] private string voiceLanguage = "ko-KR";
    [SerializeField] private string voiceName = "ko-KR-Neural2-A"; // 자연스러운 Neural2 음성

    [Header("재생 설정")]
    [SerializeField] private float volume = 1f;
    [SerializeField] private bool skipIfPlaying = false; // true: 재생 중이면 무시

    private AudioSource _audioSource;
    private Coroutine _currentCoroutine;
    private bool _warnedNoKey;
    private string _lastPlayedFile;

    [Serializable] private class TtsInput { public string text; }
    [Serializable] private class TtsVoice { public string languageCode; public string name; }
    [Serializable] private class TtsAudioConfig { public string audioEncoding = "MP3"; public float speakingRate = 0.95f; }
    [Serializable] private class TtsRequest { public TtsInput input = new TtsInput(); public TtsVoice voice = new TtsVoice(); public TtsAudioConfig audioConfig = new TtsAudioConfig(); }
    [Serializable] private class TtsResponse { public string audioContent; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _audioSource = GetComponent<AudioSource>();
        _audioSource.volume = volume;
    }

    public void Speak(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (skipIfPlaying && _audioSource.isPlaying) return;

        if (_currentCoroutine != null) StopCoroutine(_currentCoroutine);
        _currentCoroutine = StartCoroutine(DownloadAndPlay(text));
    }

    public void Stop() => _audioSource.Stop();

    private IEnumerator DownloadAndPlay(string text)
    {
        // 자리표시자 키로는 요청해도 항상 실패한다 — 불필요한 요청 대신 한 번만 알린다
        if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            if (!_warnedNoKey) { _warnedNoKey = true; Debug.LogWarning("[TTS] API 키가 설정되지 않아 음성 안내를 건너뜁니다."); }
            yield break;
        }

        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";
        var body = new TtsRequest();
        body.input.text = text;
        body.voice.languageCode = voiceLanguage;
        body.voice.name = voiceName;
        string json = JsonUtility.ToJson(body);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TTS] API 오류: {req.error} {req.downloadHandler?.text}");
                yield break;
            }

            // 응답 JSON 파싱 (예전에는 '"audioContent": "' 정확한 공백 형식에 의존했다)
            var resp = JsonUtility.FromJson<TtsResponse>(req.downloadHandler.text);
            if (resp == null || string.IsNullOrEmpty(resp.audioContent)) { Debug.LogError("[TTS] audioContent 없음"); yield break; }

            // Base64 -> MP3 파일 -> AudioClip. 요청마다 다른 파일명을 써서 연속 호출 시 덮어쓰기 경합을 피한다.
            byte[] bytes = Convert.FromBase64String(resp.audioContent);
            string path = System.IO.Path.Combine(Application.persistentDataPath, $"tts_{Time.frameCount}_{(uint)text.GetHashCode()}.mp3");
            System.IO.File.WriteAllBytes(path, bytes);
            yield return StartCoroutine(LoadAndPlay(path));

            // 이전에 쓰던 임시 파일 정리(방금 재생한 파일은 다음 호출 때 정리)
            if (!string.IsNullOrEmpty(_lastPlayedFile) && _lastPlayedFile != path)
            {
                try { System.IO.File.Delete(_lastPlayedFile); } catch { }
            }
            _lastPlayedFile = path;
        }
    }

    private IEnumerator LoadAndPlay(string path)
    {
        using (var www = UnityWebRequestMultimedia.GetAudioClip(
            "file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                // UnityEngine.Networking.DownloadHandlerAudioClip (모든 버전 공통)
                var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    _audioSource.clip = clip;
                    _audioSource.Play();
                }
            }
            else
            {
                Debug.LogError($"[TTS] 오디오 로드 실패: {www.error}");
            }
        }
    }
}