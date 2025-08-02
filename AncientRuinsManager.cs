using System.Collections.Generic;
using UnityEngine;

public class AncientRuinsManager : MonoBehaviour
{
    public static AncientRuinsManager Instance { get; private set; }

    public GameObject ruinPrefab;
    public int numberOfRuinsToSpawn = 10;

    private List<AncientRuin> ruins = new List<AncientRuin>();
    private PlanetGenerator planetGenerator;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SpawnRuins(PlanetGenerator generator)
    {
        planetGenerator = generator;
        if (planetGenerator == null) return;

        for (int i = 0; i < numberOfRuinsToSpawn; i++)
        {
            int tileIndex = Random.Range(0, planetGenerator.Grid.TileCount);
            HexTileData tileData = planetGenerator.GetTileData(tileIndex);

            if (tileData.biome != Biome.Ocean && tileData.biome != Biome.Sea)
            {
                Vector3 position = planetGenerator.Grid.GetTile(tileIndex).center;
                GameObject ruinGO = Instantiate(ruinPrefab, position, Quaternion.identity, transform);
                ruins.Add(ruinGO.GetComponent<AncientRuin>());
            }
        }
    }
}
