using TMPro;
using UnityEngine;
using XRAirpotrSecurity;

/// <summary>
/// BuildCompat 의 화면 표시 — 구석의 빌드 번호와 접속 불가 안내.
/// VR 헤드셋에는 Screen Space 캔버스가 보이지 않으므로 VR 에서는 안내를 눈앞 월드 캔버스로 바꾼다.
/// </summary>
public class BuildCompatNotice : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text buildLabel;

    [Header("VR")]
    [SerializeField] private float vrDistance = 2f;
    [SerializeField] private float vrScale = 0.001f;

    private bool _followHead;

    private void Awake()
    {
        panel.SetActive(false);
    }

    public void SetBuildLabel(string text)
    {
        buildLabel.text = text;
    }

    public void Show(string message)
    {
        messageText.text = message;
        panel.SetActive(true);

        if(Utils.GetPlatformType() == PlatformType.VR && !_followHead)
        {
            _followHead = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.localScale = Vector3.one * vrScale;
            buildLabel.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if(!_followHead) return;

        var head = Camera.main;
        if(head == null) return;

        canvas.transform.SetPositionAndRotation(
            head.transform.position + head.transform.forward * vrDistance,
            Quaternion.LookRotation(head.transform.forward, Vector3.up));
    }
}
