using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ImpactWrench : XRGrabInteractable
{
    public enum WrenchMode { Unscrew, Screw }

    [Header("Settings")]
    public WrenchMode currentMode = WrenchMode.Unscrew;
    public float rotationSpeed = 100f; // ��Ʈ ȸ�� �ӵ� ����ġ

    [Header("Visuals")]
    public MeshRenderer modeIndicator; // ��� ǥ�ÿ� LED �޽�
    public Color unscrewColor = Color.red;
    public Color screwColor = Color.green;

    private Bolt targetBolt;
    private bool isWorking = false;

    protected override void Awake()
    {
        base.Awake();
        UpdateVisuals();
    }

    // [�߿�] VR ��Ʈ�ѷ��� �⺻ ��ư(A/X ��)���� ��� ��ȯ
    // ����Ƽ �ν������� 'Select Entered' � �����ϰų� ���� �Է� ó��
    public void ToggleMode()
    {
        currentMode = (currentMode == WrenchMode.Unscrew) ? WrenchMode.Screw : WrenchMode.Unscrew;
        UpdateVisuals();

        // ��Ʈ�ѷ� ª�� ���� (��� ���� �˸�)
        SendHapticFeedback(0.3f, 0.1f);
    }

    private void UpdateVisuals()
    {
        if(modeIndicator != null)
        {
            modeIndicator.material.color = (currentMode == WrenchMode.Unscrew) ? unscrewColor : screwColor;
        }
    }

    // Ʈ���Ÿ� ���� �� �۵� ����
    protected override void OnActivated(ActivateEventArgs args)
    {
        base.OnActivated(args);
        isWorking = true;
    }

    // Ʈ���Ÿ� �� �� �۵� ����
    protected override void OnDeactivated(DeactivateEventArgs args)
    {
        base.OnDeactivated(args);
        isWorking = false;
    }

    void Update()
    {
        if(isSelected && isWorking && targetBolt != null)
        {
            // ���� ��忡 ���� ��Ʈ�� ������ �� (+ �Ǵ� -)
            float direction = (currentMode == WrenchMode.Unscrew) ? 1f : -1f;
            targetBolt.InteractWithTool(direction * Time.deltaTime);

            // �۵� �� ���� ����
            SendHapticFeedback(0.1f, 0.05f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Bolt")) targetBolt = other.GetComponent<Bolt>();
    }

    private void OnTriggerExit(Collider other)
    {
        if(other.CompareTag("Bolt")) targetBolt = null;
    }

    private void SendHapticFeedback(float intensity, float duration)
    {
        if(interactorsSelecting.Count > 0)
        {
            var controller = interactorsSelecting[0] as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor;
            if(controller != null) controller.SendHapticImpulse(intensity, duration);
        }
    }
}