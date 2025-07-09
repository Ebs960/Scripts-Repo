using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Centralized event manager for game-wide events.
/// Uses object pooling and observer pattern to optimize event handling.
/// </summary>
public class GameEventManager : MonoBehaviour
{
    public static GameEventManager Instance { get; private set; }
    
    // Event pools for common event types
    private Queue<UnitMovementEventArgs> movementEventPool = new Queue<UnitMovementEventArgs>();
    private Queue<CombatEventArgs> combatEventPool = new Queue<CombatEventArgs>();
    private Queue<ResourceEventArgs> resourceEventPool = new Queue<ResourceEventArgs>();
    private Queue<TileEventArgs> tileEventPool = new Queue<TileEventArgs>();
    
    #region Event Declarations
    
    // Unit Movement Events
    public event Action<UnitMovementEventArgs> OnUnitMoved;
    public event Action<UnitMovementEventArgs> OnMovementStarted;
    public event Action<UnitMovementEventArgs> OnMovementCompleted;
    
    // Combat Events
    public event Action<CombatEventArgs> OnCombatStarted;
    public event Action<CombatEventArgs> OnDamageApplied;
    public event Action<CombatEventArgs> OnUnitKilled;
    
    // Resource Events
    public event Action<ResourceEventArgs> OnResourceHarvested;
    public event Action<ResourceEventArgs> OnResourceAdded;
    
    // Tile Events
    public event Action<TileEventArgs> OnTileSelected;
    public event Action<TileEventArgs> OnTileImproved;
    
    // Turn Events
    public event Action OnTurnStarted;
    public event Action OnTurnEnded;
    
    #endregion
    
    #region Event Args Classes
    
    // Base class for all event args
    public abstract class GameEventArgs
    {
        public float Timestamp { get; private set; }
        
        public void Initialize()
        {
            Timestamp = Time.time;
        }
        
        public virtual void Reset() { }
    }
    
    // Unit movement event arguments
    public class UnitMovementEventArgs : GameEventArgs
    {
        public MonoBehaviour Unit { get; private set; }
        public int FromTileIndex { get; private set; }
        public int ToTileIndex { get; private set; }
        public int MovementCost { get; private set; }
        
        public void Setup(MonoBehaviour unit, int fromTile, int toTile, int cost)
        {
            Initialize();
            Unit = unit;
            FromTileIndex = fromTile;
            ToTileIndex = toTile;
            MovementCost = cost;
        }
        
        public override void Reset()
        {
            Unit = null;
            FromTileIndex = -1;
            ToTileIndex = -1;
            MovementCost = 0;
        }
    }
    
    // Combat event arguments
    public class CombatEventArgs : GameEventArgs
    {
        public MonoBehaviour Attacker { get; private set; }
        public MonoBehaviour Defender { get; private set; }
        public int Damage { get; private set; }
        public bool IsCounterAttack { get; private set; }
        public bool IsLethal { get; private set; }
        
        public void Setup(MonoBehaviour attacker, MonoBehaviour defender, int damage, bool isCounter = false, bool isLethal = false)
        {
            Initialize();
            Attacker = attacker;
            Defender = defender;
            Damage = damage;
            IsCounterAttack = isCounter;
            IsLethal = isLethal;
        }
        
        public override void Reset()
        {
            Attacker = null;
            Defender = null;
            Damage = 0;
            IsCounterAttack = false;
            IsLethal = false;
        }
    }
    
    // Resource event arguments
    public class ResourceEventArgs : GameEventArgs
    {
        public MonoBehaviour Source { get; private set; }
        public string ResourceType { get; private set; }
        public int Amount { get; private set; }
        
        public void Setup(MonoBehaviour source, string resourceType, int amount)
        {
            Initialize();
            Source = source;
            ResourceType = resourceType;
            Amount = amount;
        }
        
        public override void Reset()
        {
            Source = null;
            ResourceType = null;
            Amount = 0;
        }
    }
    
    // Tile event arguments
    public class TileEventArgs : GameEventArgs
    {
        public int TileIndex { get; private set; }
        public MonoBehaviour Cause { get; private set; }
        
        public void Setup(int tileIndex, MonoBehaviour cause = null)
        {
            Initialize();
            TileIndex = tileIndex;
            Cause = cause;
        }
        
        public override void Reset()
        {
            TileIndex = -1;
            Cause = null;
        }
    }
    
    #endregion
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Pre-populate pools
        PrePopulateEventPools();
    }
    
    private void PrePopulateEventPools()
    {
        // Pre-create some event objects to reduce runtime allocations
        for (int i = 0; i < 10; i++)
        {
            movementEventPool.Enqueue(new UnitMovementEventArgs());
            combatEventPool.Enqueue(new CombatEventArgs());
            resourceEventPool.Enqueue(new ResourceEventArgs());
            tileEventPool.Enqueue(new TileEventArgs());
        }
    }
    
    #region Event Raising Methods
    
    // Movement events
    public void RaiseUnitMovedEvent(MonoBehaviour unit, int fromTile, int toTile, int cost)
    {
        if (OnUnitMoved == null) return;
        
        var args = GetMovementEventArgs();
        args.Setup(unit, fromTile, toTile, cost);
        OnUnitMoved.Invoke(args);
        ReturnMovementEventArgs(args);
    }
    
    public void RaiseMovementStartedEvent(MonoBehaviour unit, int fromTile, int toTile, int cost)
    {
        if (OnMovementStarted == null) return;
        
        var args = GetMovementEventArgs();
        args.Setup(unit, fromTile, toTile, cost);
        OnMovementStarted.Invoke(args);
        ReturnMovementEventArgs(args);
    }
    
    public void RaiseMovementCompletedEvent(MonoBehaviour unit, int fromTile, int toTile, int cost)
    {
        if (OnMovementCompleted == null) return;
        
        var args = GetMovementEventArgs();
        args.Setup(unit, fromTile, toTile, cost);
        OnMovementCompleted.Invoke(args);
        ReturnMovementEventArgs(args);
    }
    
    // Combat events
    public void RaiseCombatStartedEvent(MonoBehaviour attacker, MonoBehaviour defender)
    {
        if (OnCombatStarted == null) return;
        
        var args = GetCombatEventArgs();
        args.Setup(attacker, defender, 0);
        OnCombatStarted.Invoke(args);
        ReturnCombatEventArgs(args);
    }
    
    public void RaiseDamageAppliedEvent(MonoBehaviour attacker, MonoBehaviour defender, int damage, bool isCounter = false)
    {
        if (OnDamageApplied == null) return;
        
        var args = GetCombatEventArgs();
        args.Setup(attacker, defender, damage, isCounter);
        OnDamageApplied.Invoke(args);
        ReturnCombatEventArgs(args);
    }
    
    public void RaiseUnitKilledEvent(MonoBehaviour attacker, MonoBehaviour defender, int damage)
    {
        if (OnUnitKilled == null) return;
        
        var args = GetCombatEventArgs();
        args.Setup(attacker, defender, damage, false, true);
        OnUnitKilled.Invoke(args);
        ReturnCombatEventArgs(args);
    }
    
    // Resource events
    public void RaiseResourceHarvestedEvent(MonoBehaviour source, string resourceType, int amount)
    {
        if (OnResourceHarvested == null) return;
        
        var args = GetResourceEventArgs();
        args.Setup(source, resourceType, amount);
        OnResourceHarvested.Invoke(args);
        ReturnResourceEventArgs(args);
    }
    
    public void RaiseResourceAddedEvent(MonoBehaviour source, string resourceType, int amount)
    {
        if (OnResourceAdded == null) return;
        
        var args = GetResourceEventArgs();
        args.Setup(source, resourceType, amount);
        OnResourceAdded.Invoke(args);
        ReturnResourceEventArgs(args);
    }
    
    // Tile events
    public void RaiseTileSelectedEvent(int tileIndex, MonoBehaviour cause = null)
    {
        if (OnTileSelected == null) return;
        
        var args = GetTileEventArgs();
        args.Setup(tileIndex, cause);
        OnTileSelected.Invoke(args);
        ReturnTileEventArgs(args);
    }
    
    public void RaiseTileImprovedEvent(int tileIndex, MonoBehaviour cause = null)
    {
        if (OnTileImproved == null) return;
        
        var args = GetTileEventArgs();
        args.Setup(tileIndex, cause);
        OnTileImproved.Invoke(args);
        ReturnTileEventArgs(args);
    }
    
    // Turn events
    public void RaiseTurnStartedEvent()
    {
        OnTurnStarted?.Invoke();
    }
    
    public void RaiseTurnEndedEvent()
    {
        OnTurnEnded?.Invoke();
    }
    
    #endregion
    
    #region Object Pool Methods
    
    // Get event args from pools
    private UnitMovementEventArgs GetMovementEventArgs()
    {
        if (movementEventPool.Count > 0)
            return movementEventPool.Dequeue();
        return new UnitMovementEventArgs();
    }
    
    private CombatEventArgs GetCombatEventArgs()
    {
        if (combatEventPool.Count > 0)
            return combatEventPool.Dequeue();
        return new CombatEventArgs();
    }
    
    private ResourceEventArgs GetResourceEventArgs()
    {
        if (resourceEventPool.Count > 0)
            return resourceEventPool.Dequeue();
        return new ResourceEventArgs();
    }
    
    private TileEventArgs GetTileEventArgs()
    {
        if (tileEventPool.Count > 0)
            return tileEventPool.Dequeue();
        return new TileEventArgs();
    }
    
    // Return event args to pools
    private void ReturnMovementEventArgs(UnitMovementEventArgs args)
    {
        args.Reset();
        movementEventPool.Enqueue(args);
    }
    
    private void ReturnCombatEventArgs(CombatEventArgs args)
    {
        args.Reset();
        combatEventPool.Enqueue(args);
    }
    
    private void ReturnResourceEventArgs(ResourceEventArgs args)
    {
        args.Reset();
        resourceEventPool.Enqueue(args);
    }
    
    private void ReturnTileEventArgs(TileEventArgs args)
    {
        args.Reset();
        tileEventPool.Enqueue(args);
    }
    
    #endregion
}