using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

namespace Autohand
{
    [RequireComponent(typeof(Grabbable))]
    public class GrabbableThrowEvent : MonoBehaviour
    {
        public Rigidbody rb;

        [Tooltip("The velocity magnitude required on collision to cause the break event")]
        public float breakVelocity = 1;

        [Tooltip("The layers that will cause this grabbale to break")]
        public LayerMask collisionLayers = ~0;

        public UnityEvent OnBreak;

        Grabbable grab;
        bool thrown = false;
        Coroutine resetThrowing;
        float throwTime = 3;

        void Awake()
        {
            // ✅ Rigidbody 없어도 오류 안 남 (rb = null 허용)
            if (rb == null)
                rb = GetComponent<Rigidbody>();

            grab = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            grab.OnReleaseEvent += OnReleased;
        }

        private void OnDisable()
        {
            grab.OnReleaseEvent -= OnReleased;
        }

        void OnReleased(Hand hand, Grabbable grab)
        {
            if (resetThrowing != null)
                StopCoroutine(resetThrowing);

            resetThrowing = StartCoroutine(ResetThrown());

            // ✅ Rigidbody 없으면 던지기 판정 건너뜀
            if (rb == null || grab.body == null)
                return;

            if (grab.body.linearVelocity.magnitude >= breakVelocity)
                thrown = true;
        }

        IEnumerator ResetThrown()
        {
            yield return new WaitForSeconds(throwTime);
            thrown = false;
            resetThrowing = null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // ✅ Rigidbody 없으면 충돌 파괴 판정 건너뜀
            if (!thrown || grab == null || rb == null)
                return;

            if (((1 << collision.collider.gameObject.layer) & collisionLayers) == 0)
                return;

            if (rb.linearVelocity.magnitude >= breakVelocity)
            {
                Invoke("Break", Time.fixedDeltaTime);
            }
        }

        void Break()
        {
            OnBreak.Invoke();
        }
    }
}