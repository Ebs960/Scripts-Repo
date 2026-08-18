using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName="Battle/Biome Visual Database",fileName="Battle Biome Visual Database")]
public sealed class BattleBiomeVisualDatabase : ScriptableObject
{
    public List<BattleBiomeVisualProfile> profiles=new();
    private Dictionary<Biome,BattleBiomeVisualProfile> lookup;
    private bool lookupDirty=true;
    public BattleBiomeVisualProfile Get(Biome biome){EnsureLookup();return lookup.TryGetValue(biome,out var profile)?profile:null;}
    private void EnsureLookup(){if(!lookupDirty&&lookup!=null)return;lookup=new Dictionary<Biome,BattleBiomeVisualProfile>();if(profiles!=null)foreach(var profile in profiles)if(profile!=null)lookup[profile.biome]=profile;lookupDirty=false;}
    private void OnEnable(){lookup=null;lookupDirty=true;}
    private void OnValidate()
    {
        lookup=null;lookupDirty=true;var seen=new HashSet<Biome>();if(profiles==null)return;
        foreach(var profile in profiles){if(profile==null)continue;if(!seen.Add(profile.biome))Debug.LogWarning($"[{name}] Duplicate tactical profile for {profile.biome}.",this);}
    }
}
