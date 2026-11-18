using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// JSON <see cref="DialogueLineJson.voiceResourcesPath"/> 한 필드로 음성 로드.
/// <list type="bullet">
/// <item><c>res:폴더/이름</c> 또는 접두사 없음 → <see cref="Resources.Load{T}"/> (확장자 제외)</item>
/// <item><c>sa:상대경로.wav</c> → <see cref="Application.streamingAssetsPath"/> 기준 (코루틴 비동기)</item>
/// </list>
/// </summary>
public static class ScenarioVoiceLoader
{
    private const string PrefixRes = "res:";
    private const string PrefixSa = "sa:";
    private const string PrefixStreaming = "streaming:";

    /// <summary>에디터 등 동기 로드. Resources 및 로컬 StreamingAssets(파일)만 지원.</summary>
    public static bool TryLoadSync(string voicePath, out AudioClip clip)
    {
        clip = null;
        if (string.IsNullOrWhiteSpace(voicePath))
            return false;

        voicePath = voicePath.Trim();

        if (StartsWithIgnoreCase(voicePath, PrefixSa) || StartsWithIgnoreCase(voicePath, PrefixStreaming))
        {
            string rel = StripPrefix(voicePath);
            return TryLoadStreamingAssetsSync(rel, out clip);
        }

        string resKey = StartsWithIgnoreCase(voicePath, PrefixRes) ? StripPrefix(voicePath) : voicePath;
        clip = Resources.Load<AudioClip>(resKey);
        return clip != null;
    }

    /// <summary>런타임 대사 재생용. StreamingAssets는 비동기.</summary>
    public static IEnumerator LoadClipCoroutine(string voicePath, Action<AudioClip> onComplete)
    {
        if (string.IsNullOrWhiteSpace(voicePath))
        {
            onComplete?.Invoke(null);
            yield break;
        }

        voicePath = voicePath.Trim();

        if (StartsWithIgnoreCase(voicePath, PrefixSa) || StartsWithIgnoreCase(voicePath, PrefixStreaming))
        {
            string rel = StripPrefix(voicePath);
            yield return LoadStreamingAssetsCoroutine(rel, onComplete);
            yield break;
        }

        string resKey = StartsWithIgnoreCase(voicePath, PrefixRes) ? StripPrefix(voicePath) : voicePath;
        onComplete?.Invoke(Resources.Load<AudioClip>(resKey));
    }

    private static IEnumerator LoadStreamingAssetsCoroutine(string relativePath, Action<AudioClip> onComplete)
    {
        string combined = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, relativePath));
        string uriString;
        try
        {
            uriString = new Uri(combined).AbsoluteUri;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScenarioVoiceLoader] URI 실패: {combined} — {e.Message}");
            onComplete?.Invoke(null);
            yield break;
        }

        AudioType type = GuessAudioType(relativePath);
        UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uriString, type);
        // 해제 시 AudioClip이 같이 무효화되지 않도록 (UnityWebRequest.Dispose 시 DownloadHandler 정리 방지)
        req.disposeDownloadHandlerOnDispose = false;
        yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        if (req.result != UnityWebRequest.Result.Success)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogWarning($"[ScenarioVoiceLoader] StreamingAssets 로드 실패: {relativePath} — {req.error}");
            req.Dispose();
            onComplete?.Invoke(null);
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        req.Dispose();
        onComplete?.Invoke(clip);
    }

    private static bool TryLoadStreamingAssetsSync(string relativePath, out AudioClip clip)
    {
        clip = null;
        string combined = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, relativePath));
        string uriString;
        try
        {
            uriString = new Uri(combined).AbsoluteUri;
        }
        catch
        {
            return false;
        }

        AudioType type = GuessAudioType(relativePath);
        UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uriString, type);
        req.disposeDownloadHandlerOnDispose = false;
        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            // 에디터 Export 등 짧은 클립 전용 — 런타임 대사는 코루틴 사용
        }

#if UNITY_2020_2_OR_NEWER
        if (req.result != UnityWebRequest.Result.Success)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogWarning($"[ScenarioVoiceLoader] StreamingAssets 동기 로드 실패: {relativePath} — {req.error}");
            req.Dispose();
            return false;
        }

        clip = DownloadHandlerAudioClip.GetContent(req);
        req.Dispose();
        return clip != null;
    }

    private static AudioType GuessAudioType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => AudioType.MPEG,
            ".ogg" => AudioType.OGGVORBIS,
            _ => AudioType.WAV,
        };
    }

    private static bool StartsWithIgnoreCase(string s, string prefix) =>
        s.Length >= prefix.Length && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string StripPrefix(string voicePath)
    {
        int colon = voicePath.IndexOf(':');
        if (colon < 0 || colon >= voicePath.Length - 1)
            return voicePath.Trim();
        return voicePath.Substring(colon + 1).TrimStart('/', '\\');
    }
}
