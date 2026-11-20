using UnityEngine;
using AYellowpaper.SerializedCollections;

public class AnchorPreview : MonoBehaviour
{
    public SerializedDictionary<string, GameObject> carObjects;

    public void ChangeCar(string carName)
    {
        foreach(var obj in carObjects)
        {
            obj.Value.SetActive(false);
        }

        if(carObjects.TryGetValue(carName, out var go))
        {
            go.SetActive(true);
        }
    }
}