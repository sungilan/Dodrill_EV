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
    public Image leftMouseBtn;
    public Image rightMouseBtn;

    [Header("Settings")]
    public Color normalColor = Color.white;
    public Color activeColor = new Color(1f, 0.5f, 0f); // 주황색
    public float duration = 0.1f;

    void Update()
    {
        // 키보드 입력
        HandleInput(KeyCode.W, keyW);
        HandleInput(KeyCode.A, keyA);
        HandleInput(KeyCode.S, keyS);
        HandleInput(KeyCode.D, keyD);
        HandleInput(KeyCode.G, keyG);
        HandleInput(KeyCode.F, keyF);

        // 마우스 입력 (KeyCode를 활용한 방식)
        HandleInput(KeyCode.Mouse0, leftMouseBtn);  // 마우스 왼쪽 (0)
        HandleInput(KeyCode.Mouse1, rightMouseBtn); // 마우스 오른쪽 (1)
    }

    // 메서드 이름을 범용적으로 HandleInput으로 변경했습니다.
    void HandleInput(KeyCode code, Image targetImage)
    {
        if(targetImage == null) return;

        if(Input.GetKeyDown(code))
        {
            // 눌렀을 때: 주황색 + 스케일 업
            targetImage.DOKill();
            targetImage.DOColor(activeColor, duration);
            targetImage.transform.DOScale(1.1f, duration).SetEase(Ease.OutQuad);
        }
        else if(Input.GetKeyUp(code))
        {
            // 뗐을 때: 원래 색 + 스케일 복구
            targetImage.DOKill();
            targetImage.DOColor(normalColor, duration);
            targetImage.transform.DOScale(1.0f, duration).SetEase(Ease.OutQuad);
        }
    }
}