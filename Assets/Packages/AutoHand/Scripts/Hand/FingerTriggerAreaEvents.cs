using Autohand;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class FingerTriggerAreaEvents : HandTriggerAreaEvents
{
    static readonly List<Hand> s_allHandsScratch = new List<Hand>(8);
    static float s_allHandsScratchTime = -1000f;
    const float AllHandsScratchTtlSeconds = 0.2f;

    [Header("Finger Trigger Events")]
    public FingerEnum[] allowedFingers;
    [Tooltip("true면 손바닥/손목 트리거가 UI 영역에 들어오지 않아도, 씬의 활성 Hand마다 검지 overlap을 검사합니다. world-space UI 검지 터치에 권장.")]
    public bool useAllActiveHandsForFingerOverlap = true;
    [Tooltip("tipRadius가 작은 손 모델에서 VR UI 등이 인식되도록 최소 구 반경(미터).")]
    public float minFingerOverlapRadius = 0.04f;
    [Tooltip("Grabbing 레이어 Overlap이 비어도, 이 영역 콜라이더와의 거리로 검지 접촉을 인정합니다.")]
    public bool useGeometryFallback = true;
    [Tooltip("해당 손의 XR 컨트롤러가 추적 중일 때 손가락 Enter/Exit 이벤트 차단 여부. false면 UIPointer가 떠도 손가락 UI 입력을 허용합니다.")]
    public bool suppressFingerEventsWhileControllerTracked = true;
    [Space]
    public UnityEvent<Finger, FingerTriggerAreaEvents> FingerEnterEvent;
    public UnityEvent<Finger, FingerTriggerAreaEvents> FingerExitEvent;

    protected Collider[] triggerAreaColliders;
    protected GameObject[] triggerAreaObjects;
    protected int[] startingLayers;

    Collider[] colliderNonAlloc = new Collider[32];
    bool validState = false;
    bool lastValidState = false;
    Finger currentFinger;

    protected virtual void Awake()
    {
        if(allowedFingers == null || allowedFingers.Length == 0)
            allowedFingers = new FingerEnum[] { FingerEnum.index };

        triggerAreaColliders = GetComponentsInChildren<Collider>();
        startingLayers = new int[triggerAreaColliders.Length];
        triggerAreaObjects = new GameObject[triggerAreaColliders.Length];
        for(int i = 0; i < triggerAreaColliders.Length; i++)
        {
            startingLayers[i] = triggerAreaColliders[i].gameObject.layer;
            triggerAreaObjects[i] = triggerAreaColliders[i].gameObject;
        }
    }


    protected virtual void FixedUpdate()
    {
        CheckFingerOverlapEvents();
    }

    protected virtual void CheckFingerOverlapEvents()
    {
        validState = false;

        IList<Hand> handsForFingers = hands;
        if(useAllActiveHandsForFingerOverlap)
        {
            RefreshAllHandsScratchIfStale();
            handsForFingers = s_allHandsScratch;
        }
        else if(hands.Count == 0)
        {
            if(validState != lastValidState)
            {
                if(validState == false)
                    OnFingerExit(currentFinger);
            }
            if(validState == false)
                currentFinger = null;
            lastValidState = validState;
            return;
        }

        if(handsForFingers.Count > 0)
        {
            var layer = LayerMask.NameToLayer(Hand.grabbingLayerName);
            var layerMask = LayerMask.GetMask(Hand.grabbingLayerName);
            for(int i = 0; i < triggerAreaColliders.Length; i++)
                triggerAreaObjects[i].layer = layer;

            foreach(var hand in handsForFingers)
            {
                if(hand == null || !hand.isActiveAndEnabled)
                    continue;
                if(!HandAllowedForArea(hand))
                    continue;
                foreach(var finger in hand.fingers)
                {
                    for(int i = 0; i < allowedFingers.Length; i++)
                    {
                        if(finger.fingerType == allowedFingers[i])
                        {
                            float sphereR = Mathf.Max(finger.tipRadius, minFingerOverlapRadius);
                            int overlapCount = Physics.OverlapSphereNonAlloc(finger.tip.position, sphereR, colliderNonAlloc, layerMask, QueryTriggerInteraction.Collide);
                            bool fingerHits = overlapCount > 0;

                            if(!fingerHits && useGeometryFallback && triggerAreaColliders != null)
                            {
                                Vector3 tip = finger.tip.position;
                                foreach(var tc in triggerAreaColliders)
                                {
                                    if(tc == null || !tc.enabled) continue;
                                    Vector3 closest = tc.ClosestPoint(tip);
                                    if((closest - tip).sqrMagnitude <= sphereR * sphereR)
                                    {
                                        fingerHits = true;
                                        break;
                                    }
                                }
                            }

                            if(fingerHits)
                            {
                                if(suppressFingerEventsWhileControllerTracked && VrFingerUiInputGate.BlockFingerUiWhileControllerTracked(hand))
                                    continue;

                                if(!VrFingerUiCanvasGate.AreAncestorsAllowingFinger(transform))
                                    continue;

                                validState = true;
                                currentFinger = finger;
                                if(validState != lastValidState)
                                    OnFingerEnter(finger);
                                break;
                            }
                        }
                    }
                }
            }

            for(int i = 0; i < startingLayers.Length; i++)
                triggerAreaObjects[i].layer = startingLayers[i];
        }

        if(validState != lastValidState)
        {
            if(validState == false)
                OnFingerExit(currentFinger);
        }

        if(validState == false)
            currentFinger = null;
        lastValidState = validState;
    }

    bool HandAllowedForArea(Hand hand)
    {
        if(handType == HandType.none)
            return false;
        if(hand.left && handType == HandType.right)
            return false;
        if(!hand.left && handType == HandType.left)
            return false;
        return true;
    }

    static void RefreshAllHandsScratchIfStale()
    {
        if(Time.unscaledTime - s_allHandsScratchTime < AllHandsScratchTtlSeconds)
            return;
        s_allHandsScratchTime = Time.unscaledTime;
        s_allHandsScratch.Clear();
        var found = Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for(int i = 0; i < found.Length; i++)
        {
            var h = found[i];
            if(h != null && h.isActiveAndEnabled)
                s_allHandsScratch.Add(h);
        }
    }

    protected virtual void OnFingerEnter(Finger finger)
    {
        FingerEnterEvent?.Invoke(finger, this);
    }

    protected virtual void OnFingerExit(Finger finger)
    {
        FingerExitEvent?.Invoke(finger, this);
    }
}