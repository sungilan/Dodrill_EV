using UnityEngine;
using TMPro;

public class Multimeter : MonoBehaviour
{
    public TextMeshProUGUI displayTarget; // 전압이 표시될 LCD 화면

    private MeasurableNode posNode; // 현재 접촉 중인 + 노드
    private MeasurableNode negNode; // 현재 접촉 중인 - 노드

    public void UpdateProbeContact(Polarity probeType, MeasurableNode node)
    {
        if(probeType == Polarity.Positive) posNode = node;
        else negNode = node;

        CalculateVoltage();
    }

    private void CalculateVoltage()
    {
        if(posNode != null && negNode != null)
        {
            // MSD는 뽑았지만 방전 시간을 안 지켰다면?
            if(LOTOSystem.Instance.isLOCKED && !DischargeTimerUI.Instance.isDischarged)
            {
                displayTarget.text = "380.5 V"; // 위험! 잔류 전압 표시
                SafetyUIHandler.Instance.TriggerWarning("위험! 잔류 전압이 남아있습니다. 대기 시간을 준수하세요.");
            }
            else if(DischargeTimerUI.Instance.isDischarged)
            {
                displayTarget.text = "0.00 V"; // 안전 확인 완료
            }
        }
    }
}