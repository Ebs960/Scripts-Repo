using UnityEngine;

public class AncientRuin : MonoBehaviour
{
    public bool isExplored = false;

    public void Explore(Civilization explorer)
    {
        if (isExplored) return;

        isExplored = true;
        
        // Give a random reward
        int rewardType = Random.Range(0, 3);
        switch (rewardType)
        {
            case 0:
                // Grant technology
                // TechData randomTech = GetRandomUnresearchedTech(explorer);
                // if (randomTech != null)
                // {
                //     explorer.UnlockTech(randomTech);
                //     Debug.Log($"{explorer.civName} discovered the secrets of {randomTech.techName}!");
                // }
                break;
            case 1:
                // Grant resources
                int goldAmount = Random.Range(50, 201);
                explorer.AddGold(goldAmount);
                Debug.Log($"{explorer.civName} found {goldAmount} gold in the ruins!");
                break;
            case 2:
                // Grant a free unit
                // UnitData randomUnit = GetRandomUnit(explorer);
                // if (randomUnit != null)
                // {
                //     // Spawn unit near the ruin
                //     Debug.Log($"{explorer.civName} found a friendly {randomUnit.unitName} in the ruins!");
                // }
                break;
        }

        // Optionally, destroy the ruin after exploration
        Destroy(gameObject);
    }
}
