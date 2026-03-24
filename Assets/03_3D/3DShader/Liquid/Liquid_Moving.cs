using UnityEngine;

[ExecuteInEditMode]
public class Liquid_Moving : MonoBehaviour
{
    private Mesh _mesh;
    private Renderer _rend;

    [SerializeField] private float maxWobble = 0.05f;
    [SerializeField] private float wobbleSpeed = 1f;
    [SerializeField] private float recovery = 1f;
    private float _deltaTime = 0.0f;
    private float _time = 0.5f;

    // Liquid Shader
    private Shader _liquidShader = null;

    // Variables about Wobble
    private float _wobbleAmountX;
    private float _wobbleAmountZ;
    private float _wobbleAmountToAddX;
    private float _wobbleAmountToAddZ;
    private float _pulse;
    private Vector3 _velocity;
    private Vector3 _pos;
    private Vector3 _lastPos;
    private Vector3 _lastRot;
    private Vector3 _angularVelocity;

    // Property ID
    private int _wobbleX = -1;
    private int _wobbleZ = -1;
    private int _boundsCenter = -1;

    void Awake()
    {
        Initialize();
    }

    void Update()
    {
        if (_rend.sharedMaterial.shader != _liquidShader) return;

        _deltaTime = Time.deltaTime;
        _time += _deltaTime;

        if (_deltaTime != 0)
        {
            _wobbleAmountToAddX = Mathf.Lerp(_wobbleAmountToAddX, 0, (_deltaTime * recovery));
            _wobbleAmountToAddZ = Mathf.Lerp(_wobbleAmountToAddZ, 0, (_deltaTime * recovery));

            // Make a Sine Wave of the decreasing wobble
            _pulse = 2 * Mathf.PI * wobbleSpeed;
            _wobbleAmountX = _wobbleAmountToAddX * Mathf.Sin(_pulse * _time);
            _wobbleAmountZ = _wobbleAmountToAddZ * Mathf.Sin(_pulse * _time);

            // send data to Liquid Shader
            _rend.sharedMaterial.SetFloat(_wobbleX, _wobbleAmountX);
            _rend.sharedMaterial.SetFloat(_wobbleZ, _wobbleAmountZ);

            // Velocity
            var tf = transform;
            var transformPosition = tf.position;
            var transformRotAngles = tf.rotation.eulerAngles;
            _velocity = (_lastPos - transformPosition) / Time.deltaTime;
            _angularVelocity = transformRotAngles - _lastRot;

            // Add clamped velocity to wobble
            _wobbleAmountToAddX += Mathf.Clamp((_velocity.x + (_angularVelocity.z * 0.2f)) * maxWobble, -maxWobble, maxWobble);
            _wobbleAmountToAddZ += Mathf.Clamp((_velocity.z + (_angularVelocity.x * 0.2f)) * maxWobble, -maxWobble, maxWobble);

            // Keep last position
            _lastPos = transformPosition;
            _lastRot = transformRotAngles;

            // Initialize sing input 
            if (_pulse * _time > Mathf.PI * 2.0f)
            {
                _time = 0.0f;
            }
        }
        UpdatePos(_deltaTime);
    }

    private void Initialize()
    {
        GetMeshAndRend();
        SetMaterial();
        GetPropertyID();
    }

    private void GetMeshAndRend()
    {
        if (_mesh == null)
            _mesh = GetComponent<MeshFilter>().sharedMesh;
        if (_rend == null)
            _rend = GetComponent<Renderer>();
    }

    private void SetMaterial()
    {
        _liquidShader = Shader.Find("SSS/Liquid");
    }

    private void GetPropertyID()
    {
        _wobbleX = Shader.PropertyToID("_WobbleX");
        _wobbleZ = Shader.PropertyToID("_WobbleZ");
        _boundsCenter = Shader.PropertyToID("_BoundsCenter");
    }

    private void UpdatePos(float deltaTime)
    {
        _rend.sharedMaterial.SetVector(_boundsCenter, new Vector3(_mesh.bounds.center.x, _mesh.bounds.center.y, _mesh.bounds.center.z));
    }
}