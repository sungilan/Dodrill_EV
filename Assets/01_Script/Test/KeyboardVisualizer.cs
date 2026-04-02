using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class KeyboardVisualizer : MonoBehaviour
{
    [Header("Key Images")]
    public Image keyW;
    public Image keyA;
    public Image keyS;
    public Image keyD;
    public Image keyG;
    public Image keyF;

    [Header("Settings")]
    public Color normalColor = Color.white;
    public Color activeColor = new Color(1f, 0.5f, 0f); // 주황색
    public float duration = 0.1f;

    void Update()
    {
        // 각 키 입력을 체크하여 처리
        HandleKey(KeyCode.W, keyW);
        HandleKey(KeyCode.A, keyA);
        HandleKey(KeyCode.S, keyS);
        HandleKey(KeyCode.D, keyD);
        HandleKey(KeyCode.G, keyG);
        HandleKey(KeyCode.F, keyF);
    }

    void HandleKey(KeyCode code, Image targetImage)
    {
        if(targetImage == null) return;

        if(Input.GetKeyDown(code))
        {
            // 눌렀을 때: 주황색으로 즉시 변경 + 살짝 커지는 연출
            targetImage.DOKill(); // 기존 트윈 중단
            targetImage.DOColor(activeColor, duration);
            targetImage.transform.DOScale(1.1f, duration).SetEase(Ease.OutQuad);
        }
        else if(Input.GetKeyUp(code))
        {
            // 뗐을 때: 원래 색으로 복귀 + 크기 원상복구
            targetImage.DOKill();
            targetImage.DOColor(normalColor, duration);
            targetImage.transform.DOScale(1.0f, duration).SetEase(Ease.OutQuad);
        }
    }
}