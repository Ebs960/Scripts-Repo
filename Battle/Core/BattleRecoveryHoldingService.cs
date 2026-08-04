using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BattleRecoveryHoldingRecord
{
    public int CampaignRuntimeId;
    public string FormationId;
    public int PlanetIndex, CampaignTile, SpaceTile, Layer;
    public bool DeepSpace;
    public string Reason;
}

/// <summary>Durable recovery queue for living units that cannot currently be registered legally.</summary>
public sealed class BattleRecoveryHoldingService : MonoBehaviour, ISaveGameParticipant
{
    [Serializable] private sealed class SaveData { public List<BattleRecoveryHoldingRecord> records=new(); }
    public static BattleRecoveryHoldingService Instance { get; private set; }
    [SerializeField] private List<BattleRecoveryHoldingRecord> records=new();
    public IReadOnlyList<BattleRecoveryHoldingRecord> Records => records;
    public string SaveKey => "BattleRecoveryHolding_v1";

    private void Awake() { if (Instance!=null&&Instance!=this){Destroy(gameObject);return;} Instance=this; DontDestroyOnLoad(gameObject); SaveGameRegistry.Register(this); }
    private void OnDestroy(){if(Instance==this)Instance=null; SaveGameRegistry.Unregister(this);}
    public static BattleRecoveryHoldingService GetOrCreate()
    { if(Instance!=null)return Instance; var existing=FindAnyObjectByType<BattleRecoveryHoldingService>(); return existing!=null?existing:new GameObject("Battle Recovery Holding").AddComponent<BattleRecoveryHoldingService>(); }

    public void Hold(CombatUnit unit, string reason, bool deepSpace)
    {
        if(unit==null)return; int id=unit.gameObject.GetRuntimeId();
        var record=records.Find(r=>r.CampaignRuntimeId==id);
        if(record==null){record=new BattleRecoveryHoldingRecord{CampaignRuntimeId=id};records.Add(record);}
        record.FormationId=unit.MilitaryFormationId; record.PlanetIndex=unit.planetIndex; record.CampaignTile=unit.currentTileIndex;
        record.SpaceTile=unit.currentSpaceTileIndex; record.Layer=(int)unit.currentLayer; record.DeepSpace=deepSpace; record.Reason=reason;
    }
    public void Resolve(CombatUnit unit){if(unit==null)return;int id=unit.gameObject.GetRuntimeId();records.RemoveAll(r=>r.CampaignRuntimeId==id);}
    public string CaptureStateJson()=>JsonUtility.ToJson(new SaveData{records=new List<BattleRecoveryHoldingRecord>(records)});
    public void RestoreStateJson(string json){var data=string.IsNullOrEmpty(json)?null:JsonUtility.FromJson<SaveData>(json);records=data?.records??new List<BattleRecoveryHoldingRecord>();}
}
