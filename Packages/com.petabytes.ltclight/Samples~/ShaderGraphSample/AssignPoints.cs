using System.Collections;
using System.Collections.Generic;
using petabytes.LTCLight;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

public class AssignPoints : MonoBehaviour
{
    LTCPolygonLight light;
    Random random = new Random();
    
    // Start is called before the first frame update
    void Start()
    {
        light = GetComponent<LTCPolygonLight>();
        StartCoroutine(updatePoints());
    }

    // Update is called once per frame
    IEnumerator updatePoints()
    {
        while (true)
        {
            yield return new WaitForSeconds(2);
            int count = random.Next(3, 20);
            List<Vector3> vertices = new List<Vector3>(count);
            for (int i = 0; i < count; ++i)
            {
                float rad = math.PI * 2f / (float)count * i;
                var v = new Vector3(math.sin(rad), math.cos(rad), 0) * UnityEngine.Random.Range(0.5f, 1.5f);
                vertices.Add(v);
            }
        
            light.PolygonPoints = vertices;    
        }
        
    }
}
