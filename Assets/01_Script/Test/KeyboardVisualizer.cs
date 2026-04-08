using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using PinePie.SimpleJoystick; // 조이스틱 네임스페이스 추가

public class KeyboardVisualizer : MonoBehaviour
{
    [System.Serializable]
    public struct KeyVisualData
    {
        public string keyName;   // "W", "A", "S", "D" 등 구분용 이름
        public KeyCode code;
        public Image targetImage;
        public Sprite onSprite;
        public Sprite offSprite;
    }

    [Header("Key Settings")]
    public KeyVisualData[] keys;

    [Header("Joystick Settings")]
    public JoystickController joystick; // 인스펙터에서 JoystickController 연결
    [Range(0.1f, 1.0f)] public float threshold = 0.5f; // 얼마나 밀었을 때 활성화할지

    [Header("Animation Settings")]
    public float duration = 0.1f;
    public float pressedScale = 1.1f;

    // 현재 각 키의 상태를 추적 (중복 애니메이션 방지)
    private bool[] isKeyPressed;

    void Start()
    {
        if(keys != null) isKeyPressed = new bool[keys.Length];
    }

    void Update()
    {
        Vector2 joyDir = (joystick != null) ? joystick.InputDirection : Vector2.zero;

        for(int i = 0; i < keys.Length; i++)
        {
            bool currentState = CheckInput(keys[i], joyDir);

            // 상태가 변했을 때만 비주얼 업데이트 (DOKill 중복 방지)
            if(currentState != isKeyPressed[i])
            {
                isKeyPressed[i] = currentState;
                UpdateVisual(keys[i], currentState);
            }
        }
    }

    // 키보드와 조이스틱 입력을 동시에 체크
    bool CheckInput(KeyVisualData data, Vector2 joyDir)
    {
        // 1. 키보드 입력 체크
        if(Input.GetKey(data.code)) return true;

        // 2. 조이스틱 입력 체크 (keyName 기준)
        if(string.IsNullOrEmpty(data.keyName)) return false;

        switch(data.keyName.ToUpper())
        {
            case "W": return joyDir.y > threshold;
            case "S": return joyDir.y < -threshold;
            case "A": return joyDir.x < -threshold;
            case "D": return joyDir.x > threshold;
            default: return false;
        }
    }

    void UpdateVisual(KeyVisualData data, bool active)
    {
        if(data.targetImage == null) return;

        data.targetImage.DOKill();

        if(active)
        {
            if(data.onSprite != null) data.targetImage.sprite = data.onSprite;
            data.targetImage.transform.DOScale(pressedScale, duration).SetEase(Ease.OutQuad);
        }
        else
        {
            if(data.offSprite != null) data.targetImage.sprite = data.offSprite;
            data.targetImage.transform.DOScale(1.0f, duration).SetEase(Ease.OutQuad);
        }
    }
}