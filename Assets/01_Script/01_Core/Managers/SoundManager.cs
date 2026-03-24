using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager
{
    AudioSource[] _audioSources = new AudioSource[(int)Define.Sound.MaxCount];
    Dictionary<string, AudioClip> _audioClips = new Dictionary<string, AudioClip>();

    public void Init()
    {
        GameObject root = GameObject.Find("@Sound");
        if (root == null)
        {
            root = new GameObject { name = "@Sound" };

            string[] soundNames = System.Enum.GetNames(typeof(Define.Sound));
            for (int i = 0; i < soundNames.Length - 1; i++)
            {
                GameObject go = new GameObject { name = soundNames[i] };
                _audioSources[i] = go.AddComponent<AudioSource>();
                go.transform.parent = root.transform;
            }

            _audioSources[(int)Define.Sound.Bgm].loop = true;
        }
    }

    public void Clear()
    {
        foreach (AudioSource audioSource in _audioSources)
        {
            audioSource.clip = null;
            audioSource.Stop();
        }
        _audioClips.Clear();
    }

    public void Play(string path, Define.Sound type = Define.Sound.Effect, float pitch = 1.0f, float volume = 1.0f)
    {
        AudioClip audioClip = GetOrAddAudioClip(path, type);
        Play(audioClip, type, pitch, volume: volume);
    }

    public void Play(AudioClip audioClip, Define.Sound type = Define.Sound.Effect, float pitch = 1.0f, System.Action onComplete = null, float volume = 1.0f)
    {
        if (audioClip == null)
            return;

        if (type == Define.Sound.Bgm)
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Bgm];
            if (audioSource.isPlaying)
                audioSource.Stop();

            audioSource.pitch = pitch;
            audioSource.clip = audioClip;
            audioSource.Play();

            // BGM은 루프가 기본적으로 설정되어 있으므로 콜백을 호출하지 않음
            if (!audioSource.loop && onComplete != null)
            {
                Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioSource, onComplete));
            }
        }
        else
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Effect];
            audioSource.pitch = 1f;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(audioClip, volume);

            // 효과음은 PlayOneShot이므로 별도의 AudioSource를 추적할 필요 없음
            if (onComplete != null)
            {
                Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioClip.length, onComplete));
            }
        }
    }

    public void PlayBgm(string path, Define.Sound type = Define.Sound.Effect, float pitch = 1.0f)
    {
        AudioClip audioClip = GetOrAddAudioClip(path, type);
        PlayBgm(audioClip, type, pitch);
    }
    public void StopBgm()
    {
        _audioSources[(int)Define.Sound.Bgm].Stop();
    }
    public void PlayBgm(AudioClip audioClip, Define.Sound type = Define.Sound.Effect, float pitch = 1.0f, System.Action onComplete = null)
    {
        if (audioClip == null)
            return;

        if (type == Define.Sound.Bgm)
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Bgm];
            if (audioSource.isPlaying)
                return;

            audioSource.pitch = pitch;
            audioSource.clip = audioClip;
            audioSource.Play();

            // BGM은 루프가 기본적으로 설정되어 있으므로 콜백을 호출하지 않음
            if (!audioSource.loop && onComplete != null)
            {
                Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioSource, onComplete));
            }
        }
        else
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Effect];
            audioSource.pitch = 1f;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(audioClip);

            // 효과음은 PlayOneShot이므로 별도의 AudioSource를 추적할 필요 없음
            if (onComplete != null)
            {
                Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioClip.length, onComplete));
            }
        }
    }


    public IEnumerator PlayAndWait(AudioClip audioClip, Define.Sound type = Define.Sound.Effect)
    {
        if (audioClip == null)
            yield break;

        if (type == Define.Sound.Bgm)
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Bgm];
            if (audioSource.isPlaying)
                audioSource.Stop();
            audioSource.clip = audioClip;
            audioSource.Play();

            yield return Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioSource, null));
        }
        else
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Effect];
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(audioClip);
            yield return Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioClip.length, null));
        }
    }
    public IEnumerator PlayAndWait(string path, Define.Sound type = Define.Sound.Effect)
    {
        AudioClip audioClip = GetOrAddAudioClip(path, type);
        if (audioClip == null)
            yield break;

        if (type == Define.Sound.Bgm)
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Bgm];
            if (audioSource.isPlaying)
                audioSource.Stop();
            audioSource.clip = audioClip;
            audioSource.Play();

            yield return Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioSource, null));
        }
        else
        {
            AudioSource audioSource = _audioSources[(int)Define.Sound.Effect];
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(audioClip);
            yield return Managers.Instance.StartCoroutine(WaitForSoundToEnd(audioClip.length, null));
        }
    }

    public IEnumerator PlayRandomPulseSound()
    {
        int ran = Random.Range(0, 3);
        if (ran == 0)
        {
            for (int i = 0; i < 5; i++)
            {
                Play("Pulse");
                yield return new WaitForSeconds(.8f);
            }
        }
        else if (ran == 1)
        {
            for (int i = 0; i < 5; i++)
            {
                Play("Pulse");
                yield return new WaitForSeconds(1.2f);
            }
        }
        else
        {
            for (int i = 0; i < 5; i++)
            {
                Play("Pulse");
                yield return new WaitForSeconds(Random.Range(0.5f, 1.2f));
            }
        }
    }

    private IEnumerator WaitForSoundToEnd(AudioSource audioSource, System.Action onComplete)
    {
        yield return new WaitWhile(() => audioSource.isPlaying);
        onComplete?.Invoke();
    }

    private IEnumerator WaitForSoundToEnd(float clipLength, System.Action onComplete)
    {
        yield return new WaitForSeconds(clipLength);
        onComplete?.Invoke();
    }

    public void StopAll()
    {
        foreach (var source in _audioSources)
        {
            source.Stop();
        }
    }
    public void PlayBgmPingPong()
    {
        string[] bgmPaths = { "War1", "War2", "War3" }; // BGM 경로 배열
        int currentIndex = 0; // 현재 재생 중인 BGM 인덱스

        // 코루틴 시작
        Managers.Instance.StartCoroutine(PlayBgmSequence());

        IEnumerator PlayBgmSequence()
        {
            while (true)
            {
                string currentPath = bgmPaths[currentIndex];
                Play(currentPath, Define.Sound.Bgm); // BGM 재생

                // 현재 BGM의 길이를 가져와 대기
                AudioClip currentClip = GetOrAddAudioClip(currentPath, Define.Sound.Bgm);
                if (currentClip != null)
                {
                    yield return new WaitForSeconds(currentClip.length);
                }

                // 다음 BGM으로 이동 (순환)
                currentIndex = (currentIndex + 1) % bgmPaths.Length;
            }
        }
    }

    AudioClip GetOrAddAudioClip(string path, Define.Sound type = Define.Sound.Effect)
    {
        if (path.Contains("Sounds/") == false)
            path = $"Sounds/{path}";

        AudioClip audioClip = null;

        if (type == Define.Sound.Bgm)
        {
            audioClip = Managers.Resource.Load<AudioClip>(path);
        }
        else
        {
            if (_audioClips.TryGetValue(path, out audioClip) == false)
            {
                audioClip = Managers.Resource.Load<AudioClip>(path);
                _audioClips.Add(path, audioClip);
            }
        }

        if (audioClip == null)
            Debug.Log($"AudioClip Missing ! {path}");

        return audioClip;
    }
}