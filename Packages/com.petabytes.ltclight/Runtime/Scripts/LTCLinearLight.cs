using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace petabytes.LTCLight
{
    public class LTCLinearLight : LTCLight
    {
        public Vector3 StartPoint
        {
            get => startPoint_;
            set
            {
                if (!LTCLightManager.IsInitialized)
                {
                    Debug.LogError("LTC manager not initialized!");
                    return;
                }
                if ((value - endPoint_).magnitude < 0.001f) return;
                startPoint_ = value;

                if (node_ != null)
                    LTCLightManager.Instance.UpdateLightVertices(node_, new List<Vector3> { startPoint_, endPoint_ });
            }
        }

        public Vector3 EndPoint
        {
            get => endPoint_;
            set
            {
                if (!LTCLightManager.IsInitialized)
                {
                    Debug.LogError("LTC manager not initialized!");
                    return;
                }
                if ((value - startPoint_).magnitude < 0.001f) return;
                endPoint_ = value;
                if (node_ != null)
                    LTCLightManager.Instance.UpdateLightVertices(node_, new List<Vector3> { startPoint_, endPoint_ });
            }
        }

        [SerializeField]
        private Vector3 startPoint_;
        [SerializeField]
        private Vector3 endPoint_;

        void Awake()
        {
            Type = LTCLightType.Linear;
            startPoint_ = new Vector3(-1, 0, 0);
            endPoint_ = new Vector3(1, 0, 0);
        }

        protected override IEnumerator Start()
        {
            yield return base.Start();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (UseWorldPosition)
                Gizmos.matrix = Matrix4x4.identity;
            else
                Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawLineStrip(new Vector3[] { startPoint_, endPoint_ }, true);
            DrawWireCapsule(startPoint_, endPoint_, Range, Color.yellow);
        }

    public void DrawWireCapsule(Vector3 _pos1, Vector3 _pos2, float _radius, Color _color = default(Color))
    {
        // Draw in world space
        using (new Handles.DrawingScope(_color, Matrix4x4.identity))
        {
            if (!UseWorldPosition)
            {
                _pos1 = transform.TransformPoint(_pos1);
                _pos2 = transform.TransformPoint(_pos2);    
            }
            
            Vector3 dir = _pos2 - _pos1;
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir);

            Vector3 up = rot * Vector3.up;
            Vector3 left = rot * Vector3.left;
            Vector3 forward = rot * Vector3.forward;
            Vector3 back = rot * Vector3.back;
            
            //draw sideways
            Handles.DrawWireArc(_pos1, up, back, 180, _radius);
            Handles.DrawLine(-_radius * up + _pos1, -_radius * up + _pos2);
            Handles.DrawLine(_radius * up + _pos1, _radius * up +  _pos2);
            Handles.DrawWireArc(_pos2, up, back, -180, _radius);
            //draw frontways
            Handles.DrawWireArc(_pos1, forward, up, 180, _radius);
            Handles.DrawLine(-_radius * forward + _pos1, -_radius * forward + _pos2);
            Handles.DrawLine(_radius * forward + _pos1, _radius * forward + _pos2);
            Handles.DrawWireArc(_pos2, forward, up, -180, _radius);
            //draw center
            Handles.DrawWireDisc(_pos1, left, _radius);
            Handles.DrawWireDisc(_pos2, left, _radius);

        }
    }
#endif
    }
}
