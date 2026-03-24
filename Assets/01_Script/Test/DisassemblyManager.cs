using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DisassemblyManager : MonoBehaviour
{
    public static DisassemblyManager Instance { get; private set; }

    public List<InteractablePart> allParts;
    public UnityEvent onAllPartsAssembled;
    private bool isTaskCompleted = false;

    void Awake()
    {
        if(Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        if(!isTaskCompleted) CheckAllPartsStatus();
    }

    private void CheckAllPartsStatus()
    {
        if(allParts == null || allParts.Count == 0) return;

        bool allDone = true;
        int completedCount = 0;

        foreach(var part in allParts)
        {
            // 위치가 맞고(Assembled) + 모든 볼트가 정상 체결됨(Tightened)
            if(part.currentState == InteractablePart.PartState.Assembled && part.IsAllBoltsTightened())
            {
                completedCount++;
            }
            else
            {
                allDone = false;
            }
        }

        if(UIManager.Instance != null)
            UIManager.Instance.UpdateProgress(completedCount, allParts.Count);

        if(allDone) CompleteTask();
    }

    private void CompleteTask()
    {
        isTaskCompleted = true;
        onAllPartsAssembled.Invoke();
        if(UIManager.Instance != null) UIManager.Instance.ShowCompleteUI();
    }

    public bool IsEverythingAssembled()
    {
        foreach(var part in allParts)
        {
            if(part.currentState != InteractablePart.PartState.Assembled || !part.IsAllBoltsTightened())
                return false;
        }
        return true;
    }
}