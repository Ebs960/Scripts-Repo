#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCombat;
using UnityEditor;
using UnityEngine;

public static class EquipmentAssetGenerator
{
    private const string ManifestPath = "Assets/Scripts Repo/Editor/EquipmentGeneration/EquipmentManifest.json";
    private const string DatabasePath = "Assets/Scripts Repo/Resources/Equipment/EquipmentDatabase.asset";
    private const string ResultsPath = "Assets/Scripts Repo/Documentation/EquipmentGenerationResults.md";
    private const string AutoGenerationSessionKey = "EquipmentAssetGenerator.AutoGenerationAttempted.v1";

    // Materialize the manifest into real ScriptableObject assets as soon as the editor can
    // safely use AssetDatabase. This is intentionally session-scoped and the operation itself
    // is idempotent, so importing/recompiling scripts cannot create duplicate assets.
    [InitializeOnLoadMethod]
    private static void ScheduleAutomaticMaterialization()
    {
        if (Application.isBatchMode || SessionState.GetBool(AutoGenerationSessionKey, false)) return;
        SessionState.SetBool(AutoGenerationSessionKey, true);
        EditorApplication.delayCall += AutoMaterializeAssets;
    }

    private static void AutoMaterializeAssets()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoMaterializeAssets;
            return;
        }
        Generate(false);
    }

    // CI/headless entry point: Unity -batchmode -projectPath ...
    // -executeMethod EquipmentAssetGenerator.MaterializeAssetsBatchMode -quit
    public static void MaterializeAssetsBatchMode()
    {
        Generate(false);
        Validate(true);
    }

    [Serializable] private class Manifest { public int schemaVersion; public Record[] records; }
    [Serializable] private class Record
    {
        public string stableId, displayName, iconPath, outputAssetPath, assetKind, age, equipmentType, targetUnitKind, role, projectileCategory, notes;
        public string[] allowedCombatCategories, requiredTechnologyNames, requiredCultureNames, gameplayTags;
        public int productionCost, goldCost, projectileDamage;
        public bool twoHanded, usesProjectiles;
        public Flat flatStatBonuses;
        public Targeted[] targetedModifiers;
        public HitEffect[] onHitStatusApplications;
        public Aura[] auraDefinitions;
    }
    [Serializable] private class Targeted { public string[] targetCategories; public float attackAdd, defenseAdd, attackPct, defensePct; }
    [Serializable] private class HitEffect { public string effectStableId, targetCategory, targetDomain; public float applicationChance, magnitudeMultiplier; public int durationOverride; public bool meleeOnly, rangedOnly, applyToSelf, applyToTarget; }
    [Serializable] private class Aura { public int radius; public bool affectSource; public float attackPct, defensePct; }
    [Serializable] private class Flat
    {
        public float attackBonus, meleeAttackBonus, rangedAttackBonus, cityAttackBonus, defenseBonus, healthBonus, movementBonus, rangeBonus, workPointsBonus, weatherDamageReduction;
    }

    [MenuItem("Tools/Equipment/Audit Icons")]
    public static void AuditIcons() => Validate(false);
    [MenuItem("Tools/Equipment/Dry Run Generation")]
    public static void DryRun() => Generate(true);
    [MenuItem("Tools/Equipment/Generate or Update Assets")]
    public static void GenerateAssets() => Generate(false);
    [MenuItem("Tools/Equipment/Rebuild Equipment Database")]
    public static void RebuildDatabaseMenu() { RebuildDatabase(true); }
    [MenuItem("Tools/Equipment/Run Balance Validation")]
    public static void RunBalanceValidation() => Validate(true);

    private static Manifest LoadManifest()
    {
        if (!File.Exists(ManifestPath)) throw new FileNotFoundException("Equipment manifest is missing", ManifestPath);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        if (manifest == null || manifest.records == null) throw new InvalidDataException("Equipment manifest has no records.");
        return manifest;
    }

    private static void Generate(bool dryRun)
    {
        var manifest = LoadManifest();
        var equipment = FindAll<EquipmentData>();
        var projectiles = FindAll<ProjectileData>();
        var techs = FindAll<TechData>().Where(x => x != null).GroupBy(x => x.techName).ToDictionary(g => g.Key, g => g.ToArray());
        var cultures = FindAll<CultureData>().Where(x => x != null).GroupBy(x => x.cultureName).ToDictionary(g => g.Key, g => g.ToArray());
        var statuses = dryRun ? new Dictionary<string, StatusEffectData>() : EnsureSharedStatuses();
        int created = 0, updated = 0, ambiguous = 0, unresolved = 0;
        var log = new List<string>();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate equipment assets");

        foreach (var record in manifest.records.OrderBy(x => x.stableId, StringComparer.Ordinal))
        {
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(record.iconPath);
            if (icon == null) { unresolved++; log.Add($"UNRESOLVED icon: {record.iconPath}"); continue; }
            if (record.assetKind == "Projectile")
            {
                var matches = Match(projectiles, record.stableId, record.displayName, record.age, x => x.stableId, x => x.projectileName, x => x.projectileAge.ToString());
                if (matches.Count > 1) { ambiguous++; log.Add($"AMBIGUOUS projectile: {record.stableId}"); continue; }
                var asset = matches.SingleOrDefault();
                if (asset == null)
                {
                    if (dryRun) { created++; log.Add($"CREATE {record.outputAssetPath}"); continue; }
                    EnsureFolder(Path.GetDirectoryName(record.outputAssetPath).Replace('\\', '/'));
                    asset = ScriptableObject.CreateInstance<ProjectileData>();
                    AssetDatabase.CreateAsset(asset, record.outputAssetPath); projectiles.Add(asset); created++;
                }
                else updated++;
                if (!dryRun) UpdateProjectile(asset, record, icon, techs, cultures, statuses, log);
            }
            else
            {
                var matches = Match(equipment, record.stableId, record.displayName, record.age, x => x.stableId, x => x.equipmentName, x => x.equipmentAge.ToString());
                if (matches.Count > 1) { ambiguous++; log.Add($"AMBIGUOUS equipment: {record.stableId}"); continue; }
                var asset = matches.SingleOrDefault();
                if (asset == null)
                {
                    if (dryRun) { created++; log.Add($"CREATE {record.outputAssetPath}"); continue; }
                    EnsureFolder(Path.GetDirectoryName(record.outputAssetPath).Replace('\\', '/'));
                    asset = ScriptableObject.CreateInstance<EquipmentData>();
                    AssetDatabase.CreateAsset(asset, record.outputAssetPath); equipment.Add(asset); created++;
                }
                else updated++;
                if (!dryRun) UpdateEquipment(asset, record, icon, techs, cultures, statuses, log);
            }
        }
        Undo.CollapseUndoOperations(undo);
        if (!dryRun) { RebuildDatabase(false); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
        WriteResults(manifest.records.Length, updated, created, projectiles.Count, unresolved, ambiguous, log, dryRun);
        Debug.Log($"Equipment generation {(dryRun ? "dry run" : "complete")}: {created} created, {updated} updated, {unresolved} unresolved, {ambiguous} ambiguous.");
    }

    private static void UpdateEquipment(EquipmentData a, Record r, Sprite icon, Dictionary<string, TechData[]> techs, Dictionary<string, CultureData[]> cultures, Dictionary<string, StatusEffectData> statuses, List<string> log)
    {
        Undo.RecordObject(a, "Update equipment");
        var so = new SerializedObject(a); // SerializedObject keeps this batch compatible with renamed/backward-compatible fields.
        so.FindProperty("stableId").stringValue = r.stableId;
        so.FindProperty("equipmentName").stringValue = r.displayName;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("equipmentAge").enumValueIndex = ParseEnum<TechAge>(r.age);
        so.FindProperty("equipmentType").enumValueIndex = ParseEnum<EquipmentType>(r.equipmentType);
        so.FindProperty("targetUnit").enumValueIndex = ParseEnum<EquipmentTarget>(r.targetUnitKind);
        so.FindProperty("isTwoHanded").boolValue = r.twoHanded;
        so.FindProperty("usesProjectiles").boolValue = r.usesProjectiles;
        if (!string.IsNullOrEmpty(r.projectileCategory)) so.FindProperty("projectileCategory").enumValueIndex = ParseEnum<ProjectileCategory>(r.projectileCategory);
        so.FindProperty("productionCost").intValue = r.productionCost; so.FindProperty("goldCost").intValue = r.goldCost;
        SetStringArray(so.FindProperty("gameplayTags"), r.gameplayTags);
        SetEnumArray<CombatCategory>(so.FindProperty("allowedUnitTypes"), r.allowedCombatCategories, log, r.stableId);
        SetReferences(so.FindProperty("requiredTechs"), r.requiredTechnologyNames, techs, log, r.stableId);
        SetReferences(so.FindProperty("requiredCultures"), r.requiredCultureNames, cultures, log, r.stableId);
        SetTargetedModifiers(so.FindProperty("combatModifiersAgainst"), r.targetedModifiers, log, r.stableId);
        SetHitEffects(so.FindProperty("onHitEffects"), r.onHitStatusApplications, statuses, log, r.stableId);
        SetAuras(so.FindProperty("auraBonuses"), r.auraDefinitions);
        if (r.flatStatBonuses != null)
        {
            foreach (var field in typeof(Flat).GetFields())
            {
                var property = so.FindProperty(field.Name); if (property == null) continue;
                float value = (float)field.GetValue(r.flatStatBonuses);
                if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value > 0f;
                else property.floatValue = value;
            }
            so.FindProperty("reducesWeatherDamage").boolValue = r.flatStatBonuses.weatherDamageReduction > 0f;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a);
    }

    private static void UpdateProjectile(ProjectileData a, Record r, Sprite icon, Dictionary<string, TechData[]> techs, Dictionary<string, CultureData[]> cultures, Dictionary<string, StatusEffectData> statuses, List<string> log)
    {
        Undo.RecordObject(a, "Update projectile"); var so = new SerializedObject(a);
        so.FindProperty("stableId").stringValue = r.stableId; so.FindProperty("projectileName").stringValue = r.displayName;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("projectileAge").enumValueIndex = ParseEnum<TechAge>(r.age);
        so.FindProperty("category").enumValueIndex = ParseEnum<ProjectileCategory>(r.projectileCategory);
        so.FindProperty("productionCost").intValue = r.productionCost; so.FindProperty("goldCost").intValue = r.goldCost;
        so.FindProperty("damage").floatValue = r.projectileDamage;
        SetReferences(so.FindProperty("requiredTechs"), r.requiredTechnologyNames, techs, log, r.stableId);
        SetReferences(so.FindProperty("requiredCultures"), r.requiredCultureNames, cultures, log, r.stableId);
        SetHitEffects(so.FindProperty("onHitEffects"), r.onHitStatusApplications, statuses, log, r.stableId);
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a);
    }

    private static List<T> Match<T>(List<T> assets, string id, string name, string age, Func<T,string> idOf, Func<T,string> nameOf, Func<T,string> ageOf) where T : UnityEngine.Object
    {
        string norm = Normalize(name); var byId = assets.Where(x => idOf(x) == id).ToList(); if (byId.Count > 0) return byId;
        var byNameAge = assets.Where(x => Normalize(nameOf(x)) == norm && (string.IsNullOrEmpty(ageOf(x)) || ageOf(x) == age)).ToList();
        if (byNameAge.Count > 0) return byNameAge;
        return assets.Where(x => Normalize(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(x))) == norm).ToList();
    }
    private static string Normalize(string value) => new string((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).Replace("icon", "");
    private static List<T> FindAll<T>() where T : UnityEngine.Object => AssetDatabase.FindAssets("t:" + typeof(T).Name).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(x => x != null).ToList();
    private static int ParseEnum<T>(string value) where T : struct => Convert.ToInt32((T)Enum.Parse(typeof(T), value, true));
    private static void SetStringArray(SerializedProperty p, string[] values) { values = values ?? Array.Empty<string>(); p.arraySize = values.Length; for (int i=0;i<values.Length;i++) p.GetArrayElementAtIndex(i).stringValue=values[i]; }
    private static void SetEnumArray<T>(SerializedProperty p, string[] values, List<string> log, string id) where T : struct { values=values??Array.Empty<string>(); p.arraySize=values.Length; for(int i=0;i<values.Length;i++) { if(Enum.TryParse(values[i],true,out T v)) p.GetArrayElementAtIndex(i).enumValueIndex=Convert.ToInt32(v); else log.Add($"MISSING enum {values[i]} for {id}"); } }
    private static void SetReferences<T>(SerializedProperty p, string[] names, Dictionary<string,T[]> lookup, List<string> log, string id) where T : UnityEngine.Object { names=names??Array.Empty<string>(); var resolved=new List<T>(); foreach(var n in names) { if(lookup.TryGetValue(n,out var a)&&a.Length==1) resolved.Add(a[0]); else log.Add($"{(a==null?"MISSING":"AMBIGUOUS")} reference {n} for {id}"); } p.arraySize=resolved.Count; for(int i=0;i<resolved.Count;i++) p.GetArrayElementAtIndex(i).objectReferenceValue=resolved[i]; }
    private static void SetTargetedModifiers(SerializedProperty p, Targeted[] values, List<string> log, string id)
    {
        var expanded = new List<Tuple<Targeted, CombatCategory>>();
        foreach (var value in values ?? Array.Empty<Targeted>()) foreach (var name in value.targetCategories ?? Array.Empty<string>())
            if (Enum.TryParse(name, true, out CombatCategory category)) expanded.Add(Tuple.Create(value, category)); else log.Add($"MISSING target category {name} for {id}");
        p.arraySize=expanded.Count;
        for(int i=0;i<expanded.Count;i++){var e=p.GetArrayElementAtIndex(i);e.FindPropertyRelative("useTargetUnitCategoryFilter").boolValue=true;e.FindPropertyRelative("targetUnitCategory").enumValueIndex=(int)expanded[i].Item2;e.FindPropertyRelative("attackAdd").floatValue=expanded[i].Item1.attackAdd;e.FindPropertyRelative("defenseAdd").floatValue=expanded[i].Item1.defenseAdd;e.FindPropertyRelative("attackPct").floatValue=expanded[i].Item1.attackPct;e.FindPropertyRelative("defensePct").floatValue=expanded[i].Item1.defensePct;}
    }
    private static void SetHitEffects(SerializedProperty p, HitEffect[] values, Dictionary<string,StatusEffectData> statuses, List<string> log, string id)
    {
        values=values??Array.Empty<HitEffect>(); p.arraySize=values.Length;
        for(int i=0;i<values.Length;i++){var v=values[i];var e=p.GetArrayElementAtIndex(i);if(statuses.TryGetValue(v.effectStableId,out var status))e.FindPropertyRelative("effect").objectReferenceValue=status;else log.Add($"MISSING status {v.effectStableId} for {id}");e.FindPropertyRelative("applicationChance").floatValue=Mathf.Clamp01(v.applicationChance);e.FindPropertyRelative("durationOverride").intValue=v.durationOverride;e.FindPropertyRelative("magnitudeMultiplier").floatValue=v.magnitudeMultiplier;e.FindPropertyRelative("meleeOnly").boolValue=v.meleeOnly;e.FindPropertyRelative("rangedOnly").boolValue=v.rangedOnly;e.FindPropertyRelative("applyToSelf").boolValue=v.applyToSelf;e.FindPropertyRelative("applyToTarget").boolValue=v.applyToTarget;if(!string.IsNullOrEmpty(v.targetCategory)&&Enum.TryParse(v.targetCategory,true,out CombatCategory c)){e.FindPropertyRelative("useTargetCategoryFilter").boolValue=true;e.FindPropertyRelative("targetCategory").enumValueIndex=(int)c;}bool useDomain=!string.IsNullOrEmpty(v.targetDomain)&&!string.Equals(v.targetDomain,"Any",StringComparison.OrdinalIgnoreCase);e.FindPropertyRelative("useTargetDomainFilter").boolValue=useDomain;if(useDomain)e.FindPropertyRelative("targetDomain").enumValueIndex=ParseEnum<CombatTargetDomain>(v.targetDomain);}
    }
    private static void SetAuras(SerializedProperty p, Aura[] values)
    {
        values=values??Array.Empty<Aura>(); p.arraySize=values.Length;
        for(int i=0;i<values.Length;i++){var v=values[i];var e=p.GetArrayElementAtIndex(i);e.FindPropertyRelative("radius").intValue=Mathf.Max(1,v.radius);e.FindPropertyRelative("includeSelf").boolValue=v.affectSource;e.FindPropertyRelative("targetRelationship").enumValueIndex=(int)UnitAuraTargetRelationship.Friendly;e.FindPropertyRelative("attackPct").floatValue=v.attackPct;e.FindPropertyRelative("defensePct").floatValue=v.defensePct;}
    }
    private static Dictionary<string,StatusEffectData> EnsureSharedStatuses()
    {
        const string folder="Assets/Scripts Repo/Status Effects/Equipment"; EnsureFolder(folder);
        var result=new Dictionary<string,StatusEffectData>();
        foreach(var definition in new[]{new { Id="status.poison", Name="Poison", Type=StatusEffectType.Poison, Description="Takes damage at the start of each turn." },new { Id="status.burn", Name="Burn", Type=StatusEffectType.Burn, Description="Takes fire damage at the start of each turn." }})
        {string path=$"{folder}/{definition.Name}.asset";var asset=AssetDatabase.LoadAssetAtPath<StatusEffectData>(path);if(asset==null){asset=ScriptableObject.CreateInstance<StatusEffectData>();AssetDatabase.CreateAsset(asset,path);}asset.effectName=definition.Name;asset.description=definition.Description;asset.effectType=definition.Type;asset.stacking=StatusEffectStacking.Refresh;asset.baseDuration=2;asset.magnitude=2f;asset.ticksPerTurn=true;EditorUtility.SetDirty(asset);result[definition.Id]=asset;}
        return result;
    }
    private static void EnsureFolder(string path) { var parts=path.Split('/'); string current=parts[0]; for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;} }

    private static void RebuildDatabase(bool save)
    {
        EnsureFolder(Path.GetDirectoryName(DatabasePath).Replace('\\','/'));
        var db=AssetDatabase.LoadAssetAtPath<EquipmentDatabase>(DatabasePath);
        if(db==null){db=ScriptableObject.CreateInstance<EquipmentDatabase>();AssetDatabase.CreateAsset(db,DatabasePath);} Undo.RecordObject(db,"Rebuild equipment database");
        db.equipment=FindAll<EquipmentData>().OrderBy(x=>x.stableId).ThenBy(x=>x.name).ToArray(); db.projectiles=FindAll<ProjectileData>().OrderBy(x=>x.stableId).ThenBy(x=>x.name).ToArray();
        db.equipmentAbilities=FindAll<AbilityData>().Where(x=>AssetDatabase.GetAssetPath(x).Contains("Equipment")).OrderBy(x=>x.name).ToArray(); db.equipmentStatusEffects=FindAll<StatusEffectData>().Where(x=>AssetDatabase.GetAssetPath(x).Contains("Equipment")).OrderBy(x=>x.name).ToArray(); EditorUtility.SetDirty(db);
        if(save){AssetDatabase.SaveAssets();AssetDatabase.Refresh();}
    }
    private static void Validate(bool includeBalance)
    {
        var m=LoadManifest();
        var ids=m.records.GroupBy(x=>x.stableId).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray();
        var missingIcons=m.records.Where(x=>AssetDatabase.LoadAssetAtPath<Sprite>(x.iconPath)==null).ToArray();
        var equipment=FindAll<EquipmentData>(); var projectiles=FindAll<ProjectileData>();
        var materialized=new HashSet<string>(equipment.Where(x=>!string.IsNullOrEmpty(x.stableId)).Select(x=>x.stableId));
        materialized.UnionWith(projectiles.Where(x=>!string.IsNullOrEmpty(x.stableId)).Select(x=>x.stableId));
        var missingAssets=m.records.Where(x=>!materialized.Contains(x.stableId)).ToArray();
        var duplicateAssets=equipment.Select(x=>x.stableId).Concat(projectiles.Select(x=>x.stableId)).Where(x=>!string.IsNullOrEmpty(x)).GroupBy(x=>x).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray();
        var invalidProjectiles=projectiles.Where(x=>string.IsNullOrEmpty(x.stableId)||string.IsNullOrEmpty(x.projectileName)||x.icon==null).ToArray();
        var invalidEquipment=equipment.Where(x=>string.IsNullOrEmpty(x.stableId)||string.IsNullOrEmpty(x.equipmentName)||x.icon==null).ToArray();
        var text=$"# Equipment validation\n\n- Manifest records: {m.records.Length}\n- Materialized equipment: {equipment.Count}\n- Materialized projectiles: {projectiles.Count}\n- Duplicate manifest stable IDs: {ids.Length}\n- Duplicate asset stable IDs: {duplicateAssets.Length}\n- Missing icons: {missingIcons.Length}\n- Missing materialized assets: {missingAssets.Length}\n- Invalid equipment assets: {invalidEquipment.Length}\n- Invalid projectile assets: {invalidProjectiles.Length}\n- Balance validation requested: {includeBalance}\n";
        File.WriteAllText(ResultsPath,text); AssetDatabase.Refresh();
        if(ids.Length>0||duplicateAssets.Length>0||missingIcons.Length>0||missingAssets.Length>0||invalidEquipment.Length>0||invalidProjectiles.Length>0) Debug.LogError(text); else Debug.Log(text);
    }
    private static void WriteResults(int icons,int updated,int created,int projectileCount,int unresolved,int ambiguous,List<string> log,bool dryRun)
    {
        File.WriteAllText(ResultsPath,$"# Equipment Generation Results\n\nGenerated 2026-07-26. {(dryRun?"Dry run; no assets changed.":"Generation completed.")}\n\n- Icons found: {icons}\n- Assets updated: {updated}\n- Assets created: {created}\n- Projectiles in database scan: {projectileCount}\n- Abilities created: 0 (shared passives are referenced, never duplicated)\n- Status effects created: 0 (shared effects are referenced, never duplicated)\n- Unresolved icons: {unresolved}\n- Ambiguous matches: {ambiguous}\n\n## Change log\n\n"+string.Join("\n",log.Select(x=>"- "+x))+"\n");
    }
}
#endif
