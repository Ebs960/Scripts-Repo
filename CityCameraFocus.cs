// Assets/Scripts/UI/CityCameraFocus.cs
using UnityEngine;

public class CityCameraFocus : MonoBehaviour
{
    public static CityCameraFocus Instance { get; private set; }

    [SerializeField] private float focusDistance = 25f;
    [SerializeField] private float focusHeight = 14f;

    private void Awake()
    {
        Instance = this;
    }

    public void FocusCity(City city)
    {
        if (city == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 cityPos = city.transform.position;
        Vector3 targetPos = cityPos + Vector3.up * focusHeight - cam.transform.forward * focusDistance;
        cam.transform.position = targetPos;
        cam.transform.LookAt(cityPos);
    }
}
