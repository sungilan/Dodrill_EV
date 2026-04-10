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
        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";
        string json = "{\"input\":{\"text\":\"" + text + "\"}," +
                      "\"voice\":{\"languageCode\":\"" + voiceLanguage + "\",\"name\":\"" + voiceName + "\"}," +
                      "\"audioConfig\":{\"audioEncoding\":\"MP3\",\"speakingRate\":0.95}}";

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                //Debug.LogError($"[TTS] API 오류: {req.error}");
                yield break;
            }

            // JSON에서 audioContent(Base64) 추출
            string response = req.downloadHandler.text;
            string key = "\"audioContent\": \"";
            int start = response.IndexOf(key);
            if (start < 0) { Debug.LogError("[TTS] audioContent 없음"); yield break; }
            start += key.Length;
            int end = response.IndexOf("\"", start);
            string b64 = response.Substring(start, end - start);

            // Base64 → MP3 파일 → AudioClip
            byte[] bytes = Convert.FromBase64String(b64);
            string path = Application.persistentDataPath + "/tts_guide.mp3";
            System.IO.File.WriteAllBytes(path, bytes);
            yield return StartCoroutine(LoadAndPlay(path));
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