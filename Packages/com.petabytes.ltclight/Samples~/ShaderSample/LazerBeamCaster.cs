using petabytes.LTCLight;
using UnityEngine;

[ExecuteAlways]
public class LazerBeamCaster : MonoBehaviour
{
    private LineRenderer lr;
    private LTCLinearLight ll;
    RaycastHit[] hits;
    Vector3[] lazerPositions;

    static float kMaxRayDist = 1000;

    void Awake()
    {
        hits = new RaycastHit[1];
        lazerPositions = new Vector3[2];
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lr = GetComponent<LineRenderer>();
        ll = GetComponent<LTCLinearLight>();
    }

    // Update is called once per frame
    void Update()
    {
        Ray r = new Ray(transform.position, transform.forward);
        int numHit = Physics.RaycastNonAlloc(r, hits);
        lazerPositions[0] = r.origin;

        if (numHit > 0)
        {
            lazerPositions[1] = r.origin + r.direction * hits[0].distance;
        }
        else
        {
            lazerPositions[1] = r.origin + r.direction * kMaxRayDist;
        }
        lr.SetPositions(lazerPositions);
        ll.StartPoint = lazerPositions[0];
        ll.EndPoint = lazerPositions[1];
        ll.Color = lr.sharedMaterial.color;
    }
}
