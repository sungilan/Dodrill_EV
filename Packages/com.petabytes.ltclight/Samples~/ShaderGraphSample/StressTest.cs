using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StressTest : MonoBehaviour
{
    public List<GameObject> prefab;
    public List<GameObject> texturePrefabs;
    
    System.Random random = new System.Random();
    
    List<GameObject> lights = new List<GameObject>();
    // Update is called once per frame

    void Start()
    {
        StartCoroutine(UpdateLights());
    }
    IEnumerator UpdateLights()
    {
        while (true)
        {
            yield return new WaitForSeconds(1);

            int type = random.Next(0, 2);
            GameObject light;
            if (type == 0)
            {
                int rand = random.Next(0, prefab.Count);
                light = Instantiate(prefab[rand]);
            }
            else
            {
                int rand = random.Next(0, texturePrefabs.Count);
                light = Instantiate(texturePrefabs[rand]);
            }
            
            light.transform.position = transform.position + new Vector3(random.Next(-10, 10), 0, random.Next(-10, 10));
            light.transform.Rotate(new Vector3(0, random.Next(0, 360), 0));
            lights.Add(light);

            if (lights.Count > 16)
            {
                while (lights.Count > 5)
                {
                    DestroyImmediate(lights[0]);
                    lights.RemoveAt(0);
                }
            }
        }
    }
}
