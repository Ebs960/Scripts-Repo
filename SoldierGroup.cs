using System.Collections.Generic;
using UnityEngine;

public enum FormationType
{
    Square,     // Grid rows and columns (default, soldiers on tile)
    Wedge       // V-shape, leader at front
}

[System.Serializable]
public struct SoldierVariant
{
    [Tooltip("Model prefab for this soldier variant")]
    public GameObject modelPrefab;
    [Tooltip("Relative spawn weight (higher = more common). Default 1.")]
    [Range(0.01f, 10f)]
    public float weight;
}

/// <summary>
/// Manages the multi-soldier visual representation of a single gameplay unit.
/// Spawns N soldier figures picked randomly from variant prefabs, arranges them
/// in a configurable formation, forwards animations and equipment, and removes
/// figures as the unit loses health (Civ-style visual attrition).
/// 
/// Attach to the root unit prefab. Populated at runtime by BaseUnit after Initialize.
/// </summary>
public class SoldierGroup : MonoBehaviour
{
    // --- Runtime state ---
    private List<SoldierInstance> soldiers = new List<SoldierInstance>();
    private int targetSoldierCount;
    private FormationType formation;
    private float spacing;
    private float modelRadius; // measured from lead model's renderer bounds
    private int visibleCount; // how many soldiers are currently shown (HP-based)
    private System.Random variantRng;

    /// <summary>Per-soldier bookkeeping.</summary>
    private class SoldierInstance
    {
        public GameObject root;       // instantiated variant GO
        public Animator animator;
        public bool isAlive = true;   // false after visual attrition "kills" it

        // Equipment holders found on this variant (matched by name)
        public Transform weaponHolder;
        public Transform projectileWeaponHolder;
        public Transform shieldHolder;
        public Transform armorHolder;
        public Transform miscHolder;

        // Equipment visual instances (so we can clean them up)
        public Dictionary<string, GameObject> equipmentObjects = new Dictionary<string, GameObject>();
    }

    // Cached param flags (same as BaseUnit hashes)
    private static readonly int isWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int attackHash = Animator.StringToHash("Attack");
    private static readonly int hitHash = Animator.StringToHash("Hit");
    private static readonly int deathHash = Animator.StringToHash("Death");
    private static readonly int isFortifiedHash = Animator.StringToHash("IsFortified");

    /// <summary>Number of currently alive (visible) soldiers.</summary>
    public int VisibleCount => visibleCount;

    /// <summary>Total soldiers spawned (including dead ones).</summary>
    public int TotalCount => soldiers.Count;

    // -----------------------------------------------------------------
    // Initialization
    // -----------------------------------------------------------------

    /// <summary>
    /// Spawn soldier figures and arrange them in formation.
    /// Call once after the unit GameObject is placed in the world.
    /// </summary>
    /// <param name="count">Total soldiers to display (including the lead model on the root prefab).</param>
    /// <param name="variants">Model variant pool. If null/empty, additional soldiers duplicate the root model's visual child.</param>
    /// <param name="formationType">Formation layout.</param>
    /// <param name="formationSpacing">Distance between soldiers (world units).</param>
    /// <param name="seed">Random seed for reproducible variant picks.</param>
    public void Initialize(int count, SoldierVariant[] variants, FormationType formationType, float formationSpacing, int seed = 0)
    {
        targetSoldierCount = Mathf.Max(1, count);
        formation = formationType;
        variantRng = new System.Random(seed != 0 ? seed : gameObject.GetRuntimeId());

        // Measure the lead model once for downstream layout/visual logic.
        modelRadius = MeasureModelRadius();
        // formationSpacing is authored in world units on the unit data assets.
        spacing = Mathf.Max(0.15f, formationSpacing);

        // Additional soldiers beyond the lead (the root prefab model is soldier #1)
        int extras = targetSoldierCount - 1;
        if (extras <= 0)
        {
            visibleCount = 1;
            return;
        }

        GameObject fallbackSource = null;
        float totalWeight = 0f;
        bool useVariants = variants != null && variants.Length > 0;

        if (useVariants)
        {
            foreach (var v in variants)
            {
                if (v.modelPrefab != null)
                    totalWeight += Mathf.Max(0.01f, v.weight);
            }

            useVariants = totalWeight > 0f;
        }

        if (!useVariants)
        {
            fallbackSource = FindFallbackVisualSource();
            if (fallbackSource == null)
            {
                visibleCount = 1;
                return;
            }
        }

        // When the fallback source is the root gameObject itself, clone it once
        // up-front as a clean template so that subsequent iterations don't pick up
        // previously spawned extras (which causes cascading duplication).
        GameObject fallbackTemplate = null;
        if (!useVariants && fallbackSource == gameObject)
        {
            fallbackTemplate = Instantiate(gameObject);
            fallbackTemplate.SetActive(false);
            SanitizeFallbackClone(fallbackTemplate);
        }

        for (int i = 0; i < extras; i++)
        {
            GameObject instance;
            if (useVariants)
            {
                instance = CreateVariantInstance(PickVariant(variants, totalWeight));
            }
            else if (fallbackTemplate != null)
            {
                instance = Instantiate(fallbackTemplate, transform);
                instance.SetActive(true);
            }
            else
            {
                instance = CreateFallbackInstance(fallbackSource);
            }
            if (instance == null) continue;

            instance.name = $"Soldier_{i + 2}";

            var si = new SoldierInstance
            {
                root = instance,
                animator = instance.GetComponentInChildren<Animator>(),
                weaponHolder = FindHolderByName(instance.transform, "WeaponHolder", "Weapon Holder", "weaponHolder"),
                projectileWeaponHolder = FindHolderByName(instance.transform, "ProjectileWeaponHolder", "Projectile Weapon Holder", "projectileWeaponHolder"),
                shieldHolder = FindHolderByName(instance.transform, "ShieldHolder", "Shield Holder", "shieldHolder"),
                armorHolder = FindHolderByName(instance.transform, "ArmorHolder", "Armor Holder", "armorHolder"),
                miscHolder = FindHolderByName(instance.transform, "MiscHolder", "Misc Holder", "miscHolder")
            };

            // Disable root motion on additional soldier animators
            if (si.animator != null)
                si.animator.applyRootMotion = false;

            soldiers.Add(si);
        }

        // Clean up the one-time fallback template if we created one
        if (fallbackTemplate != null)
            Destroy(fallbackTemplate);

        visibleCount = targetSoldierCount;
        ArrangeFormation();
    }

    private GameObject FindFallbackVisualSource()
    {
        Animator leadAnimator = GetComponentInChildren<Animator>();
        if (leadAnimator != null)
            return leadAnimator.gameObject;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;
            if (child.GetComponentInChildren<Renderer>() != null)
                return child.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Measure the approximate XZ radius of the lead model by combining all renderer bounds.
    /// Returns a sensible default if no renderers are found.
    /// </summary>
    private float MeasureModelRadius()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return 0.5f;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        // Use the larger of X or Z extents as the radius
        float r = Mathf.Max(combined.extents.x, combined.extents.z);
        return Mathf.Max(0.1f, r);
    }

    private GameObject CreateVariantInstance(GameObject prefab)
    {
        if (prefab == null) return null;
        return Instantiate(prefab, transform);
    }

    private GameObject CreateFallbackInstance(GameObject fallbackSource)
    {
        if (fallbackSource == null) return null;

        // If the lead animator lives on the unit root, clone the full visual rig and strip gameplay components.
        if (fallbackSource == gameObject)
        {
            GameObject clone = Instantiate(gameObject);
            clone.transform.SetParent(transform, true);
            SanitizeFallbackClone(clone);
            return clone;
        }

        return Instantiate(fallbackSource, transform);
    }

    private void SanitizeFallbackClone(GameObject clone)
    {
        if (clone == null) return;

        foreach (var unit in clone.GetComponentsInChildren<BaseUnit>(true))
        {
            if (unit != null)
                Destroy(unit);
        }

        foreach (var group in clone.GetComponentsInChildren<SoldierGroup>(true))
        {
            if (group != null)
                Destroy(group);
        }

        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
                Destroy(collider);
        }

        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body != null)
                Destroy(body);
        }

        foreach (var label in clone.GetComponentsInChildren<UnitLabel>(true))
        {
            if (label != null)
                Destroy(label.gameObject);
        }
    }

    // -----------------------------------------------------------------
    // Formation layout
    // -----------------------------------------------------------------

    /// <summary>
    /// Recompute local positions for all additional soldiers based on formation type.
    /// The lead soldier (main prefab model) stays at local origin; extras are offset.
    /// </summary>
    public void ArrangeFormation()
    {
        if (soldiers.Count == 0) return;

        Vector3[] offsets = ComputeFormationOffsets(soldiers.Count, formation, spacing);
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i].root == null) continue;
            soldiers[i].root.transform.localPosition = WorldOffsetToLocal(offsets[i]);
        }
    }

    private Vector3 WorldOffsetToLocal(Vector3 worldOffset)
    {
        Vector3 parentScale = transform.lossyScale;

        float x = Mathf.Approximately(parentScale.x, 0f) ? worldOffset.x : worldOffset.x / parentScale.x;
        float y = Mathf.Approximately(parentScale.y, 0f) ? worldOffset.y : worldOffset.y / parentScale.y;
        float z = Mathf.Approximately(parentScale.z, 0f) ? worldOffset.z : worldOffset.z / parentScale.z;

        return new Vector3(x, y, z);
    }

    private Vector3[] ComputeFormationOffsets(int count, FormationType type, float sp)
    {
        var offsets = new Vector3[count];

        switch (type)
        {
            case FormationType.Square:
            default:
            {
                // Grid layout: compute rows/cols to be as square as possible,
                // centered on the leader at origin.
                int total = count + 1; // +1 for the leader at center
                int cols = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(total)));
                int rows = Mathf.CeilToInt((float)total / cols);
                int slot = 0;
                for (int r = 0; r < rows && slot < count; r++)
                {
                    int colsThisRow = Mathf.Min(cols, total - r * cols);
                    for (int c = 0; c < colsThisRow && slot < count; c++)
                    {
                        // Skip the center slot (leader position)
                        int globalIndex = r * cols + c;
                        float x = (c - (colsThisRow - 1) * 0.5f) * sp;
                        float z = -(r - (rows - 1) * 0.5f) * sp;
                        // Skip the approximate center position (that's the leader)
                        if (r == rows / 2 && c == colsThisRow / 2)
                        {
                            // nudge this soldier to avoid overlapping the leader
                            x += sp * 0.5f;
                        }
                        offsets[slot] = new Vector3(x, 0f, z);
                        slot++;
                    }
                }
                break;
            }

            case FormationType.Wedge:
                for (int i = 0; i < count; i++)
                {
                    int side = (i % 2 == 0) ? 1 : -1;
                    int row = (i / 2) + 1;
                    offsets[i] = new Vector3(side * row * sp * 0.6f, 0f, -row * sp * 0.8f);
                }
                break;
        }

        return offsets;
    }

    // -----------------------------------------------------------------
    // Animation forwarding
    // -----------------------------------------------------------------

    /// <summary>Forward a trigger to all additional soldier animators (with slight random delay for variety).</summary>
    public void ForwardTrigger(int hash)
    {
        for (int i = 0; i < soldiers.Count; i++)
        {
            var si = soldiers[i];
            if (!si.isAlive || si.animator == null) continue;
            if (!HasParameter(si.animator, hash)) continue;
            // Small stagger so they don't fire in lockstep
            float delay = 0.02f + (float)variantRng.NextDouble() * 0.12f;
            StartCoroutine(DelayedTrigger(si.animator, hash, delay));
        }
    }

    /// <summary>Forward a bool to all additional soldier animators.</summary>
    public void ForwardBool(int hash, bool value)
    {
        foreach (var si in soldiers)
        {
            if (!si.isAlive || si.animator == null) continue;
            if (HasParameter(si.animator, hash))
                si.animator.SetBool(hash, value);
        }
    }

    /// <summary>
    /// Mirror all bool parameters from the lead animator onto the additional soldier animators.
    /// This keeps secondary models in the same controller state when units use extra bools such as IsIdle or IdleYoung.
    /// </summary>
    public void SyncBoolParametersFrom(Animator sourceAnimator)
    {
        if (sourceAnimator == null) return;

        var sourceParameters = sourceAnimator.parameters;
        if (sourceParameters == null || sourceParameters.Length == 0) return;

        foreach (var si in soldiers)
        {
            if (!si.isAlive || si.animator == null) continue;

            foreach (var parameter in sourceParameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Bool) continue;
                if (!HasParameter(si.animator, parameter.nameHash)) continue;
                si.animator.SetBool(parameter.nameHash, sourceAnimator.GetBool(parameter.nameHash));
            }
        }
    }

    private System.Collections.IEnumerator DelayedTrigger(Animator anim, int hash, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (anim != null) anim.SetTrigger(hash);
    }

    private static bool HasParameter(Animator anim, int hash)
    {
        if (anim == null) return false;
        foreach (var parameter in anim.parameters)
        {
            if (parameter.nameHash == hash)
                return true;
        }
        return false;
    }

    // -----------------------------------------------------------------
    // Equipment distribution
    // -----------------------------------------------------------------

    /// <summary>
    /// Equip all additional soldiers with the same equipment as the lead unit.
    /// Call this after the lead unit's UpdateEquipmentVisuals().
    /// </summary>
    public void DistributeEquipment(
        EquipmentData weapon,
        EquipmentData projectileWeapon,
        EquipmentData shield,
        EquipmentData armor,
        EquipmentData misc)
    {
        foreach (var si in soldiers)
        {
            if (!si.isAlive || si.root == null) continue;
            ClearSoldierEquipment(si);
            AttachEquipment(si, "weapon", weapon, si.weaponHolder);
            AttachEquipment(si, "projectile", projectileWeapon, si.projectileWeaponHolder);
            AttachEquipment(si, "shield", shield, si.shieldHolder);
            AttachEquipment(si, "armor", armor, si.armorHolder);
            AttachEquipment(si, "misc", misc, si.miscHolder);
        }
    }

    private void AttachEquipment(SoldierInstance si, string slotKey, EquipmentData data, Transform holder)
    {
        if (data == null || data.equipmentPrefab == null || holder == null) return;

        GameObject equipObj = Application.isPlaying
            ? EquipmentVisualPool.Acquire(data.equipmentPrefab)
            : Instantiate(data.equipmentPrefab);

        Quaternion authoredLocal = equipObj.transform.localRotation;
        equipObj.transform.SetParent(holder, false);
        equipObj.transform.localPosition = Vector3.zero;
        equipObj.transform.localRotation = authoredLocal;

        var renderers = equipObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null && !r.enabled) r.enabled = true;
        }

        si.equipmentObjects[slotKey] = equipObj;
    }

    private void ClearSoldierEquipment(SoldierInstance si)
    {
        foreach (var kv in si.equipmentObjects)
        {
            if (kv.Value == null) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(kv.Value); continue; }
#endif
            EquipmentVisualPool.Release(kv.Value);
        }
        si.equipmentObjects.Clear();
    }

    // -----------------------------------------------------------------
    // HP-based visual attrition
    // -----------------------------------------------------------------

    /// <summary>
    /// Update which soldiers are visible based on the unit's current health ratio.
    /// Call after any health change. Soldiers "die" back-to-front when HP drops.
    /// </summary>
    public void UpdateAttrition(int currentHealth, int maxHealth)
    {
        if (soldiers.Count == 0) return;

        int totalFigures = soldiers.Count + 1; // +1 for lead
        float ratio = maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f;

        // Map ratio to a figure count (at least 1 while alive, 0 only at death)
        int desired = currentHealth > 0
            ? Mathf.Max(1, Mathf.CeilToInt(ratio * totalFigures))
            : 0;

        // Additional soldiers to keep visible (lead is always index 0, extras start at index 1)
        int extrasDesired = Mathf.Clamp(desired - 1, 0, soldiers.Count); // -1 because lead is separate

        // Kill extras from back to front if we need fewer
        for (int i = soldiers.Count - 1; i >= 0; i--)
        {
            var si = soldiers[i];
            if (si.root == null) continue;

            if (i >= extrasDesired && si.isAlive)
            {
                // This soldier "dies"
                si.isAlive = false;
                if (si.animator != null)
                    si.animator.SetTrigger(deathHash);
                // Hide after death animation plays
                StartCoroutine(HideAfterDelay(si.root, 2f));
            }
            else if (i < extrasDesired && !si.isAlive)
            {
                // Revive (e.g., healing) — re-enable
                si.isAlive = true;
                si.root.SetActive(true);
            }
        }

        visibleCount = desired;
    }

    private System.Collections.IEnumerator HideAfterDelay(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null) go.SetActive(false);
    }

    // -----------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------

    /// <summary>Destroy all spawned soldier instances.</summary>
    public void Cleanup()
    {
        foreach (var si in soldiers)
        {
            ClearSoldierEquipment(si);
            if (si.root != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(si.root);
                else
#endif
                Destroy(si.root);
            }
        }
        soldiers.Clear();
        visibleCount = 0;
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private GameObject PickVariant(SoldierVariant[] variants, float totalWeight)
    {
        float roll = (float)variantRng.NextDouble() * totalWeight;
        float cumulative = 0f;
        foreach (var v in variants)
        {
            if (v.modelPrefab == null) continue;
            cumulative += Mathf.Max(0.01f, v.weight);
            if (roll <= cumulative)
                return v.modelPrefab;
        }
        // Fallback: return last valid
        for (int i = variants.Length - 1; i >= 0; i--)
        {
            if (variants[i].modelPrefab != null) return variants[i].modelPrefab;
        }
        return null;
    }

    /// <summary>Find a child transform by attempting several common naming conventions.</summary>
    private static Transform FindHolderByName(Transform root, params string[] names)
    {
        foreach (string name in names)
        {
            var found = FindChildRecursive(root, name);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var result = FindChildRecursive(parent.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }
}
