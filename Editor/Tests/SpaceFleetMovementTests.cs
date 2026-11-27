using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class SpaceFleetMovementTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly Dictionary<Type, object> previousInstances = new Dictionary<Type, object>();
    private SpaceWorldManager world;
    private SpaceFleetManager fleets;
    private SpaceShipMovementController movement;
    private FleetDeploymentManager deployment;
    private Civilization civilization;
    private SpaceHexGrid grid;

    private T Create<T>() where T : Component
    {
        var go = new GameObject("SpaceFleetTest_" + typeof(T).Name);
        objects.Add(go);
        return go.AddComponent<T>();
    }

    private void SetInstance<T>(T value) where T : class
    {
        var field = typeof(T).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        if (!previousInstances.ContainsKey(typeof(T))) previousInstances.Add(typeof(T), field.GetValue(null));
        field.SetValue(null, value);
    }

    [SetUp]
    public void SetUp()
    {
        SetInstance(Create<GameEventManager>());
        SetInstance<AdmiralManager>(null);
        var civManager = Create<CivilizationManager>();
        SetInstance(civManager);
        civilization = Create<Civilization>();
        civManager.RegisterCiv(civilization);
        world = Create<SpaceWorldManager>();
        SetInstance(world);
        world.CreateSystem(7, 4, 1f);
        grid = world.Grid;
        fleets = Create<SpaceFleetManager>();
        SetInstance(fleets);
        movement = Create<SpaceShipMovementController>();
        SetInstance(movement);
        movement.SetGrid(grid);
        deployment = Create<FleetDeploymentManager>();
        deployment.spaceGrid = grid;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in objects)
            if (go != null && go.GetComponent<CombatUnit>() != null) UnitRegistry.Unregister(go);
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
        foreach (var pair in previousInstances)
            pair.Key.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, pair.Value);
        previousInstances.Clear();
    }

    private int Tile(int q, int r)
    {
        Assert.That(grid.TryGetIndex(q, r, out int index), Is.True);
        return index;
    }

    private CombatUnit Ship(int tile, int movementPerTurn)
    {
        var unit = Create<CombatUnit>();
        unit.Initialize(null, civilization);
        unit.spaceMovementPointsPerTurn = movementPerTurn;
        unit.currentSpaceMovementPoints = movementPerTurn;
        unit.currentSpaceTileIndex = tile;
        unit.spaceLocation = SpaceLocation.InSpace(tile);
        unit.spaceFleetId = -1;
        civilization.combatUnits.Add(unit);
        UnitRegistry.Register(unit.gameObject);
        UnitRegistry.RegisterPersistent(unit.PersistentId, unit.gameObject);
        Assert.That(movement.PlaceOnSpaceTile(unit, tile), Is.True);
        return unit;
    }

    [Test]
    public void PackedFleetAdvancesTogetherAcrossTurnsUsingSlowestShip()
    {
        int start = Tile(0, 0), destination = Tile(3, 0);
        var fast = Ship(start, 3);
        var slow = Ship(start, 1);
        var fleet = fleets.CreateFleet(civilization.gameObject.GetRuntimeId(), "Fleet", -1, new[] { fast, slow });
        Assert.That(deployment.PackFleet(fleet, 3, out string reason), Is.True, reason);
        fast.currentSpaceMovementPoints = 3;
        slow.currentSpaceMovementPoints = 1;
        Assert.That(movement.TryGetPackedMembers(fleet, out var members, out reason), Is.True, reason);
        Assert.That(movement.GetReachableTiles(members, start).Contains(destination), Is.False);
        Assert.That(movement.QueueFleetMove(fleet, destination), Is.True);
        Assert.That(fleet.queuedPathCursor, Is.EqualTo(1));
        Assert.That(fast.currentSpaceTileIndex, Is.EqualTo(fleet.currentSpaceTileIndex));
        Assert.That(slow.currentSpaceTileIndex, Is.EqualTo(fleet.currentSpaceTileIndex));
        Assert.That(slow.currentSpaceMovementPoints, Is.Zero);
        SpaceShipMovementController.ProcessCivilizationTurn(civilization);
        Assert.That(fleet.queuedPathCursor, Is.EqualTo(2));
        SpaceShipMovementController.ProcessCivilizationTurn(civilization);
        Assert.That(fleet.currentSpaceTileIndex, Is.EqualTo(destination));
        Assert.That(fast.spaceLocation.spaceTileIndex, Is.EqualTo(destination));
        Assert.That(slow.spaceLocation.spaceTileIndex, Is.EqualTo(destination));
        Assert.That(grid.GetTile(destination).spacecraftIds, Does.Contain(fast.gameObject.GetRuntimeId()));
        Assert.That(grid.GetTile(destination).spacecraftIds, Does.Contain(slow.gameObject.GetRuntimeId()));
        Assert.That(fleet.memberUnitIds, Has.Count.EqualTo(2));
        Assert.That(fast.gameObject.activeSelf, Is.False);
        Assert.That(slow.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void BlockedOrdersAreRejectedAndDeploymentRepackingPreservesMembers()
    {
        int start = Tile(0, 0), blocked = Tile(1, 0);
        var first = Ship(start, 2);
        var second = Ship(start, 2);
        var fleet = fleets.CreateFleet(civilization.gameObject.GetRuntimeId(), "Fleet", -1, new[] { first, second });
        Assert.That(deployment.PackFleet(fleet, 3, out string reason), Is.True, reason);
        grid.GetTile(blocked).blocksMovement = true;
        Assert.That(movement.QueueFleetMove(fleet, blocked), Is.False);
        Assert.That(fleet.currentSpaceTileIndex, Is.EqualTo(start));
        Assert.That(deployment.UnpackFleet(fleet, out reason), Is.True, reason);
        Assert.That(first.gameObject.activeSelf, Is.True);
        Assert.That(second.gameObject.activeSelf, Is.True);
        Assert.That(first.currentSpaceTileIndex, Is.Not.EqualTo(second.currentSpaceTileIndex));
        Assert.That(grid.GetTile(first.currentSpaceTileIndex).spacecraftIds, Does.Contain(first.gameObject.GetRuntimeId()));
        Assert.That(grid.GetTile(second.currentSpaceTileIndex).spacecraftIds, Does.Contain(second.gameObject.GetRuntimeId()));
        Assert.That(deployment.PackFleet(fleet, 3, out reason), Is.True, reason);
        Assert.That(fleet.memberUnitIds, Has.Count.EqualTo(2));
        Assert.That(first.currentSpaceTileIndex, Is.EqualTo(second.currentSpaceTileIndex));
        Assert.That(first.spaceFleetId, Is.EqualTo(fleet.fleetId));
        Assert.That(second.spaceFleetId, Is.EqualTo(fleet.fleetId));
    }

    [Test]
    public void MergeTransfersMembershipWithoutDuplicatingUnits()
    {
        int start = Tile(0, 0);
        var first = Ship(start, 2);
        var second = Ship(start, 2);
        var target = fleets.CreateFleet(civilization.gameObject.GetRuntimeId(), "Target", -1, new[] { first });
        var source = fleets.CreateFleet(civilization.gameObject.GetRuntimeId(), "Source", -1, new[] { second });
        Assert.That(fleets.MergeFleets(target.fleetId, source.fleetId), Is.True);
        Assert.That(second.spaceFleetId, Is.EqualTo(target.fleetId));
        Assert.That(target.memberUnitIds, Has.Count.EqualTo(2));
        Assert.That(fleets.GetFleet(source.fleetId), Is.Null);
        Assert.That(fleets.MergeFleets(target.fleetId, source.fleetId), Is.False);
    }

    [Test]
    public void SnapshotRemapsPackedFleetMembersAndResumesSavedRoute()
    {
        int start = Tile(0, 0), destination = Tile(3, 0);
        var original = Ship(start, 1);
        var fleet = fleets.CreateFleet(civilization.gameObject.GetRuntimeId(), "Fleet", -1, new[] { original });
        Assert.That(deployment.PackFleet(fleet, 3, out string reason), Is.True, reason);
        original.currentSpaceMovementPoints = 1;
        Assert.That(movement.QueueFleetMove(fleet, destination), Is.True);
        int savedTile = fleet.currentSpaceTileIndex;
        int savedCursor = fleet.queuedPathCursor;
        var saved = JsonUtility.FromJson<SpaceWorldSaveData>(JsonUtility.ToJson(SpaceWorldPersistence.Capture()));

        var newCivManager = Create<CivilizationManager>();
        SetInstance(newCivManager);
        civilization = Create<Civilization>();
        newCivManager.RegisterCiv(civilization);
        var restored = Create<CombatUnit>();
        restored.Initialize(null, civilization);
        restored.spaceMovementPointsPerTurn = 1;
        restored.currentSpaceMovementPoints = original.currentSpaceMovementPoints;
        restored.currentSpaceTileIndex = savedTile;
        restored.spaceLocation = SpaceLocation.InSpace(savedTile);
        restored.spaceFleetId = fleet.fleetId;
        restored.RestorePersistentId(original.PersistentId);
        civilization.combatUnits.Add(restored);
        UnitRegistry.Register(restored.gameObject);
        SpaceWorldPersistence.Restore(saved);
        grid = world.Grid;
        var restoredFleet = fleets.GetFleet(fleet.fleetId);
        Assert.That(restoredFleet.memberUnitIds, Does.Contain(restored.gameObject.GetRuntimeId()));
        Assert.That(restoredFleet.queuedPathCursor, Is.EqualTo(savedCursor));
        Assert.That(restored.isPackedInSpaceFleet, Is.True);
        Assert.That(restored.gameObject.activeSelf, Is.False);
        SpaceShipMovementController.ProcessCivilizationTurn(civilization);
        SpaceShipMovementController.ProcessCivilizationTurn(civilization);
        Assert.That(restoredFleet.currentSpaceTileIndex, Is.EqualTo(destination));
        Assert.That(restored.currentSpaceTileIndex, Is.EqualTo(destination));
    }
}
