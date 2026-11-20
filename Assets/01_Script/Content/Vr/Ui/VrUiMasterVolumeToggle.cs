using UnityEngine;

/// <summary>
/// <see cref="ToggleButton"/> 상태에 맞춰 <see cref="AudioListener.volume"/>을 0 또는 복원합니다.
/// BTN_SoundMute 등에 붙입니다.
/// </summary>
[RequireComponent(typeof(ToggleButton))]
public class VrUiMasterVolumeToggle : MonoBehaviour
{
    [Tooltip("켜면 ToggleButton이 선택된 상태(_isSelected true)일 때 음소거(볼륨 0). 아이콘 selected가 음소거일 때 켭니다.")]
    [SerializeField] private bool _muteWhenSelected = true;

    [Tooltip("음소거 해제 시 이 값으로 복원(이전 볼륨이 0이었을 때 대비)")]
    [SerializeField, Range(0f, 1f)] private float _defaultUnmutedVolume = 1f;

    ToggleButton _toggle;

    float _volumeBeforeMute = 1f;

    void Awake()
    {
        _toggle = GetComponent<ToggleButton>();
    }

    void OnEnable()
    {
        _toggle.SelectedStateChanged += OnToggleSelected;
        ApplyVolume(_toggle.IsSelected);
    }

    void OnDisable()
    {
        if (_toggle != null)
            _toggle.SelectedStateChanged -= OnToggleSelected;
    }

    void OnToggleSelected(bool selected)
    {
        ApplyVolume(selected);
    }

    void ApplyVolume(bool selected)
    {
        bool muted = _muteWhenSelected ? selected : !selected;
        if (muted)
        {
            _volumeBeforeMute = AudioListener.volume;
            AudioListener.volume = 0f;
        }
        else
        {
            float restore = _volumeBeforeMute > 0.0001f ? _volumeBeforeMute : _defaultUnmutedVolume;
            AudioListener.volume = Mathf.Clamp01(restore);
        }
    }
}
