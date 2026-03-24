using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace petabytes.LTCLight
{
    public partial class LTCPolygonLight : LTCLight
    {
        public enum PolygonPreset
        {
            Rectangle = 0,
            Pentagon,
            Hexagon,
            Pentagram,
            Custom,
            [InspectorName(null)]
            PresetCount
        }
        
        public List<Vector3> PolygonPoints
        {
            get => polygonPoints_;
            set
            {
                if (!LTCLightManager.IsInitialized)
                {
                    Debug.LogError("LTC manager not initialized!");
                    return;
                }
                
                if (value.Count < 3)
                {
                    Debug.LogError("Polygon light vertex count must be at least 3!");
                    return;
                }
#if UNITY_EDITOR
                if (setPointsMannually_)
                    polygonPreset_ = PolygonPreset.Custom;
#endif
                if (node_ != null)
                {
                    LTCLightManager.Instance.UpdateLightVertices(node_, value);   
                }
                polygonPoints_ = value;   
            }
        }

        public void SetPolygonPreset(PolygonPreset preset)
        {
            if (preset >= PolygonPreset.PresetCount)
            {
                Debug.LogError("Polygon preset count exceeds limit!");
                return;
            }
            if (preset == PolygonPreset.Custom)
                return;
#if UNITY_EDITOR
            setPointsMannually_ = false;
#endif
            List<Vector3> newVertices = new List<Vector3>();
            newVertices.AddRange(presetVertices_[(int)preset]);
            PolygonPoints = newVertices;
#if UNITY_EDITOR
            setPointsMannually_ = true;
#endif
        }

        private static readonly List<Vector3> presetRectangle_ = new List<Vector3>()
        {
            new (1, -1, 0),
            new (-1, -1, 0),
            new (-1, 1, 0),
            new (1, 1, 0),
        };

        private static readonly List<Vector3> presetPentagon_ = new List<Vector3>()
        {
            new (0,1,0),
            new (0.9510565162951535F, 0.30901699437494745F, 0),
            new (0.5877852522924732F, -0.8090169943749473F, 0),
            new (-0.587785252292473F, -0.8090169943749475F, 0),
            new (-0.9510565162951536F, 0.30901699437494723F, 0)
        };

        private static readonly List<Vector3> presetHexagon_ = new List<Vector3>()
        {
            new(0.0F, 1.0F, 0),
            new(0.8660254037844386F, 0.5000000000000001F, 0),
            new(0.8660254037844387F, -0.4999999999999998F, 0),
            new(1.2246467991473532e-16F, -1.0F, 0),
            new(-0.8660254037844385F, -0.5000000000000004F, 0),
            new(-0.8660254037844386F, 0.5000000000000001F, 0),
        };

        private static readonly List<Vector3> presetPentagram_ = new List<Vector3>()
        {
            new(0.0F,1.0F,0),
            new(0.29389262614623657F,0.4045084971874737F,0),
            new(0.9510565162951535F,0.30901699437494745F,0),
            new(0.47552825814757677F,-0.15450849718747378F,0),
            new(0.5877852522924732F,-0.8090169943749473F,0),
            new(6.123233995736766e-17F,-0.5F,0),
            new(-0.5877852522924734F,-0.8090169943749472F,0),
            new(-0.47552825814757677F,-0.15450849718747378F,0),
            new(-0.9510565162951536F,0.30901699437494723F,0),
            new(-0.2938926261462367F,0.40450849718747367F,0),
        };

        private static readonly List<Vector3>[] presetVertices_ = new List<Vector3>[(int)PolygonPreset.PresetCount]
        {
            presetRectangle_,
            presetPentagon_,
            presetHexagon_,
            presetPentagram_,
            null
        };
        
        // What if polygon points changed at runtime
        [SerializeField]
        private List<Vector3> polygonPoints_ = new List<Vector3>();

        // Start is called before the first frame update
        void Awake()
        {
            Type = LTCLightType.Polygon;
        }

        protected override IEnumerator Start()
        {
            yield return base.Start();
#if UNITY_EDITOR
            SetPolygonPreset(polygonPreset_);
#endif
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (UseWorldPosition)
                Gizmos.matrix = Matrix4x4.identity;
            else
                Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawLineStrip(polygonPoints_.ToArray(), true);

            if (!UseWorldPosition)
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireSphere(Vector3.zero, Range);
        }
    }
}