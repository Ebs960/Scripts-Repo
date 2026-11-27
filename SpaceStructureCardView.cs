using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One space structure in the infrastructure panel, read from a live SpaceImprovementInstance.</summary>
public class SpaceStructureCardView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI iconFallbackText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private TextMeshProUGUI statusText;

    private Button actionButton;
    private Action action;
    private float baseHeight;

    public void Bind(SpaceImprovementInstance instance)
    {
        var data = SpaceImprovementRegistry.GetData(instance.improvementDataId);
        var details = new List<string>
        {
            SpaceMapSelectionInfo.CivilizationName(SpaceMapSelectionInfo.ResolveCivilization(instance.ownerCivilizationId)),
            SpaceMapSelectionInfo.SectorName(instance.spaceTileIndex),
            data != null ? $"Health {instance.currentHealth}/{data.maximumHealth} · Defense {data.defense}" : $"Health {instance.currentHealth}"
        };
        if (data != null)
        {
            if (data.goldPerTurn != 0) details.Add($"Gold {data.goldPerTurn:+0;-0}/turn");
            if (data.sciencePerTurn != 0) details.Add($"Science {data.sciencePerTurn:+0;-0}/turn");
            if (data.productionPerTurn != 0) details.Add($"Production {data.productionPerTurn:+0;-0}/turn");
            if (data.extractsResources) details.Add($"Extraction {data.extractionPerTurn}/turn {data.requiredResourceType}".TrimEnd());
            if (data.visionRange > 0) details.Add($"Vision range {data.visionRange}");
            if (data.detectionStrength > 0) details.Add($"Detection {data.detectionStrength}");
            if (data.reconRange > 0) details.Add($"Recon range {data.reconRange}");
            if (data.repairPerTurn > 0) details.Add($"Repairs {data.repairPerTurn}/turn · Range {data.repairRange} · Capacity {data.repairCapacity}");
            if (data.canProduceShips)
            {
                var types = (data.permittedShipTypes ?? Array.Empty<CombatUnitData>())
                    .Where(ship => ship != null).Select(ship => ship.unitName).ToArray();
                details.Add(types.Length == 0 ? "Ship production" : $"Ship production: {string.Join(", ", types)}");
            }
            if (data.productionBonus != 0) details.Add($"Production bonus {data.productionBonus:+0;-0}");
            if (data.canAttack) details.Add($"Space attack {data.spaceAttack} · Range {data.attackRange} · {data.attacksPerTurn}/turn");
            if (data.contributesToBlockadeControl) details.Add("Contributes to blockade control");
            if (data.tradeCapability?.providesTradeNode == true) details.Add("Trade node");
            if (data.claimsTile) details.Add("Claims sector");
            if (data.shipStorageCapacity > 0) details.Add($"Ships stored {instance.storedShips}/{data.shipStorageCapacity}");
            if (data.cargoStorageCapacity > 0) details.Add($"Cargo stored {instance.storedCargo}/{data.cargoStorageCapacity}");
        }
        BindRow(SpaceMapSelectionInfo.ImprovementName(instance, data), data?.icon,
            string.Join("\n", details), SpaceMapSelectionInfo.ImprovementStatus(instance), null);
    }

    public void BindRow(string title, Sprite sprite, string details, string status, Action onClick)
    {
        if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (iconFallbackText != null)
        {
            iconFallbackText.gameObject.SetActive(sprite == null);
            iconFallbackText.text = string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1).ToUpperInvariant();
        }
        if (nameText != null) nameText.text = title;
        if (statusText != null) statusText.text = status;
        if (detailText != null)
        {
            detailText.text = details;
            var rect = (RectTransform)transform;
            if (baseHeight <= 0) baseHeight = rect.sizeDelta.y;
            var detailRect = detailText.rectTransform;
            float width = Mathf.Max(1, rect.rect.width * (detailRect.anchorMax.x - detailRect.anchorMin.x));
            float height = detailText.GetPreferredValues(details, width, float.PositiveInfinity).y;
            float cardHeight = Mathf.Max(baseHeight, height / Mathf.Max(.1f, detailRect.anchorMax.y - detailRect.anchorMin.y));
            if (!Mathf.Approximately(rect.rect.height, cardHeight))
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardHeight);
        }

        action = onClick;
        if (actionButton == null && action != null && statusText != null)
        {
            // The template's status label is the action target for project cards.
            actionButton = statusText.GetComponent<Button>() ?? statusText.gameObject.AddComponent<Button>();
            actionButton.targetGraphic = statusText;
            actionButton.onClick.AddListener(() => action?.Invoke());
        }
        if (actionButton != null)
        {
            actionButton.enabled = action != null;
            statusText.raycastTarget = action != null;
        }
    }
}
