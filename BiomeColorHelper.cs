using UnityEngine;

public static class BiomeColorHelper
{
    public static Color GetMinimapColor(Biome b)
    {
        return b switch {
            Biome.Ocean => new Color(0.0f,0.3f,0.6f,1f),
            Biome.Seas => new Color(0.02f,0.35f,0.65f,1f),
            Biome.Coast => new Color(0.05f,0.45f,0.7f,1f),
            Biome.Plains => new Color(0.5f,0.8f,0.4f,1f),
            Biome.Grassland => new Color(0.45f,0.75f,0.35f,1f),
            Biome.Forest => new Color(0.16f,0.5f,0.16f,1f),
            Biome.Taiga => new Color(0.1f,0.45f,0.12f,1f),
            Biome.Jungle => new Color(0.12f,0.6f,0.2f,1f),
            Biome.Rainforest => new Color(0.08f,0.55f,0.18f,1f),
            Biome.Desert => new Color(0.9f,0.8f,0.5f,1f),
            Biome.Savannah => new Color(0.8f,0.7f,0.4f,1f),
            Biome.Mountain => new Color(0.5f,0.5f,0.5f,1f),
            Biome.Glacier => new Color(0.85f,0.9f,0.95f,1f),
            Biome.Arctic => new Color(0.95f,0.95f,0.98f,1f),
            Biome.IcicleField => new Color(0.9f,0.92f,0.98f,1f),
            Biome.CryoForest => new Color(0.7f,0.85f,0.9f,1f),
            Biome.Volcanic => new Color(0.45f,0.1f,0.05f,1f),
            Biome.Steam => new Color(0.6f,0.3f,0.2f,1f),
            Biome.Ashlands => new Color(0.45f,0.4f,0.38f,1f),
            Biome.CharredForest => new Color(0.15f,0.12f,0.1f,1f),
            Biome.Scorched => new Color(0.5f,0.25f,0.05f,1f),
            Biome.Floodlands => new Color(0.4f,0.5f,0.35f,1f),
            Biome.Hellscape => new Color(0.5f,0.05f,0.05f,1f),
            Biome.Brimstone => new Color(0.6f,0.2f,0.05f,1f),
            Biome.MoonDunes => new Color(0.6f,0.6f,0.6f,1f),
            Biome.MoonCraters => new Color(0.5f,0.5f,0.5f,1f),
            Biome.MercuryPlains => new Color(0.45f,0.45f,0.45f,1f),
            Biome.MercuryBasalt => new Color(0.4f,0.4f,0.4f,1f),
            Biome.MercuryScarp => new Color(0.35f,0.35f,0.35f,1f),
            Biome.PlutoCryo => new Color(0.55f,0.6f,0.65f,1f),
            Biome.PlutoTholins => new Color(0.45f,0.35f,0.25f,1f),
            Biome.MartianRegolith => new Color(0.8f,0.35f,0.25f,1f),
            Biome.MartianCanyon => new Color(0.6f,0.35f,0.25f,1f),
            Biome.MartianPolarIce => new Color(0.9f,0.9f,0.95f,1f),
            Biome.TitanLakes => new Color(0.05f,0.25f,0.2f,1f),
            Biome.TitanDunes => new Color(0.45f,0.35f,0.2f,1f),
            Biome.TitanIce => new Color(0.6f,0.65f,0.7f,1f),
            Biome.EuropaIce => new Color(0.85f,0.9f,0.95f,1f),
            Biome.EuropaRidges => new Color(0.7f,0.8f,0.85f,1f),
            Biome.IoVolcanic => new Color(0.6f,0.2f,0.05f,1f),
            Biome.IoSulfur => new Color(0.8f,0.7f,0.2f,1f),
            _ => new Color(1f,0f,1f,1f)
        };
    }

    public static Color GetBattleMapColor(Biome b)
    {
        // For now, use the same palette as minimap; can be adjusted for battle-specific contrast later.
        return GetMinimapColor(b);
    }
}
