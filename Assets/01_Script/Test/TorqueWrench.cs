using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class TorqueWrench : XRGrabInteractable
{
    [Header("Torque Config")]
    public float targetTorque = 52f;
    public float breakTorque = 75f;    // 이 수치를 넘기면 부러짐
    public float torqueSpeed = 20f;
    public float tolerance = 2f;

    [Header("UI & Feedback")]
    public TextMeshProUGUI display;
    private float currentTorque = 0f;
    private Bolt targetBolt;

    void Update()
    {
        if(isSelected && targetBolt != null && !targetBolt.isBroken)
        {
            float triggerValue = 0f;
            // XRI 트리거 입력 감지 (사용 중인 입력 방식에 맞게 수정 가능)
            if(interactorsSelecting[0] is UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray)
            {
                // 간단한 예시로 스페이스바나 특정 키 입력을 대체로 쓸 수 있음
            }

            // 시뮬레이션용: 트리거를 누르고 있다고 가정
            if(Input.GetKey(KeyCode.Space) || triggerValue > 0.1f)
            {
                currentTorque += torqueSpeed * Time.deltaTime;

                if(currentTorque > breakTorque)
                {
                    targetBolt.BreakBoltByOverTorque();
                    targetBolt = null;
                    currentTorque = 0f;
                }
                else
                {
                    targetBolt.ApplyFinalTorque(currentTorque);
                }
                UpdateUI();
            }
        }
    }

    private void UpdateUI()
    {
        if(display == null) return;
        display.text = $"{currentTorque:F1} Nm";

        if(Mathf.Abs(currentTorque - targetTorque) <= tolerance) display.color = Color.green;
        else if(currentTorque > targetTorque) display.color = Color.red;
        else display.color = Color.white;
    }

    // 볼트에 도구를 갖다 댔을 때 호출 (Trigger 이벤트 등에 연결)
    public void ConnectToBolt(Bolt bolt)
    {
        if(!bolt.isBroken && bolt.Progress <= 0.05f)
        {
            targetBolt = bolt;
            currentTorque = 0f;
        }
    }
}