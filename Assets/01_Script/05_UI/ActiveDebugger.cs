using UnityEngine;

public class ActiveDebugger : MonoBehaviour
{
    void OnDisable()
    {
        Debug.Log($"[{name}] 꺼짐!\n{System.Environment.StackTrace}");
    }
}