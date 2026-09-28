using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>HUD-styled, non-destructive pre-battle confrontation screen.</summary>
public sealed class BattlePreviewUI : MonoBehaviour
{
    private BattleManager manager;
    private EngagementPreview preview;
    private GameObject root;
    private TextMeshProUGUI attackerText, defenderText, forecastText, battlefieldText, commanderSelection;
    private Button fightButton, autoResolveButton, retreatButton, cancelButton;
    private Coroutine forecastRoutine;
    private readonly BattleAutoResolveEstimator estimator = new();
    private readonly List<CommanderChoice> commanderChoices = new();
    private int commanderChoiceIndex, commandRoleIndex;
    private struct CommanderChoice { public BattleSide Side; public CommanderCharacterKind Kind; public int Id; public string Name; }

    public static BattlePreviewUI GetOrCreate(BattleManager manager)
    { var existing=manager.GetComponent<BattlePreviewUI>(); return existing!=null?existing:manager.gameObject.AddComponent<BattlePreviewUI>(); }

    public void Bind(BattleManager battleManager)
    {
        if(manager==battleManager)return;
        if(manager!=null){manager.BattlePreviewOpened-=Show;manager.BattlePreviewClosed-=Hide;}
        manager=battleManager;manager.BattlePreviewOpened+=Show;manager.BattlePreviewClosed+=Hide;Build();
    }
    private void OnDestroy(){if(manager!=null){manager.BattlePreviewOpened-=Show;manager.BattlePreviewClosed-=Hide;}}

    private void Build()
    {
        if(root!=null)return;
        root=new GameObject("Battle Confrontation",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=500;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var shade=Panel(root.transform,"Backdrop",new Color(.015f,.02f,.025f,.82f));Stretch(shade.GetComponent<RectTransform>());
        var panel=Panel(root.transform,"Confrontation Panel",new Color(.075f,.085f,.09f,.985f));var pr=panel.GetComponent<RectTransform>();pr.anchorMin=pr.anchorMax=new Vector2(.5f,.5f);pr.sizeDelta=new Vector2(1280,900);
        var title=Text(panel.transform,"BATTLE / ENGAGEMENT",new Vector2(.5f,1),new Vector2(0,-24),new Vector2(800,50),30,TextAlignmentOptions.Center);title.fontStyle=FontStyles.Bold;
        attackerText=Text(panel.transform,"Attacker",new Vector2(0,1),new Vector2(55,-92),new Vector2(520,510),19,TextAlignmentOptions.TopLeft);
        defenderText=Text(panel.transform,"Defender",new Vector2(1,1),new Vector2(-575,-92),new Vector2(520,510),19,TextAlignmentOptions.TopLeft);
        battlefieldText=Text(panel.transform,"Battlefield",new Vector2(.5f,.5f),new Vector2(0,-105),new Vector2(1120,70),17,TextAlignmentOptions.Center);
        forecastText=Text(panel.transform,"Forecast",new Vector2(.5f,0),new Vector2(0,190),new Vector2(900,155),23,TextAlignmentOptions.Center);
        commanderSelection=Text(panel.transform,"Commander Selector",new Vector2(.5f,0),new Vector2(0,112),new Vector2(880,65),15,TextAlignmentOptions.Center);
        Button(panel.transform,"◀",new Vector2(-480,103),PreviousCommander,55);Button(panel.transform,"▶",new Vector2(480,103),NextCommander,55);
        Button(panel.transform,"ROLE",new Vector2(370,52),NextRole,100);Button(panel.transform,"ASSIGN",new Vector2(480,52),AssignSelectedCommander,100);
        retreatButton=Button(panel.transform,"RETREAT",new Vector2(-230,18),Retreat,190);
        autoResolveButton=Button(panel.transform,"AUTO-RESOLVE",new Vector2(0,18),AutoResolve,210);
        fightButton=Button(panel.transform,"FIGHT",new Vector2(230,18),BeginManual,190);
        cancelButton=Button(panel.transform,"Back",new Vector2(-570,18),Cancel,90,true);
        root.SetActive(false);
    }

    private void Show(EngagementPreview value)
    {
        Build();if(value==null)return;preview=value;
        attackerText.text=BuildSide(value,BattleSide.Attacker);defenderText.text=BuildSide(value,BattleSide.Defender);
        battlefieldText.text=BuildBattlefield(value);fightButton.interactable=value.AllowsManualBattle;autoResolveButton.interactable=true;
        retreatButton.interactable=value.AllowsRetreat;cancelButton.gameObject.SetActive(value.AllowsCancel);cancelButton.interactable=value.AllowsCancel;
        PopulateCommanderChoices(value);root.SetActive(true);RefreshForecast();
    }
    public void PresentRestored(EngagementPreview value)=>Show(value);
    private void Hide(){if(forecastRoutine!=null)StopCoroutine(forecastRoutine);forecastRoutine=null;preview=null;if(root!=null)root.SetActive(false);}

    private void RefreshForecast()
    {
        if(forecastRoutine!=null)StopCoroutine(forecastRoutine);
        forecastText.text="<b>ESTIMATED AUTO-RESOLVE ODDS</b>\nCalculating odds…";
        forecastRoutine=StartCoroutine(estimator.Estimate(manager,preview,ShowForecast));
    }
    private void ShowForecast(BattleAutoResolveForecast f)
    {
        if(f==null||preview==null)return;int a=Mathf.RoundToInt(f.AttackerWinProbability*100),d=100-a;
        string factors=JoinFactors(f.AttackerFactors,f.DefenderFactors);
        forecastText.text=$"<b>ESTIMATED AUTO-RESOLVE ODDS</b>\n<size=32>{a}%  |  {d}%</size>\n{f.AdvantageLabel}\n<size=16>Projected losses: Attacker ~{f.EstimatedAttackerSoldierLosses:N0}  •  Defender ~{f.EstimatedDefenderSoldierLosses:N0}{factors}</size>";
        forecastRoutine=null;
    }
    private static string JoinFactors(List<string>a,List<string>d){var s=new StringBuilder();foreach(var x in a)s.Append("\nAttacker: + ").Append(x);foreach(var x in d)s.Append("\nDefender: + ").Append(x);return s.ToString();}

    private static string BuildSide(EngagementPreview p,BattleSide side)
    {
        var units=side==BattleSide.Attacker?p.AttackerUnits:p.DefenderUnits;var source=side==BattleSide.Attacker?p.Attacker:p.Defender;
        int soldiers=Soldiers(units);var b=new StringBuilder();b.Append("<b>").Append(side.ToString().ToUpperInvariant()).Append("</b>\n");
        b.Append(source?.owner!=null?source.owner.name:"Unknown Civilization").Append("\n\n");b.Append(CommanderSummary(units)).Append("\n\n");
        b.Append("<size=27><b>").Append(soldiers.ToString("N0")).Append(" soldiers</b></size>\n").Append(units.Count).Append(" units\n\n<b>ARMY COMPOSITION</b>\n").Append(Composition(units));
        b.Append("\n\n<b>REINFORCEMENTS</b>\n");bool any=false;foreach(var g in p.Reinforcements)if(g.Side==side&&g.IsEligible){any=true;b.Append(Soldiers(g.Units).ToString("N0")).Append(" soldiers — expected Round ").Append(Mathf.Max(2,g.AvailableFromRound)).Append('\n');}if(!any)b.Append("None");return b.ToString();
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
    private void RefreshCommanderSelection(){if(commanderChoices.Count==0){commanderSelection.text="No eligible player commanders";return;}var c=commanderChoices[commanderChoiceIndex];commanderSelection.text=$"<b>COMMANDER SELECTOR</b> — {c.Side}\n{c.Name} — {(CommandRole)commandRoleIndex}";}
    private void AssignSelectedCommander(){if(manager==null||commanderChoices.Count==0)return;var c=commanderChoices[commanderChoiceIndex];string reason;bool ok=c.Kind==CommanderCharacterKind.Governor?manager.TryAssignGovernorCommander(c.Side,c.Id,(CommandRole)commandRoleIndex,out reason):manager.TryAssignAdmiralCommander(c.Side,c.Id,(CommandRole)commandRoleIndex,out reason);if(!ok)UIManager.Instance?.ShowNotification(reason);else{UIManager.Instance?.ShowNotification($"Assigned {c.Name}.");Show(preview);}}

    private void BeginManual()=>manager?.BeginPendingManualBattle();private void AutoResolve(){manager?.AutoResolvePendingPreview(out _);}private void Cancel()=>manager?.CancelPreview();
    private void Retreat(){if(manager!=null&&!manager.RetreatPendingPreview(out string reason))UIManager.Instance?.ShowNotification(reason);}
    private static GameObject Panel(Transform parent,string name,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);go.GetComponent<Image>().color=color;return go;}
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    private static TextMeshProUGUI Text(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size,float font,TextAlignmentOptions align){var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=anchor;r.pivot=anchor;r.anchoredPosition=pos;r.sizeDelta=size;var t=go.GetComponent<TextMeshProUGUI>();t.font=TMP_Settings.defaultFontAsset;t.fontSize=font;t.color=new Color(.92f,.89f,.78f);t.alignment=align;t.enableWordWrapping=true;return t;}
    private static Button Button(Transform parent,string label,Vector2 pos,UnityEngine.Events.UnityAction action,float width,bool secondary=false){var go=Panel(parent,label,secondary?new Color(.2f,.22f,.22f):new Color(.68f,.57f,.34f));var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,0);r.pivot=new Vector2(.5f,0);r.anchoredPosition=pos;r.sizeDelta=new Vector2(width,48);var button=go.AddComponent<Button>();button.onClick.AddListener(action);var t=Text(go.transform,"Label",new Vector2(.5f,.5f),Vector2.zero,new Vector2(width-8,42),16,TextAlignmentOptions.Center);t.color=Color.white;t.text=label;return button;}
}
