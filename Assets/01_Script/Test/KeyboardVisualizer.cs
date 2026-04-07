using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class KeyboardVisualizer : MonoBehaviour
{
    [System.Serializable]
    public struct KeyVisualData
    {
        public KeyCode code;
        public Image targetImage;
        public Sprite onSprite;  // 눌렸을 때 이미지
        public Sprite offSprite; // 뗐을 때 이미지
    }

    [Header("Key Settings")]
    public KeyVisualData[] keys; // 인스펙터에서 리스트로 관리하면 훨씬 편합니다.

    [Header("Animation Settings")]
    public float duration = 0.1f;
    public float pressedScale = 1.1f;

    void Update()
    {
        foreach(var keyData in keys)
        {
            HandleInput(keyData);
        }
    }

    void HandleInput(KeyVisualData data)
    {
        if(data.targetImage == null) return;

        if(Input.GetKeyDown(data.code))
        {
            // 눌렀을 때: On 이미지로 교체 + 스케일 업
            data.targetImage.DOKill();
            if(data.onSprite != null) data.targetImage.sprite = data.onSprite;

            data.targetImage.transform.DOScale(pressedScale, duration).SetEase(Ease.OutQuad);
        }
        else if(Input.GetKeyUp(data.code))
        {
            // 뗐을 때: Off 이미지로 교체 + 스케일 복구
            data.targetImage.DOKill();
            if(data.offSprite != null) data.targetImage.sprite = data.offSprite;

            data.targetImage.transform.DOScale(1.0f, duration).SetEase(Ease.OutQuad);
        }
    }
}