using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global registry for unit GameObjects to avoid expensive scene-wide searches.
/// </summary>
public static class UnitRegistry
{
    private static readonly Dictionary<int, GameObject> idLookup = new();
    private static readonly HashSet<CombatUnit> combatUnits = new();
    private static readonly HashSet<WorkerUnit> workerUnits = new();
    // Persistent id lookup
    private static readonly Dictionary<string, GameObject> persistentIdLookup = new();

    public static void Register(GameObject obj)
    {
        if (obj == null) return;
        idLookup[obj.GetInstanceID()] = obj;
        if (obj.TryGetComponent<CombatUnit>(out var cu))
            combatUnits.Add(cu);
        if (obj.TryGetComponent<WorkerUnit>(out var wu))
            workerUnits.Add(wu);
    }

    public static void Unregister(GameObject obj)
    {
        if (obj == null) return;
        idLookup.Remove(obj.GetInstanceID());
        if (obj.TryGetComponent<CombatUnit>(out var cu))
            combatUnits.Remove(cu);
        if (obj.TryGetComponent<WorkerUnit>(out var wu))
            workerUnits.Remove(wu);
        // Remove from persistent lookup if present
        if (obj.TryGetComponent<WorkerUnit>(out var w2))
        {
            if (!string.IsNullOrEmpty(w2.PersistentId))
            {
                persistentIdLookup.Remove(w2.PersistentId);
            }
        }
    }

    public static GameObject GetObject(int instanceId)
    {
        idLookup.TryGetValue(instanceId, out var obj);
        return obj;
    }

    public static IEnumerable<CombatUnit> GetCombatUnits() => combatUnits;
    public static IEnumerable<WorkerUnit> GetWorkerUnits() => workerUnits;

    public static void RegisterPersistent(string persistentId, GameObject obj)
    {
        if (string.IsNullOrEmpty(persistentId) || obj == null) return;
        persistentIdLookup[persistentId] = obj;
    }

    public static GameObject GetByPersistentId(string persistentId)
    {
        if (string.IsNullOrEmpty(persistentId)) return null;
        persistentIdLookup.TryGetValue(persistentId, out var obj);
        return obj;
    }
}
