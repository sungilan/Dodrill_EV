using Autohand;
using UnityEngine;

public class KinematicGrab : MonoBehaviour
{
    void Awake()
    {
        Grabbable grabbable = GetComponent<Grabbable>();
        if (grabbable == null)
            return;
        grabbable.body.isKinematic = true;
        grabbable.onGrab.AddListener((hand, grab) =>
        {
            grabbable.body.isKinematic = false;
        });
        grabbable.onRelease.AddListener((hand, grab) =>
        {
            grabbable.body.isKinematic = true;
        });
    }
}