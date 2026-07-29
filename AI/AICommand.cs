using UnityEngine;

/// <summary>
/// Abstract base for all AI commands. Commands are generated during planning and executed sequentially.
/// Each command targets a specific unit and carries a pre-computed utility score.
/// </summary>
public abstract class AICommand
{
    public BaseUnit unit;
    public int planetIndex;
    public float score;

    public abstract bool CanExecute();
    public abstract void Execute();
}

// ─────────────────────────── Movement ───────────────────────────

public class AIMoveCommand : AICommand
{
    public int targetTileIndex;

    public override bool CanExecute()
    {
        if (unit == null || unit.isStored)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[AIMoveCommand] Cannot execute: unit null or stored (unit={(unit!=null?unit.name:"null")}) target={targetTileIndex}");
            return false;
        }
        if (targetTileIndex < 0 || targetTileIndex == unit.currentTileIndex)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[AIMoveCommand] Cannot execute: invalid targetTileIndex={targetTileIndex} current={unit.currentTileIndex} unit={unit.name}");
            return false;
        }
        bool can = unit.CanReachTile(targetTileIndex);
        if (!can && (Application.isEditor || Debug.isDebugBuild)) Debug.LogWarning($"[AIMoveCommand] CanReachTile returned false for unit={unit.name} target={targetTileIndex} tileOwner={(unit.owner!=null?unit.owner.civData?.civName:"null")}");
        return can;
    }

    public override void Execute()
    {
        unit.MoveTo(targetTileIndex);
    }
}

// ─────────────────────────── Attack (Combat) ───────────────────────────

public class AIAttackCommand : AICommand
{
    public BaseUnit target;

    public override bool CanExecute()
    {
        if (unit == null || target == null || unit.isStored)
        {
            if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[AIAttackCommand] Cannot execute: unit/target null or unit stored (unit={(unit!=null?unit.name:"null")}, target={(target!=null?target.name:"null")})");
            return false;
        }
        if (unit is CombatUnit cu)
        {
            if (cu.hasActedThisTurn)
            {
                if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[AIAttackCommand] Cannot execute: attacker hasActedThisTurn unit={cu.name}");
                return false;
            }
            if (target is CombatUnit ct)
            {
                bool ok = cu.CanAttack(ct);
                if (!ok && (Application.isEditor || Debug.isDebugBuild)) Debug.LogWarning($"[AIAttackCommand] CanAttack returned false for attacker={cu.name} targetCombat={ct.name}");
                return ok;
            }
            if (target is WorkerUnit wt)
            {
                bool ok = cu.CanAttack(wt);
                if (!ok && (Application.isEditor || Debug.isDebugBuild)) Debug.LogWarning($"[AIAttackCommand] CanAttack returned false for attacker={cu.name} targetWorker={wt.name}");
                return ok;
            }
        }
        if (unit is WorkerUnit wu)
        {
            bool ok = wu.CanAttack(target);
            if (!ok && (Application.isEditor || Debug.isDebugBuild)) Debug.LogWarning($"[AIAttackCommand] Worker CanAttack returned false for attacker={wu.name} target={target.name}");
            return ok;
        }
        if (Application.isEditor || Debug.isDebugBuild) Debug.LogWarning($"[AIAttackCommand] Cannot execute: unsupported unit type {unit.GetType().Name} for attack");
        return false;
    }

    public override void Execute()
    {
        if (unit is CombatUnit cu)
        {
            if (target is CombatUnit ct)
            {
                var mode = EngagementModeResolver.ResolveEngagementMode(cu, ct);
                if (mode == EngagementMode.TacticalLandBattle)
                {
                    var manager = BattleManager.GetOrCreate();
                    var preview = manager.RequestEngagement(cu, ct);
                    if (preview != null && preview.IsValid)
                    {
                        bool attackerPlayer = cu.owner != null && cu.owner.isPlayerControlled;
                        bool defenderPlayer = ct.owner != null && ct.owner.isPlayerControlled;
                        if (attackerPlayer || defenderPlayer)
                            manager.BeginManualBattle(preview);
                        else
                            manager.AutoResolve(preview);
                        return;
                    }
                }

                cu.Attack(ct);
            }
            else if (target is WorkerUnit wt) cu.Attack(wt);
        }
        else if (unit is WorkerUnit wu)
        {
            wu.Attack(target);
        }
    }
}

// ─────────────────────────── Capture (non-lethal convert to herd) ───────────────────────────

public class AICaptureCommand : AICommand
{
    public BaseUnit target;

    public override bool CanExecute()
    {
        if (unit == null || target == null || unit.isStored) return false;
        if (unit is CombatUnit cu)
        {
            if (cu.hasActedThisTurn) return false;
            if (target.planetIndex != cu.planetIndex) return false;
            var ts = TileSystem.GetForPlanet(cu.planetIndex) ?? TileSystem.Instance;
            if (ts == null) return false;
            int d = ts.GetTileDistance(cu.currentTileIndex, target.currentTileIndex);
            if (d != 1) return false; // must be adjacent

            if (target is CombatUnit ct && ct.data != null && ct.data.captureable) return true;
            if (target is WorkerUnit wt && wt.data != null && wt.data.captureable) return true;
        }
        return false;
    }

    public override void Execute()
    {
        if (unit == null || target == null) return;
        var cu = unit as CombatUnit;
        var attackerCiv = unit.owner;
        if (attackerCiv == null) return;

        CombatUnitData targetCombatData = target is CombatUnit ? (target as CombatUnit).data : null;
        WorkerUnitData targetWorkerData = target is WorkerUnit ? (target as WorkerUnit).data : null;
        int herdCount = 0;
        if (targetCombatData != null) herdCount = targetCombatData.captureHerdCount;
        else if (targetWorkerData != null) herdCount = targetWorkerData.captureHerdCount;
        if (herdCount > 0)
        {
            if (targetCombatData != null)
                attackerCiv.AddAnimalsToNearestHerd(targetCombatData, herdCount, target.planetIndex, target.currentTileIndex);
            else if (targetWorkerData != null)
                attackerCiv.AddAnimalsToNearestHerd(targetWorkerData, herdCount, target.planetIndex, target.currentTileIndex);
        }

        // Destroy the captured unit GameObject without triggering normal kill flow
        try { Object.Destroy(target.gameObject); } catch { }

        if (cu != null)
        {
            try { cu.ConsumeAction(); } catch { }
        }
    }
}

// ─────────────────────── Approach Then Attack ───────────────────────

/// <summary>
/// Move toward a target that is currently out of range, then attack if we arrive adjacent.
/// </summary>
public class AIApproachCommand : AICommand
{
    public BaseUnit target;
    public int approachTileIndex;

    public override bool CanExecute()
    {
        if (unit == null || target == null || unit.isStored) return false;
        if (approachTileIndex < 0) return false;
        return unit.CanReachTile(approachTileIndex);
    }

    public override void Execute()
    {
        unit.MoveTo(approachTileIndex);
    }
}

// ─────────────────────────── Fortify ───────────────────────────

/// <summary>
/// Unit stays in place and skips its turn to gain a defensive posture.
/// </summary>
public class AIFortifyCommand : AICommand
{
    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        return true;
    }

    public override void Execute()
    {
        unit.Fortify();
    }
}

// ─────────────────────────── Retreat ───────────────────────────

public class AIRetreatCommand : AICommand
{
    public int retreatTileIndex;

    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        if (retreatTileIndex < 0) return false;
        return unit.CanReachTile(retreatTileIndex);
    }

    public override void Execute()
    {
        unit.MoveTo(retreatTileIndex);
    }
}

// ─────────────────────────── Settle City ───────────────────────────

public class AISettleCityCommand : AICommand
{
    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        var worker = unit as WorkerUnit;
        return worker != null && worker.CanFoundCityOnCurrentTile();
    }

    public override void Execute()
    {
        var worker = unit as WorkerUnit;
        if (worker != null) worker.FoundCity();
    }
}

// ─────────────────────────── Build Improvement ───────────────────────────

public class AIBuildImprovementCommand : AICommand
{
    public ImprovementData improvement;

    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        var worker = unit as WorkerUnit;
        if (worker == null || improvement == null) return false;
        return worker.currentWorkPoints > 0 && worker.currentTileIndex >= 0;
    }

    public override void Execute()
    {
        var worker = unit as WorkerUnit;
        if (worker != null) worker.StartBuilding(improvement, worker.currentTileIndex);
    }
}

// ─────────────────────────── Forage ───────────────────────────

public class AIForageCommand : AICommand
{
    public ResourceData resource;

    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        var worker = unit as WorkerUnit;
        if (worker == null || resource == null) return false;
        return worker.CanForage(resource, worker.currentTileIndex);
    }

    public override void Execute()
    {
        var worker = unit as WorkerUnit;
        if (worker == null) return;
        worker.Forage(resource, worker.currentTileIndex);
        ResourceManager.Instance?.ForageResource(
            ResourceManager.Instance.GetResourceInstanceAtTile(worker.currentTileIndex, worker.planetIndex),
            worker.owner);
    }
}

// ─────────────────────────── Enter Orbit ───────────────────────────

public class AIEnterOrbitCommand : AICommand
{
    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        var cu = unit as CombatUnit;
        return cu != null && cu.CanEnterOrbit() && !cu.IsInOrbit;
    }

    public override void Execute()
    {
        var cu = unit as CombatUnit;
        if (cu != null)
        {
            cu.EnterOrbit(cu.currentTileIndex);
            cu.ConsumeAction();
        }
    }
}

// ─────────────────────────── Explore ───────────────────────────

/// <summary>
/// Move toward unexplored/fog-covered tiles to reveal the map.
/// </summary>
public class AIExploreCommand : AICommand
{
    public int targetTileIndex;

    public override bool CanExecute()
    {
        if (unit == null || unit.isStored) return false;
        if (targetTileIndex < 0 || targetTileIndex == unit.currentTileIndex) return false;
        return unit.CanReachTile(targetTileIndex);
    }

    public override void Execute()
    {
        unit.MoveTo(targetTileIndex);
    }
}

// ─────────────────────────── Unstore from Shelter ───────────────────────────

public class AIUnstoreCommand : AICommand
{
    public override bool CanExecute()
    {
        if (unit == null || !unit.isStored || unit.storedInImprovement == null) return false;
        return true;
    }

    public override void Execute()
    {
        unit.storedInImprovement.TryUnstoreUnit(unit);
    }
}
