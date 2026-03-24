using UnityEngine;

public class Rotate: MonoBehaviour
{
    float rotateSpeed = 1;
    void Update()
    {
        transform.Rotate(Vector3.up, rotateSpeed);
    }
}
