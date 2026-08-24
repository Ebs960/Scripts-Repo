using System.Linq;
using GameCombat;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Build-included manifest for base-game ScriptableObject content. Runtime code must consume
/// this manifest rather than attempting to enumerate the AssetDatabase or arbitrary folders.
/// </summary>
[CreateAssetMenu(fileName = "BaseGameContentDatabase", menuName = "Data/Base Game Content Database", order = 190)]
public sealed class BaseGameContentDatabase : ScriptableObject
{
    public CombatUnitData[] combatUnits;
    public WorkerUnitData[] workerUnits;
    public BuildingData[] buildings;
    public DistrictData[] districts;
    public ImprovementData[] improvements;
    public ResourceData[] resources;
    public MissileData[] missiles;
    public CivData[] civilizations;
    public LeaderData[] leaders;
    public GovernmentData[] governments;
    public PolicyData[] policies;

    [Header("Canonical specialist databases")]
    public ResearchDatabase research;
    public ReligionDatabase religion;
    public EquipmentDatabase equipment;

#if UNITY_EDITOR
    [ContextMenu("Populate From Project")]
    public void PopulateFromProject()
    {
        combatUnits = FindAll<CombatUnitData>();
        workerUnits = FindAll<WorkerUnitData>();
        buildings = FindAll<BuildingData>();
        districts = FindAll<DistrictData>();
        improvements = FindAll<ImprovementData>();
        resources = FindAll<ResourceData>();
        missiles = FindAll<MissileData>();
        civilizations = FindAll<CivData>();
        leaders = FindAll<LeaderData>();
        governments = FindAll<GovernmentData>();
        policies = FindAll<PolicyData>();
        research = FindFirst<ResearchDatabase>();
        religion = FindFirst<ReligionDatabase>();
        equipment = FindFirst<EquipmentDatabase>();
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    private static T[] FindAll<T>() where T : ScriptableObject => AssetDatabase
        .FindAssets($"t:{typeof(T).Name}")
        .Select(AssetDatabase.GUIDToAssetPath)
        .Select(AssetDatabase.LoadAssetAtPath<T>)
        .Where(value => value != null)
        .OrderBy(value => value.name)
        .ToArray();

    private static T FindFirst<T>() where T : ScriptableObject => FindAll<T>().FirstOrDefault();
#endif
}
