using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Inspector-wired campaign panel. Intentionally contains no combat-stat fields.</summary>
public sealed class BandPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText, populationText, foodText, starvationText, stateText, movementText, structuresText, productionText, garrisonText;
    [SerializeField] private Button packButton, encampButton, forageButton;
    private Band band;

    public void Show(Band value)
    {
        band = value;
        gameObject.SetActive(value != null);
        if (value != null) Refresh();
    }

    public void Pack() { if (band != null) band.Pack(); Refresh(); }
    public void Encamp() { if (band != null) band.Encamp(); Refresh(); }
    public void Forage() { if (band != null) band.Forage(); Refresh(); }

    public void Refresh()
    {
        if (band == null) return;
        Set(titleText, band.Data != null ? band.Data.displayName : "Band");
        Set(populationText, $"Population: {band.Population}");
        Set(foodText, $"Food Reserve: {band.FoodReserve} / {band.FoodCapacity}\nFood Use: {band.FoodRequiredPerTurn} / turn");
        int collapse = band.Data != null ? band.Data.collapseAfterStarvationTurns : 8;
        Set(starvationText, band.IsStarving ? $"STARVING\n{band.ConsecutiveStarvationTurns} / {collapse} turns" : $"Starvation: 0 / {collapse}");
        Set(stateText, band.State.ToString());
        Set(movementText, $"Movement: {band.CurrentMovePoints}");
        Set(structuresText, "Structures\n" + string.Join("\n", band.BuiltStructures.Where(x => x != null).Select(x => "• " + x.structureName)));
        Set(productionText, band.QueuedStructure != null ? $"Producing: {band.QueuedStructure.structureName} ({band.ProductionProgress}/{band.QueuedStructure.productionCost})" : "Production: idle");
        Set(garrisonText, "Garrison\n" + string.Join("\n", band.Garrison.Where(x => x != null).Select(x => "• " + x.UnitName)));
        if (packButton != null) packButton.interactable = band.State == BandState.Encamped;
        if (encampButton != null) encampButton.interactable = band.State == BandState.Packed;
        if (forageButton != null) forageButton.interactable = band.CurrentMovePoints > 0;
    }

    private static void Set(TMP_Text target, string value) { if (target != null) target.text = value; }
}
