using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace petabytes.LTCLight
{
    public class LTCTexturedLight : LTCLight
    {
        public Texture Texture
        {
            get => _texture;
            set
            {
                if (_texture == value)
                    return;

                if (node_ != null)
                    LTCLightManager.Instance.UpdateLightTexture(node_, value, _texture);
                
                _texture = value;
            }
        }

        public bool IsRealTime
        {
            get => _isRealTime;
            set
            {
                _isRealTime = value;
                if (node_ != null)
                {
                    node_.Value.isRealTime = value;
                    LTCLightManager.Instance.UpdateLightTexture(node_, _texture, _texture);
                }
            }
        }
        
        public float Width
        {
            get => _width;
            set
            {
                if (value <= 0.01f)
                {
                    Debug.LogWarning("Texture width is too small or negative.");
                    return;
                }
                
                _width = value;
                GenerateVertices();
                UpdateGeomData();
            }
        }

        public float Height {
            get => _height;
            set
            {
                if (value <= 0.01f)
                {
                    Debug.LogWarning("Texture height is too small or negative.");
                    return;
                }

                _height = value;
                GenerateVertices();
                UpdateGeomData();
            }
        }

        public List<Vector4> GeomData
        {
            get => _geomData;
        }
        public List<Vector3> RectPoints
        {
            get => _points;
        }

        public float BarnDoorAngle
        {
            get => _barnDoorAngle;
            set
            {
                _barnDoorAngle = value;
                UpdateGeomData();
            }
        }

        public float BarnDoorLength
        {
            get => _barnDoorLength;
            set
            {
                _barnDoorLength = value;
                UpdateGeomData();
            }
        }

        // X axis, x extent
        // Y axis, y extent
        // Forward axis, barnDoorLength * sinTheta
        // Barn door xy extent, sinTheta, cosTheta
        private List<Vector4> _geomData = new List<Vector4>()
        {
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
        };

        private List<Vector3> _points = new List<Vector3>()
        {
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0),
            new Vector3(0.5f, 0.5f, 0)
        };
        [SerializeField] private float _width = 1;
        [SerializeField] private float _height = 1;
        [SerializeField] private Texture _texture;
        [SerializeField] private bool _isRealTime;
        [SerializeField] private float _barnDoorAngle = 0;
        [SerializeField] private float _barnDoorLength = 0.2f;

        void Awake()
        {
            Type = LTCLightType.Textured;
            UpdateGeomData();
        }

        void Update()
        {
            if (transform.hasChanged)
                UpdateGeomData();
        }

        void UpdateGeomData()
        {
            Vector4 xAxis = Vector3.right;
            Vector4 yAxis = Vector3.up;
            Vector4 fAxis = Vector3.forward;
            float xScale = 1;
            float yScale = 1;

            if (!UseWorldPosition)
            {
                xAxis = transform.right;
                yAxis = transform.up;
                fAxis = transform.forward;
                xScale = transform.lossyScale.x;
                yScale = transform.lossyScale.y;
            }
            
            xAxis.w = _width * xScale;
            _geomData[0] = xAxis;
            
            yAxis.w = _height * yScale;
            _geomData[1] = yAxis;
            
            fAxis.w = _barnDoorLength * Mathf.Sin(_barnDoorAngle * Mathf.Deg2Rad);
            _geomData[2] = fAxis;
            
            float cosTheta = Mathf.Cos(_barnDoorAngle * Mathf.Deg2Rad);
            float sinTheta = Mathf.Sin(_barnDoorAngle * Mathf.Deg2Rad);
            
            float bottomWidth = xAxis.w + _barnDoorLength * cosTheta * 2;
            float bottomHeight = yAxis.w + _barnDoorLength * cosTheta * 2;
            _geomData[3] = new Vector4(bottomWidth, bottomHeight, sinTheta, cosTheta);
            
            if (node_ != null)
            {
                LTCLightManager.Instance.UpdateLightVertices(node_, _geomData);   
            }       
        }
        
        // Start is called before the first frame update
        protected override IEnumerator Start()
        {
            yield return base.Start();
        }

        private void GenerateVertices()
        {
            float halfWidth = Width * 0.5f;
            float halfHeight = Height * 0.5f;
            
            _points = new List<Vector3>(4);
            _points.Add(new Vector3(halfWidth, -halfHeight, 0));
            _points.Add(new Vector3(-halfWidth, -halfHeight, 0));
            _points.Add(new Vector3(-halfWidth, halfHeight, 0));
            _points.Add(new Vector3(halfWidth, halfHeight, 0));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (UseWorldPosition)
                Gizmos.matrix = Matrix4x4.identity;
            else
                Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawLineStrip(_points.ToArray(), true);
            
            // Draw range sphere
            if (!UseWorldPosition)
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            
            Gizmos.DrawWireSphere(Vector3.zero, Range);
            // Draw barn doors
            float cosTheta = Mathf.Cos(_barnDoorAngle * Mathf.Deg2Rad);
            float sinTheta = Mathf.Sin(_barnDoorAngle * Mathf.Deg2Rad);
            float bottomWidth = _width * transform.lossyScale.x + _barnDoorLength * cosTheta * 2;
            float bottomHeight = _height * transform.lossyScale.y + _barnDoorLength * cosTheta * 2;
            // Vertex of bottom rect
            // Vector3 f = transform.forward, r = -transform.right, u = transform.up;
            Vector3 f = Vector3.forward, r = Vector3.right, u = Vector3.up;
            Vector3[] bottomPoints = new Vector3[4];
            bottomPoints[0] = r * bottomWidth * 0.5f - u * bottomHeight * 0.5f + f * _barnDoorLength * sinTheta;
            bottomPoints[1] = -r * bottomWidth * 0.5f - u * bottomHeight * 0.5f + f * _barnDoorLength * sinTheta;
            bottomPoints[2] = -r * bottomWidth * 0.5f + u * bottomHeight * 0.5f + f * _barnDoorLength * sinTheta;
            bottomPoints[3] = r * bottomWidth * 0.5f + u * bottomHeight * 0.5f + f * _barnDoorLength * sinTheta;
            
            Gizmos.DrawLineStrip(bottomPoints, true);
            for (int i = 0; i < 4; i++)
                Gizmos.DrawLine(Vector3.Scale(_points[i], transform.lossyScale), bottomPoints[i]);
        }
    }
}
