using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Autohand; // AutoHand 사용 시 필수

public class InteractablePart : MonoBehaviour
{
    public enum PartState { Assembled, Unlocked, Detached }
    public PartState currentState = PartState.Assembled;

    [Header("References")]
    public List<Bolt> requiredBolts = new List<Bolt>();
    public Transform assemblyTarget;
    public Material ghostMaterial;

    private XRGrabInteractable grabInteractable;
    private Grabbable autoGrabbable;
    private GhostManager ghost;

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    [Header("Snap Settings")]
    public float snapDistance = 0.15f;
    public Color snapReadyColor = Color.green;

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        autoGrabbable = GetComponent<Grabbable>();

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        ghost = gameObject.AddComponent<GhostManager>();
        if(ghost != null)
        {
            ghost.ghostMaterial = ghostMaterial;
            ghost.CreateGhost();
        }

        // VR(AutoHand) 이벤트 연결
        if(autoGrabbable != null)
        {
            autoGrabbable.onGrab.AddListener((hand, grabbable) => OnGrabStart());
            autoGrabbable.onRelease.AddListener((hand, grabbable) => OnGrabEnd());
        }
    }

    void Start() => SetPartState(PartState.Assembled);

    void Update()
    {
        if(currentState == PartState.Assembled) CheckBoltsForUnlock();
        else if(currentState == PartState.Detached && IsBeingHeld()) CheckSnapDistance();
    }

    private bool IsBeingHeld()
    {
        bool xriHeld = (grabInteractable != null && grabInteractable.isSelected);
        bool autoHeld = (autoGrabbable != null && autoGrabbable.IsHeld());
        return xriHeld || autoHeld;
    }

    // --- [복구된 필수 메서드들] ---

    /// <summary> 모든 볼트가 규정 토크로 조여졌는지 확인 (DisassemblyManager 참조) </summary>
    public bool IsAllBoltsTightened()
    {
        if(requiredBolts == null || requiredBolts.Count == 0) return true;

        foreach(var bolt in requiredBolts)
        {
            if(bolt == null) continue;
            if(!bolt.isTightened) return false; // 하나라도 안 조여졌으면 false
        }
        return true;
    }

    /// <summary> 매니저에서 강제로 상태를 변경할 때 사용 (AIGuideManager 참조) </summary>
    public void ForceSetState(PartState targetState)
    {
        if(targetState == PartState.Detached)
        {
            // 강제 탈거 시 위치 이동 (예: 보관함 혹은 오프셋 위치)
            transform.position = initialPosition + (Vector3.up * 0.5f);
            if(requiredBolts != null)
            {
                foreach(var bolt in requiredBolts) if(bolt != null) bolt.progress = 1.0f;
            }
        }
        else if(targetState == PartState.Assembled)
        {
            SnapToOriginalPosition();
        }

        SetPartState(targetState);
    }

    /// <summary> 부품 상태를 완전히 초기화 (AIGuideManager 참조) </summary>
    public virtual void ResetPart()
    {
        SnapToOriginalPosition();
        Debug.Log($"<color=white>[Reset]</color> {gameObject.name} 초기화 완료.");
    }

    // --- [인터랙션 및 내부 로직] ---

    public void OnPCClick() // FreeLookController용
    {
        if(currentState == PartState.Assembled) return;
        if(!IsBeingHeld()) OnGrabStart();
        else OnGrabEnd();
    }

    public void OnGrabStart()
    {
        if(ElectricSafetyManager.Instance != null && !ElectricSafetyManager.Instance.IsSafeToWork())
        {
            if(autoGrabbable != null) autoGrabbable.ForceHandsRelease();
            ShowSafetyWarning();
        }
    }

    public void OnGrabEnd()
    {
        float distance = Vector3.Distance(transform.position, initialPosition);
        if(distance < snapDistance) SnapToOriginalPosition();
        else SetPartState(PartState.Detached);
    }

    private void CheckBoltsForUnlock()
    {
        if(ElectricSafetyManager.Instance == null) return;
        if(!ElectricSafetyManager.Instance.IsSafeToWork()) return;

        if(requiredBolts == null || requiredBolts.Count == 0)
        {
            SetPartState(PartState.Unlocked);
            return;
        }

        foreach(var bolt in requiredBolts)
        {
            if(bolt != null && !bolt.isloosened) return;
        }
        SetPartState(PartState.Unlocked);
    }

    private void SetPartState(PartState newState)
    {
        if(this == null) return;
        currentState = newState;

        bool grabEnabled = (newState != PartState.Assembled);
        if(grabInteractable != null) grabInteractable.enabled = grabEnabled;
        if(autoGrabbable != null) autoGrabbable.enabled = grabEnabled;
        if(ghost != null) ghost.SetGhostActive(newState == PartState.Detached);
    }

    private void SnapToOriginalPosition()
    {
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        SetPartState(PartState.Assembled);

        if(requiredBolts != null)
        {
            foreach(var bolt in requiredBolts)
            {
                if(bolt == null) continue;
                bolt.gameObject.SetActive(true);
                bolt.ResetBolt();
            }
        }
    }

    private void CheckSnapDistance()
    {
        if(ghost == null) return;
        float distance = Vector3.Distance(transform.position, initialPosition);
        if(distance < snapDistance) ghost.UpdateGhostColor(snapReadyColor);
        else ghost.ResetGhostColor();
    }

    private void ShowSafetyWarning()
    {
        if(SafetyUIHandler.Instance != null)
            SafetyUIHandler.Instance.TriggerWarning("위험! 고전압 전원을 차단하십시오!");
    }
}