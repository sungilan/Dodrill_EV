using UnityEngine;

namespace DoDrill
{
    public class KeepWorldRotation : MonoBehaviour
    {
        private Quaternion _fixedRotation;

        void Start()
        {
            _fixedRotation = Quaternion.identity; // 항상 정면 고정
        }

        void LateUpdate()
        {
            Vector3 euler = transform.eulerAngles;
            transform.eulerAngles = new Vector3(0f, euler.y, 0f); // X, Z만 고정
        }
    }
}
