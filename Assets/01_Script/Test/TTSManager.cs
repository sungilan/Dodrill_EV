using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

public class TTSManager : MonoBehaviour
{
    public static TTSManager Instance;

    [Header("API Settings")]
    [SerializeField] private string apiKey = "YOUR_GOOGLE_API_KEY";
    [SerializeField] private string voiceLanguage = "ko-KR";
    [SerializeField] private string voiceName = "ko-KR-Standard-A"; // 한국어 여성/남성 선택

    private AudioSource audioSource;

    void Awake()
    {
        Instance = this;
        audioSource = GetComponent<AudioSource>();
        if(audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    // 외부에서 호출할 함수: "MSD를 분리하세요" -> 음성 출력
    public void Speak(string text)
    {
        StartCoroutine(DownloadSpeech(text));
    }

    private IEnumerator DownloadSpeech(string text)
    {
        // Google Cloud TTS REST API 엔드포인트
        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";

        // JSON 요청 데이터 구성
        string jsonRequest = "{\"input\":{\"text\":\"" + text + "\"}," +
                             "\"voice\":{\"languageCode\":\"" + voiceLanguage + "\",\"name\":\"" + voiceName + "\"}," +
                             "\"audioConfig\":{\"audioEncoding\":\"MP3\"}}";

        using(UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequest);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if(request.result == UnityWebRequest.Result.Success)
            {
                // API 응답에서 Base64 오디오 데이터를 추출하여 재생하는 로직 (Helper 필요)
                ProcessAudioResponse(request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("TTS API Error: " + request.error);
            }
        }
    }

    private void ProcessAudioResponse(string jsonResponse)
    {
        // JSON 파싱 후 Base64 string -> AudioClip 변환 로직이 들어갑니다.
        // 실무에서는 'SimpleJSON' 같은 라이브러리를 사용하면 편리합니다.
        Debug.Log("TTS 음성 데이터 수신 완료 및 재생 준비");
    }
}