using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace petabytes.LTCLight
{
    public enum LTCLightMode
    {
        DiffuseOnly,
        SpecularOnly,
        DiffuseAndSpecular,
    }

    [ExecuteAlways]
    public partial class LTCLight : MonoBehaviour
    {
        // Only set Type in subclass at initialize,
        // can't be changed at runtime
        public LTCLightType Type
        {
            get => type_;
            protected set
            {
                if (node_ != null) node_.Value.properties.flags |= (int)value;
                type_ = value;
            }
        }

        public float Range
        {
            get => range_;
            set
            {
                range_ = value;
                if (node_ != null) node_.Value.properties.range = range_;
            }
        }

        public float Radius
        {
            get => radius_;
            set
            {
                radius_ = Mathf.Max(0, value);
                if (node_ != null) node_.Value.properties.radius = radius_;
            }
        }

        public Color Color
        {
            get => color_;
            set
            {
                if (node_ != null) node_.Value.properties.color = value;
                color_ = value;
            }
        }

        public Vector2 Intensity
        {
            get => intensity_;
            set
            {
                intensity_ = Vector2.Max(Vector2.zero, value);
                if (node_ != null) node_.Value.properties.intensity = intensity_;
            }
        }

        public bool DoubleSided
        {
            get => doubleSided_;
            set
            {
                if (node_ != null)
                {
                    SetFlagBoolBit(value, (int)LTCFlag.IsDoubleSided, ref node_.Value.properties.flags);
                }
                doubleSided_ = value;
            }
        }

        public bool UseWorldPosition
        {
            get => useWorldPosition_;
            set
            {
                useWorldPosition_ = value;
                if (node_ != null)
                {
                    node_.Value.useWorldPosition = useWorldPosition_;
                    node_.Value.lightTransform.hasChanged = true;
                }
            }
        }

        public LTCLightMode LightMode
        {
            get => lightMode_;
            set
            {
                lightMode_ = value;
                if (node_ == null)
                    return;

                switch (value)
                {
                    case LTCLightMode.DiffuseOnly:
                        SetFlagBoolBit(true, (int)LTCFlag.IsDiffuse, ref node_.Value.properties.flags);
                        SetFlagBoolBit(false, (int)LTCFlag.IsSpecular, ref node_.Value.properties.flags);
                        break;
                    case LTCLightMode.SpecularOnly:
                        SetFlagBoolBit(false, (int)LTCFlag.IsDiffuse, ref node_.Value.properties.flags);
                        SetFlagBoolBit(true, (int)LTCFlag.IsSpecular, ref node_.Value.properties.flags);
                        break;
                    case LTCLightMode.DiffuseAndSpecular:
                        SetFlagBoolBit(true, (int)LTCFlag.IsDiffuse, ref node_.Value.properties.flags);
                        SetFlagBoolBit(true, (int)LTCFlag.IsSpecular, ref node_.Value.properties.flags);
                        break;
                }
            }
        }

        [SerializeField]
        private LTCLightType type_;
        [SerializeField]
        private float radius_ = 1;
        [SerializeField]
        private Color color_ = Color.white;
        [SerializeField]
        private Vector2 intensity_ = new Vector2(5, 5);
        [SerializeField]
        private bool doubleSided_ = false;
        [SerializeField]
        private bool useWorldPosition_ = false;
        [SerializeField]
        private LTCLightMode lightMode_ = LTCLightMode.DiffuseAndSpecular;
        [SerializeField]
        private float range_ = 5;

        protected LinkedListNode<LTCLightInfo> node_ = null;

        protected virtual IEnumerator Start()
        {
            yield return CheckAndRegister();
        }

        private void OnEnable()
        {
            RegisterSelf();
        }

        private void OnDisable()
        {
            UnregisterSelf();
        }

        public static void SetFlagBoolBit(bool cond, int bitMask, ref int bitOut)
        {
            bitMask = bitMask << 16;
            if (cond)
                bitOut |= bitMask;
            else
                bitOut &= ~(bitMask);
        }

        public static LTCLightType GetLightTypeFromFlag(int flag)
        {
            return (LTCLightType)(flag & 0xFFFF);
        }

        protected virtual void OnValidate()
        {
            intensity_ = Vector2.Max(intensity_, Vector2.zero);
            radius_ = Mathf.Max(radius_, 0);
            range_ = Mathf.Max(range_, 0);
        }

        public void RegisterSelf()
        {
            // Current light may already registered when LTCManager becomes enabled
            if (LTCLightManager.IsInitialized && node_ == null)
                node_ = LTCLightManager.Instance.RegisterLight(this);
            transform.hasChanged = true;
        }

        public void UnregisterSelf()
        {
            if (LTCLightManager.IsInitialized && node_ != null)
            {
                LTCLightManager.Instance.UnregisterLight(node_);
                node_ = null;
            }
        }

        private IEnumerator CheckAndRegister()
        {
            while (!LTCLightManager.IsInitialized)
                yield return null;
            RegisterSelf();
        }

        public void OnManagerDisabled()
        {
            node_ = null;
            StopCoroutine(nameof(CheckAndRegister));
            if (gameObject.activeInHierarchy)
                StartCoroutine(CheckAndRegister());
        }
    }
}
