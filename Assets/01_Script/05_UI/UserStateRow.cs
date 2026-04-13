using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UserStateRow : MonoBehaviour
{
    [Header("컴포넌트 할당")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image bgImage;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private Outline iconOutline; // UserIcon의 Outline

    [Header("플랫폼 아이콘")]
    [SerializeField] private Sprite vrIcon;
    [SerializeField] private Sprite pcIcon;
    [SerializeField] private Sprite mobileIcon;

    private static readonly Color OutlineDefault = new Color(0f, 0f, 0f, 1f);
    private static readonly Vector2 OutlineSizeDefault = new Vector2(1f, -1f);
    private static readonly Color OutlineActive = new Color(1f, 0.918f, 0f, 1f); // #FFEA00
    private static readonly Vector2 OutlineSizeActive = new Vector2(1f, -1f);

    private int _myClientId = -1;

    public void Apply(int clientId, string displayName, byte platform, bool isActive)
    {
        _myClientId = clientId;
        SetPlatform(platform);
        SetActive(isActive);
        SetDisplayName(displayName, clientId);
    }

    public void UpdateHighlight(int activePlayerId)
    {
        if (_myClientId < 0) return;
        SetActive(_myClientId == activePlayerId);
    }

    public void SetPlatform(byte platform)
    {
        if (iconImage == null) return;
        iconImage.color = Color.white;
        iconImage.sprite = platform switch
        {
            0 => vrIcon,
            1 => pcIcon,
            2 => mobileIcon,
            _ => null
        };
        iconImage.enabled = iconImage.sprite != null;
    }

    public void SetActive(bool isActive)
    {
        if (iconOutline != null)
        {
            iconOutline.effectColor = isActive ? OutlineActive : OutlineDefault;
            iconOutline.effectDistance = isActive ? OutlineSizeActive : OutlineSizeDefault;
        }
    }

    public void SetDisplayName(string name, int clientId)
    {
        if (idText == null) return;
        idText.text = $"Player {clientId + 1}";
    }
}