using Autohand;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Autohand {
    public class HandTouchButton : MonoBehaviour {
        [NaughtyAttributes.HideIf("startUnpress")]
        public bool startPress = false;
        [NaughtyAttributes.HideIf("startPress")]
        public bool startUnpress = false;
        public HandTouchEvent touchEvent;
        public Transform button;
        public Vector3 pressOffset;
        public Color unpressColor = Color.white;
        public Color pressColor = Color.white;

        public bool toggle = true;

        [Space]
        public UnityHandEvent OnPressed;
        public UnityHandEvent OnUnpressed;

        bool pressed = false;
        MeshRenderer buttonRenderer;
        Image image;

        private void Start() {
            if(startPress)
                PressButton(null);
            else if(startUnpress)
                ReleaseButton(null);

            buttonRenderer = button.GetComponent<MeshRenderer>();
            if(buttonRenderer == null)
            {
                image = button.GetComponent<Image>();
            }
            touchEvent.HandStartTouchEvent += OnTouch;
            touchEvent.HandStopTouchEvent += OnUntouch;
        }

        //void OnEnable() {
        //    touchEvent.HandStartTouchEvent += OnTouch;
        //    touchEvent.HandStopTouchEvent += OnUntouch;
        //}
        //void OnDisable() {
        //    touchEvent.HandStartTouchEvent -= OnTouch;
        //    touchEvent.HandStopTouchEvent -= OnUntouch;
        //}

        void OnTouch(Hand hand) {
            if(toggle) {
                if(!pressed)
                    PressButton(hand);
                else if(pressed)
                    ReleaseButton(hand);
            }
            else if(!pressed)
                PressButton(hand);
        }
        void OnUntouch(Hand hand) {
            if(pressed && !toggle)
                ReleaseButton(hand);
        }
        
        void PressButton(Hand hand) {
            if(!pressed)
                button.localPosition += pressOffset;
            pressed = true;
            OnPressed?.Invoke(hand); 
            if(buttonRenderer != null && buttonRenderer.material != null)
                buttonRenderer.material.color = pressColor;
            if(image != null)
                image.color = pressColor;
        }

        void ReleaseButton(Hand hand) {
            if(pressed)
                button.localPosition -= pressOffset;
            pressed = false; 
            OnUnpressed?.Invoke(hand);
            if(buttonRenderer != null && buttonRenderer.material != null)
                buttonRenderer.material.color = unpressColor;
            if (image != null)
                image.color = unpressColor;
        }
    }
}