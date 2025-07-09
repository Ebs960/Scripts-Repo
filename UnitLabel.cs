using UnityEngine;
using TMPro;

public class UnitLabel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private TextMeshProUGUI ownerNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Vector3 offset = new Vector3(0, 2.5f, 0); // Offset above unit

    private Transform target;
    private Camera mainCam;

    public void Initialize(Transform targetTransform, string unitName, string ownerName, int currentHP, int maxHP)
    {
        target = targetTransform;
        mainCam = Camera.main;
        UpdateLabel(unitName, ownerName, currentHP, maxHP);
    }

    public void UpdateLabel(string unitName, string ownerName, int currentHP, int maxHP)
    {
        if (unitNameText != null) unitNameText.text = unitName;
        if (ownerNameText != null) ownerNameText.text = ownerName;
        if (healthText != null) healthText.text = $"HP: {currentHP}/{maxHP}";
    }

    void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return; // Still no camera, can't do anything.

        // Follow the target
        transform.position = target.position + offset;
        
        // Make the label face the camera and remain upright relative to the camera's view.
        transform.rotation = mainCam.transform.rotation;
    }
} 