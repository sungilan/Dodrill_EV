using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SimpleTogglePanelUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform buttonRect;
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform icon;

    [Header("Position")]
    [SerializeField] private float openedX = 20f;
    [SerializeField] private float visibleButtonWidth = 60f;

    [Header("Offset")]
    [SerializeField] private float openOffset = 20f;   // 👉 더 튀어나오게
    [SerializeField] private float closeOffset = 20f;  // 👉 더 들어가게

    [Header("Animation")]
    [SerializeField] private float duration = 0.25f;
    [SerializeField] private bool startOpened = true;

    private bool isOpen;
    private Coroutine animRoutine;

    private void Awake()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        isOpen = startOpened;
        ApplyImmediate();
    }

    public void Toggle()
    {
        isOpen = !isOpen;

        if (animRoutine != null)
            StopCoroutine(animRoutine);

        animRoutine = StartCoroutine(Animate());
    }

    private float GetClosedX()
    {
        if (panel == null || buttonRect == null)
            return openedX;

        float buttonRight = GetButtonRightEdgeInPanelSpace();

        float baseClosed = openedX - (buttonRight - visibleButtonWidth);

        return baseClosed - closeOffset; // 👉 더 들어가게
    }

    private float GetOpenedX()
    {
        return openedX + openOffset; // 👉 더 나오게
    }

    private float GetButtonRightEdgeInPanelSpace()
    {
        Vector3[] corners = new Vector3[4];
        buttonRect.GetWorldCorners(corners);

        Vector3 rightBottom = panel.InverseTransformPoint(corners[3]);
        return rightBottom.x;
    }

    private void ApplyImmediate()
    {
        if (panel == null) return;

        Vector2 pos = panel.anchoredPosition;
        pos.x = isOpen ? GetOpenedX() : GetClosedX();
        panel.anchoredPosition = pos;

        if (icon != null)
            icon.localRotation = Quaternion.Euler(0f, 0f, isOpen ? 180f : 0f);
    }

    private IEnumerator Animate()
    {
        float startX = panel.anchoredPosition.x;
        float endX = isOpen ? GetOpenedX() : GetClosedX();

        Quaternion startRot = icon != null ? icon.localRotation : Quaternion.identity;
        Quaternion endRot = Quaternion.Euler(0f, 0f, isOpen ? 180f : 0f);

        float time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            t = 1f - Mathf.Pow(1f - t, 3f);

            Vector2 pos = panel.anchoredPosition;
            pos.x = Mathf.Lerp(startX, endX, t);
            panel.anchoredPosition = pos;

            if (icon != null)
                icon.localRotation = Quaternion.Lerp(startRot, endRot, t);

            yield return null;
        }

        ApplyImmediate();
        animRoutine = null;
    }
}