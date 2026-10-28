using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Non-destructive pre-battle confrontation controller. The screen itself is an authored prefab.</summary>
public sealed class BattlePreviewUI : MonoBehaviour
{
    private const string PanelResource = BattleUIUtil.ResourceFolder + "BattleConfrontationPanel";

    private BattleManager manager;
    private EngagementPreview preview;
    private BattlePreviewPanel panel;
    private BattleTooltipView modalTooltip;
    private Coroutine forecastRoutine;
    private readonly BattleAutoResolveEstimator estimator = new();
    private readonly List<CommanderChoice> commanderChoices = new();
    private int commanderChoiceIndex, commandRoleIndex;
    private Civilization previewMusicCiv;
    private struct CommanderChoice { public BattleSide Side; public CommanderCharacterKind Kind; public int Id; public string Name; }

    public static BattlePreviewUI GetOrCreate(BattleManager manager)
    { var existing=manager.GetComponent<BattlePreviewUI>(); return existing!=null?existing:manager.gameObject.AddComponent<BattlePreviewUI>(); }

    public void Bind(BattleManager battleManager)
    {
        if(manager==battleManager)return;
        if(manager!=null){manager.BattlePreviewOpened-=Show;manager.BattlePreviewClosed-=Hide;}
        manager=battleManager;manager.BattlePreviewOpened+=Show;manager.BattlePreviewClosed+=Hide;Build();
    }
    private void OnDestroy(){RestorePreviewMusic();if(manager!=null){manager.BattlePreviewOpened-=Show;manager.BattlePreviewClosed-=Hide;}}

    private void Build()
    {
        if(panel!=null)return;
        var prefab=Resources.Load<BattlePreviewPanel>(PanelResource);
        if(prefab==null){Debug.LogError($"[BattlePreviewUI] Resources/{PanelResource}.prefab is missing. Run Tools > Battle UI > Build Tactical Battle Prefabs.");return;}
        panel=Instantiate(prefab,transform,false);panel.name="Battle Confrontation";
        panel.PreviousCommanderButton.onClick.AddListener(PreviousCommander);panel.NextCommanderButton.onClick.AddListener(NextCommander);
        panel.RoleButton.onClick.AddListener(NextRole);panel.AssignButton.onClick.AddListener(AssignSelectedCommander);
        panel.RetreatButton.onClick.AddListener(Retreat);panel.AutoResolveButton.onClick.AddListener(AutoResolve);
        panel.FightButton.onClick.AddListener(BeginManual);panel.CancelButton.onClick.AddListener(Cancel);
        BindTooltip(panel.FightButton, "Fight: Start a manual tactical battle.");
        BindTooltip(panel.AutoResolveButton, "Auto-Resolve: Resolve the engagement automatically.");
        BindTooltip(panel.RetreatButton, "Retreat: Withdraw before battle if permitted.");
        BindTooltip(panel.CancelButton, "Back: Return to the campaign if cancellation is permitted.");
        BindTooltip(panel.PreviousCommanderButton, "Previous Commander: Select the previous available commander.");
        BindTooltip(panel.NextCommanderButton, "Next Commander: Select the next available commander.");
        BindTooltip(panel.RoleButton, "Commander Role: Change the selected command role.");
        BindTooltip(panel.AssignButton, "Assign Commander: Assign the selected commander to this formation.");
        panel.gameObject.SetActive(false);
    }

    private void Show(EngagementPreview value)
    {
        Build();if(panel==null||value==null)return;preview=value;
        BeginPreviewMusic(value);
        ShowSide(panel.AttackerSide,value,BattleSide.Attacker);ShowSide(panel.DefenderSide,value,BattleSide.Defender);
        panel.SetBattlefield(BuildBattlefield(value));panel.SetButtons(value.AllowsManualBattle,value.AllowsRetreat,value.AllowsCancel);
        PopulateCommanderChoices(value);panel.gameObject.SetActive(true);RefreshForecast();
    }
    public void PresentRestored(EngagementPreview value)=>Show(value);
    private void Hide(){if(forecastRoutine!=null)StopCoroutine(forecastRoutine);forecastRoutine=null;RestorePreviewMusic();preview=null;if(panel!=null)panel.gameObject.SetActive(false);}

    private void BeginPreviewMusic(EngagementPreview value)
    {
        var playerCiv = value.Attacker?.owner != null && value.Attacker.owner.isPlayerControlled
            ? value.Attacker.owner
            : value.Defender?.owner != null && value.Defender.owner.isPlayerControlled
                ? value.Defender.owner
                : null;
        if (playerCiv == null || MusicManager.Instance == null) return;
        previewMusicCiv = playerCiv;
        MusicManager.Instance.UpdateMusic(playerCiv, playerCiv.GetCurrentAge(), DiplomaticState.War);
    }

    private void RestorePreviewMusic()
    {
        if (previewMusicCiv == null || MusicManager.Instance == null) return;
        MusicManager.Instance.UpdateMusic(previewMusicCiv, previewMusicCiv.GetCurrentAge(), DiplomaticState.Peace);
        previewMusicCiv = null;
    }

    private void RefreshForecast()
    {
        if(forecastRoutine!=null)StopCoroutine(forecastRoutine);
        panel.SetForecastPending();
        forecastRoutine=StartCoroutine(estimator.Estimate(manager,preview,ShowForecast));
    }
    private void ShowForecast(BattleAutoResolveForecast f)
    {
        if(f==null||preview==null||panel==null)return;int a=Mathf.RoundToInt(f.AttackerWinProbability*100),d=100-a;
        panel.SetForecast($"{a}%  |  {d}%",f.AdvantageLabel,$"Projected losses (estimate): Attacker ~{f.EstimatedAttackerSoldierLosses:N0}  •  Defender ~{f.EstimatedDefenderSoldierLosses:N0}",JoinFactors(f.AttackerFactors,f.DefenderFactors));
        forecastRoutine=null;
    }
    private static string JoinFactors(List<string>a,List<string>d){var s=new StringBuilder();foreach(var x in a)s.Append("Attacker: + ").Append(x).Append('\n');foreach(var x in d)s.Append("Defender: + ").Append(x).Append('\n');return s.ToString().TrimEnd();}

    private static void ShowSide(BattleConfrontationSideView view,EngagementPreview p,BattleSide side)
    {
        var units=side==BattleSide.Attacker?p.AttackerUnits:p.DefenderUnits;var owner=BattleUIUtil.SideOwner(p,side);
        var b=new StringBuilder();bool any=false;foreach(var g in p.Reinforcements)if(g.Side==side&&g.IsEligible){any=true;b.Append(Soldiers(g.Units).ToString("N0")).Append(" soldiers - expected Round ").Append(Mathf.Max(2,g.AvailableFromRound)).Append('\n');}
        view.Set(side.ToString().ToUpperInvariant(),BattleUIUtil.CivName(owner),BattleUIUtil.CivEmblem(owner),CommanderSummary(units),$"{Soldiers(units):N0} soldiers",$"{units.Count} units",Composition(units),any?b.ToString().TrimEnd():"None");
    }
    private static int Soldiers(IReadOnlyList<BattleUnitSnapshot> units){int n=0;for(int i=0;i<units.Count;i++)n+=Mathf.Max(0,units[i].SoldierCount);return n;}
    private static string Composition(IReadOnlyList<BattleUnitSnapshot> units){var counts=new Dictionary<string,int>();foreach(var u in units){string key=u.UnitData!=null?u.UnitData.unitType.ToString():u.Domain.ToString();counts.TryGetValue(key,out int n);counts[key]=n+1;}if(counts.Count==0)return "None";var b=new StringBuilder();foreach(var x in counts)b.Append(x.Key).Append(" ×").Append(x.Value).Append('\n');return b.ToString().TrimEnd();}
    private static string BuildBattlefield(EngagementPreview p){var b=new StringBuilder($"{p.Theater} • {p.PlanetaryEnvironment}");if(p.SiegeType!=BattleSiegeType.None)b.Append("\n<b>").Append(p.FortificationProfile!=null?p.FortificationProfile.displayName:"Fortified position").Append("</b> — ").Append(p.Fortifications.Count).Append(" defenses");return b.ToString();}

    private static string CommanderSummary(IReadOnlyList<BattleUnitSnapshot> units)
    {
        var service=MilitaryCommanderAssignmentService.Instance;if(service==null)return "<b>No Commander</b>";MilitaryCommanderAssignment best=null;BattleUnitSnapshot unit=null;
        foreach(var u in units)foreach(var a in service.PeekAssignments(u.FormationId))if(best==null||(a.Role==CommandRole.OverallCommander&&best.Role!=CommandRole.OverallCommander)){best=a;unit=u;}
        if(best==null)return "<b>No Commander</b>";string name="Commander",detail="";int level=1;
        if(best.CharacterKind==CommanderCharacterKind.Governor&&unit?.Owner!=null){var g=unit.Owner.governors.Find(x=>x!=null&&x.Id==best.CharacterId);if(g!=null){name=g.Name;level=g.Level;var traits=new List<string>();if(g.specialization==Governor.Specialization.Military)traits.Add("Military specialist");foreach(var t in g.Traits)if(t!=null&&t.combatBonusModifier!=0&&traits.Count<4)traits.Add(t.traitName);detail=string.Join(", ",traits);}}
        else {var a=AdmiralManager.Instance?.GetAdmiral(best.CharacterId);if(a!=null){name=a.admiralName;level=a.level;detail=$"Tactics {a.tactics} • Command {a.command}";}}
        float attack=service.GetAttackMultiplierReadOnly(unit.FormationId,unit.Domain),defense=service.GetDefenseMultiplierReadOnly(unit.FormationId,unit.Domain);
        return $"<b>{name}</b> ({best.CharacterKind})\n{best.Role} • Level {level} • {best.Status}\nAttack {(attack-1f):+0%;-0%;0%} / Defense {(defense-1f):+0%;-0%;0%}"+(string.IsNullOrEmpty(detail)?"":"\n"+detail);
    }

    private void PopulateCommanderChoices(EngagementPreview p){commanderChoices.Clear();AddChoices(p.Attacker?.owner,BattleSide.Attacker);AddChoices(p.Defender?.owner,BattleSide.Defender);commanderChoiceIndex=Mathf.Clamp(commanderChoiceIndex,0,Mathf.Max(0,commanderChoices.Count-1));RefreshCommanderSelection();}
    private void AddChoices(Civilization owner,BattleSide side){if(owner==null||!owner.isPlayerControlled)return;foreach(var g in owner.governors)if(g!=null)commanderChoices.Add(new CommanderChoice{Side=side,Kind=CommanderCharacterKind.Governor,Id=g.Id,Name=$"{g.Name} • Level {g.Level} • {g.specialization}"});int ownerId=CivilizationManager.Instance!=null?CivilizationManager.Instance.GetCivIndex(owner):-1;if(AdmiralManager.Instance!=null)foreach(var a in AdmiralManager.Instance.admirals)if(a!=null&&a.ownerCivilizationId==ownerId&&a.status==AdmiralStatus.Active)commanderChoices.Add(new CommanderChoice{Side=side,Kind=CommanderCharacterKind.Admiral,Id=a.admiralId,Name=$"{a.admiralName} • Level {a.level} • Tactics {a.tactics}"});}
    private void PreviousCommander(){if(commanderChoices.Count>0)commanderChoiceIndex=(commanderChoiceIndex-1+commanderChoices.Count)%commanderChoices.Count;RefreshCommanderSelection();}
    private void NextCommander(){if(commanderChoices.Count>0)commanderChoiceIndex=(commanderChoiceIndex+1)%commanderChoices.Count;RefreshCommanderSelection();}
    private void NextRole(){commandRoleIndex=(commandRoleIndex+1)%System.Enum.GetValues(typeof(CommandRole)).Length;RefreshCommanderSelection();}
    private void RefreshCommanderSelection(){bool available=commanderChoices.Count>0;panel.PreviousCommanderButton.interactable=available;panel.NextCommanderButton.interactable=available;panel.RoleButton.interactable=available;panel.AssignButton.interactable=available;if(!available){panel.SetCommanderSelector("No eligible player commanders");SetTooltip(panel.AssignButton,"Assign Commander unavailable: No eligible player commanders.");return;}var c=commanderChoices[commanderChoiceIndex];panel.SetCommanderSelector($"<b>COMMANDER SELECTOR</b> — {c.Side}\n{c.Name} — {(CommandRole)commandRoleIndex}");SetTooltip(panel.AssignButton,$"Assign Commander: Assign {c.Name} as {(CommandRole)commandRoleIndex}.");}
    private void AssignSelectedCommander(){if(manager==null||commanderChoices.Count==0)return;var c=commanderChoices[commanderChoiceIndex];string reason;bool ok=c.Kind==CommanderCharacterKind.Governor?manager.TryAssignGovernorCommander(c.Side,c.Id,(CommandRole)commandRoleIndex,out reason):manager.TryAssignAdmiralCommander(c.Side,c.Id,(CommandRole)commandRoleIndex,out reason);if(!ok)UIManager.Instance?.ShowNotification(reason);else{UIManager.Instance?.ShowNotification($"Assigned {c.Name}.");Show(preview);}}

    private void BeginManual()=>manager?.BeginPendingManualBattle();private void AutoResolve(){manager?.AutoResolvePendingPreview(out _);}private void Cancel()=>manager?.CancelPreview();
    private void Retreat(){if(manager!=null&&!manager.RetreatPendingPreview(out string reason))UIManager.Instance?.ShowNotification(reason);}

    private static void SetTooltip(UnityEngine.UI.Button button, string text)
    { var tip=button!=null?button.GetComponent<BattleHoverTip>():null;if(tip!=null)tip.SetText(text); }

    private void BindTooltip(UnityEngine.UI.Button button, string text)
    {
        if (button == null) return;
        modalTooltip ??= panel.GetComponentInChildren<BattleTooltipView>(true)
            ?? manager.GetComponentInChildren<BattleHUD>(true)?.CreateTooltipOverlay((RectTransform)panel.transform);
        var tip = button.GetComponent<BattleHoverTip>();
        if (tip == null) tip = button.gameObject.AddComponent<BattleHoverTip>();
        tip.Initialize(modalTooltip);
        tip.SetText(text);
    }
}
