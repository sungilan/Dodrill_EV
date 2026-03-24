using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RootMotion.FinalIK;

namespace Autohand {
    [DefaultExecutionOrder(12), RequireComponent(typeof(VRIK))]
    public class AutoHandVRIK : MonoBehaviour {
        public Hand rightHand;
        public Hand leftHand;
        [Tooltip("The transform (or a child transform) of the Tracked VR controller")]
        public Transform rightTrackedController;
        [Tooltip("The transform (or a child transform) of the Tracked VR controller")]
        public Transform leftTrackedController;

        [HideInInspector, Tooltip("Should be a transform under the Auto Hand, can be used to adjust the IK offset so the hands connect with the arms properly (This is the point where the wrists follow the hands)")]
        public Transform rightIKTarget = null;
        [HideInInspector, Tooltip("Should be a transform under the Auto Hand, can be used to adjust the IK offset so the hands connect with the arms properly (This is the point where the wrists follow the hands)")]
        public Transform leftIKTarget = null;
        [HideInInspector, Tooltip("Should be a transform under the IK Character hierarchy, can be used to adjust the IK offset so the hands connect with the arms properly")]
        public Transform rightHandFollowTarget = null;
        [HideInInspector, Tooltip("Should be a transform under the IK Character hierarchy, can be used to adjust the IK offset so the hands connect with the arms properly")]
        public Transform leftHandFollowTarget = null;


        public VRIK visibleIK { get; protected set; }
        public VRIK invisibleIK { get; protected set; }

        bool isCopy = false;
        bool resetQueued = false;
        SkinnedMeshRenderer skinnedMeshRenderer;

        Animator visibleAnimator;
        Animator invisibleAnimator;

        public virtual void DesignateCopy() {
            isCopy = true;
        }

        protected virtual void Start() {
            visibleIK = GetComponent<VRIK>();
            visibleAnimator = GetComponentInChildren<Animator>();

            if(!isCopy)
                SetupIKCopy();

            if(AutoHandPlayer.Instance != null)
                visibleIK.transform.position -= Vector3.up * AutoHandPlayer.Instance.heightOffset;

            skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            skinnedMeshRenderer.forceMatrixRecalculationPerRender = true;

            StartCoroutine(RebindAnimatorDelay());
        }

        //This fixes a bug with the animator not updating the IK automatically
        IEnumerator RebindAnimatorDelay() {
            yield return new WaitForEndOfFrame();
            yield return new WaitForFixedUpdate();
            visibleAnimator.Rebind();
            invisibleAnimator.Rebind();
        }

        protected virtual void OnEnable() {
            if(!isCopy && AutoHandPlayer.Instance != null) {
                AutoHandPlayer.Instance.OnSnapTurn += AutoPlayerResetIKEvent;
                AutoHandPlayer.Instance.OnSmoothTurn += AutoPlayerResetIKEvent;
                AutoHandPlayer.Instance.OnTeleported += AutoPlayerResetIKEvent;

                rightHand.OnGrabbed += OnRightGrab;
                rightHand.OnReleased += OnRightRelease;
                leftHand.OnGrabbed += OnLeftGrab;
                leftHand.OnReleased += OnLeftRelease;
            }

            if(invisibleIK != null)
                invisibleIK.gameObject.SetActive(true);

            resetQueued = true;
        }

        protected virtual void OnDisable() {
            if(!isCopy && AutoHandPlayer.Instance != null) {
                AutoHandPlayer.Instance.OnSnapTurn -= AutoPlayerResetIKEvent;
                AutoHandPlayer.Instance.OnSmoothTurn -= AutoPlayerResetIKEvent;
                AutoHandPlayer.Instance.OnTeleported -= AutoPlayerResetIKEvent;

                rightHand.OnGrabbed -= OnRightGrab;
                rightHand.OnReleased -= OnRightRelease;
                leftHand.OnGrabbed -= OnLeftGrab;
                leftHand.OnReleased -= OnLeftRelease;


                if(invisibleIK != null)
                    invisibleIK.gameObject.SetActive(false);
            }
        }

        private void OnDestroy() {
            if(isCopy)
                return;

            if(invisibleIK != null)
                Destroy(invisibleIK.gameObject);
            Destroy(leftHand.gameObject);
            Destroy(rightHand.gameObject);
        }


        protected virtual void OnRightGrab(Hand hand, Grabbable grab) {
            visibleIK.references.rightHand = hand.handGrabPoint;
            visibleIK.solver.rightArm.target = hand.handGrabPoint;
            invisibleIK.solver.rightArm.target = rightTrackedController;
        }

        protected virtual void OnRightRelease(Hand hand, Grabbable grab) {
            visibleIK.references.rightHand = rightHandFollowTarget;
            visibleIK.solver.rightArm.target = rightIKTarget;
            invisibleIK.solver.rightArm.target = rightTrackedController;
        }

        protected virtual void OnLeftGrab(Hand hand, Grabbable grab) {
            visibleIK.references.leftHand = hand.handGrabPoint;
            visibleIK.solver.leftArm.target = hand.handGrabPoint;
            invisibleIK.solver.leftArm.target = leftTrackedController;
        }

        protected virtual void OnLeftRelease(Hand hand, Grabbable grab) {
            visibleIK.references.leftHand = leftHandFollowTarget;
            visibleIK.solver.leftArm.target = leftIKTarget;
            invisibleIK.solver.leftArm.target = leftTrackedController;
        }


        protected virtual void AutoPlayerResetIKEvent(AutoHandPlayer player) {
            resetQueued = true;
        }

        private void Update() {
            if(isCopy)
                return;

            if(resetQueued) {
                rightHand.handFollow.SetMoveTo(true);
                rightHand.handFollow.SetHandLocation(rightHand.moveTo.position, rightHand.moveTo.rotation);
                leftHand.handFollow.SetMoveTo(true);
                leftHand.handFollow.SetHandLocation(leftHand.moveTo.position, leftHand.moveTo.rotation);

                visibleIK.solver.Reset();
                invisibleIK.solver.Reset();
                resetQueued = false;
            }
        }

        protected virtual void LateUpdate() {
            if(isCopy)
                return;

            // adjust so feet are aligned with AutoHandPlayer to respect heightOffset
            Vector3 pos = transform.position;
            pos.y = AutoHandPlayer.Instance.transform.position.y;
            transform.position = pos;
            invisibleIK.transform.position = pos;

        }


        protected virtual void SetupIKCopy() {
            if(rightIKTarget == null) {
                rightIKTarget = new GameObject().transform;
                rightIKTarget.name = "rightIKTarget";
                rightIKTarget.transform.parent = rightHand.transform;

                rightIKTarget.transform.localPosition = Vector3.zero;
                rightIKTarget.transform.localRotation = Quaternion.identity;
            }
            if(leftIKTarget == null) {
                leftIKTarget = new GameObject().transform;
                leftIKTarget.name = "leftIKTarget";
                leftIKTarget.transform.parent = leftHand.transform;

                leftIKTarget.transform.localPosition = Vector3.zero;
                leftIKTarget.transform.localRotation = Quaternion.identity;
            }
            if(rightHandFollowTarget == null) {
                rightHandFollowTarget = new GameObject().transform;
                rightHandFollowTarget.name = "rightHandTarget";
                rightHandFollowTarget.transform.parent = rightHand.transform.parent;

                rightHandFollowTarget.transform.localPosition = rightHand.transform.localPosition;
                rightHandFollowTarget.transform.localRotation = rightHand.transform.localRotation;
            }
            if(leftHandFollowTarget == null) {
                leftHandFollowTarget = new GameObject().transform;
                leftHandFollowTarget.name = "leftHandTarget";
                leftHandFollowTarget.transform.parent = leftHand.transform.parent;
                leftHandFollowTarget.transform.localPosition = leftHand.transform.localPosition;
                leftHandFollowTarget.transform.localRotation = leftHand.transform.localRotation;
            }

            rightHand.transform.parent = visibleIK.transform.parent;
            leftHand.transform.parent = visibleIK.transform.parent;

            visibleIK.references.rightHand = rightHandFollowTarget;
            visibleIK.references.leftHand = leftHandFollowTarget;

            invisibleIK = Instantiate(visibleIK.gameObject, visibleIK.transform.parent).GetComponent<VRIK>();
            invisibleIK.name = "Hidden IK Copy (Auto Hand + VRIK requirement)";
            invisibleIK.enabled = true;
            invisibleAnimator = invisibleIK.GetComponentInChildren<Animator>();


            if(invisibleIK.CanGetComponent<AutoHandVRIK>(out var autoIK)) {
                autoIK.DesignateCopy();
                autoIK.enabled = false;
                rightHand.follow = autoIK.rightHandFollowTarget;
                leftHand.follow = autoIK.leftHandFollowTarget;
                autoIK.invisibleIK = invisibleIK;
                autoIK.visibleIK = visibleIK;
            }

            DeactivateEverything(invisibleIK.transform);

            visibleIK.solver.rightArm.target = rightIKTarget;
            visibleIK.solver.leftArm.target = leftIKTarget;
            invisibleIK.solver.rightArm.target = rightTrackedController;
            invisibleIK.solver.leftArm.target = leftTrackedController;
        }

        protected virtual void DeactivateEverything(Transform deactivate) {
            var behaviours = deactivate.GetComponentsInChildren<Component>(true);
            for(int j = behaviours.Length - 1; j >= 0; j--)
                if(!(behaviours[j] is Animator) && !(behaviours[j] is VRIK) && !(behaviours[j] is Transform))
                    Destroy(behaviours[j]);
        }
    }
}