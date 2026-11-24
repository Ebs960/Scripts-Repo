using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EditMode tests for the pure victory rules that VictoryManager builds on. These run inside the
/// Unity Test Runner (Window > General > Test Runner > EditMode) and need no scene.
/// </summary>
public sealed class VictoryRulesTests
{
    [TestCase(VictoryType.Domination, true, "Domination Victory")]
    [TestCase(VictoryType.Science, true, "Science Victory")]
    [TestCase(VictoryType.Culture, true, "Culture Victory")]
    [TestCase(VictoryType.Science, false, "Science Victory — China")]
    public void RomeResultUsesPlayerArtworkAndActualCondition(VictoryType type, bool playerWon, string subtitle)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts Repo/UI/Endgame Screen.prefab");
        var rome = AssetDatabase.LoadAssetAtPath<CivData>("Assets/Scripts Repo/Civilizations/Rome.asset");
        var instance = Object.Instantiate(prefab);
        try
        {
            instance.GetComponent<EndgameScreenUI>().Present(new VictoryResult
            {
                Type = type,
                PlayerWon = playerWon,
                WinnerCivName = playerWon ? "Rome" : "China"
            }, rome, "Germanicus");
            var art = instance.transform.Find("Civilization Illustration").GetComponent<Image>();
            Assert.That(art.sprite.name, Is.EqualTo(playerWon ? "Rome Victory Screen" : "Rome Defeat Screen"));
            Assert.That(instance.transform.Find("Victory Condition").GetComponent<TMP_Text>().text, Is.EqualTo(subtitle));
            Assert.That(instance.transform.Find("Player Leader").GetComponent<TMP_Text>().text, Is.EqualTo("Germanicus"));
            var button = instance.transform.Find("Main Menu Button").GetComponent<Button>();
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(nameof(EndgameScreenUI.ReturnToMainMenu)));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private readonly List<ScriptableObject> created = new List<ScriptableObject>();

    private TechData Tech()
    {
        var tech = ScriptableObject.CreateInstance<TechData>();
        created.Add(tech);
        return tech;
    }

    private CultureData Culture()
    {
        var culture = ScriptableObject.CreateInstance<CultureData>();
        created.Add(culture);
        return culture;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var asset in created)
            if (asset != null) Object.DestroyImmediate(asset);
        created.Clear();
    }

    // ── Enabled types / AI mapping ────────────────────────────────────────────

    [Test]
    public void OnlyDominationScienceAndCultureAreEnabled()
    {
        Assert.IsTrue(VictoryRules.IsEnabled(VictoryType.Domination));
        Assert.IsTrue(VictoryRules.IsEnabled(VictoryType.Science));
        Assert.IsTrue(VictoryRules.IsEnabled(VictoryType.Culture));
        Assert.IsFalse(VictoryRules.IsEnabled(VictoryType.Religious));
        Assert.IsFalse(VictoryRules.IsEnabled(VictoryType.Diplomatic));
        Assert.IsFalse(VictoryRules.IsEnabled(VictoryType.Economic));
        CollectionAssert.AreEquivalent(
            new[] { VictoryType.Domination, VictoryType.Science, VictoryType.Culture },
            VictoryRules.EnabledTypes);
    }

    [Test]
    public void EnumValuesRemainStableForSerialization()
    {
        Assert.AreEqual(0, (int)VictoryType.Domination);
        Assert.AreEqual(1, (int)VictoryType.Science);
        Assert.AreEqual(2, (int)VictoryType.Culture);
        Assert.AreEqual(3, (int)VictoryType.Religious);
        Assert.AreEqual(4, (int)VictoryType.Diplomatic);
        Assert.AreEqual(5, (int)VictoryType.Economic);
    }

    [Test]
    public void UnsupportedLeaderPreferencesMapToSupportedTypes()
    {
        foreach (VictoryType type in System.Enum.GetValues(typeof(VictoryType)))
            Assert.IsTrue(VictoryRules.IsEnabled(VictoryRules.NormalizeForAi(type)), type.ToString());
        Assert.AreEqual(VictoryType.Science, VictoryRules.NormalizeForAi(VictoryType.Science));
        Assert.AreEqual(VictoryType.Culture, VictoryRules.NormalizeForAi(VictoryType.Religious));
        Assert.AreEqual(VictoryType.Science, VictoryRules.NormalizeForAi(VictoryType.Economic));
    }

    // ── Science ───────────────────────────────────────────────────────────────

    [Test]
    public void Science_AllButOneTechnology_IsNotComplete()
    {
        var catalog = new List<TechData> { Tech(), Tech(), Tech() };
        var researched = new List<TechData> { catalog[0], catalog[1] };

        Assert.IsFalse(VictoryRules.IsCatalogComplete(catalog, researched));
        Assert.AreEqual(2, VictoryRules.CountCatalogCompletion(catalog, researched, out int total));
        Assert.AreEqual(3, total);
    }

    [Test]
    public void Science_FinalTechnology_CompletesCatalog()
    {
        var catalog = new List<TechData> { Tech(), Tech(), Tech() };
        var researched = new List<TechData>(catalog);
        Assert.IsTrue(VictoryRules.IsCatalogComplete(catalog, researched));
    }

    [Test]
    public void Science_StartingTechnologiesCount()
    {
        var catalog = new List<TechData> { Tech(), Tech() };
        var startingTechs = new List<TechData> { catalog[0] };
        Assert.AreEqual(1, VictoryRules.CountCatalogCompletion(catalog, startingTechs, out _));
        startingTechs.Add(catalog[1]);
        Assert.IsTrue(VictoryRules.IsCatalogComplete(catalog, startingTechs));
    }

    [Test]
    public void Science_EmptyOrNullCatalog_NeverCompletes()
    {
        Assert.IsFalse(VictoryRules.IsCatalogComplete(new List<TechData>(), new List<TechData> { Tech() }));
        Assert.IsFalse(VictoryRules.IsCatalogComplete<TechData>(null, new List<TechData> { Tech() }));
        Assert.IsFalse(VictoryRules.IsCatalogComplete(new List<TechData> { null, null }, new List<TechData>()));
    }

    [Test]
    public void Science_NullsDuplicatesAndForeignEntriesAreIgnored()
    {
        var a = Tech();
        var b = Tech();
        var foreign = Tech();
        var catalog = new List<TechData> { a, null, b, a };
        var researched = new List<TechData> { a, a, null, foreign };

        Assert.AreEqual(1, VictoryRules.CountCatalogCompletion(catalog, researched, out int total));
        Assert.AreEqual(2, total);
        Assert.IsFalse(VictoryRules.IsCatalogComplete(catalog, researched));
    }

    [Test]
    public void Science_LegacyVictoryTechFlagDoesNotCompleteCatalog()
    {
        var victoryTech = Tech();
        victoryTech.IsVictoryTech = true;
        var catalog = new List<TechData> { victoryTech, Tech() };
        Assert.IsFalse(VictoryRules.IsCatalogComplete(catalog, new List<TechData> { victoryTech }));
    }

    // ── Culture ───────────────────────────────────────────────────────────────

    [Test]
    public void Culture_AllButOne_IsNotComplete_FinalCultureCompletes()
    {
        var catalog = new List<CultureData> { Culture(), Culture() };
        var adopted = new List<CultureData> { catalog[0] };
        Assert.IsFalse(VictoryRules.IsCatalogComplete(catalog, adopted));
        adopted.Add(catalog[1]);
        Assert.IsTrue(VictoryRules.IsCatalogComplete(catalog, adopted));
    }

    [Test]
    public void Culture_EmptyCatalog_NeverCompletes()
    {
        Assert.IsFalse(VictoryRules.IsCatalogComplete(new List<CultureData>(), new List<CultureData> { Culture() }));
    }

    // ── Domination ────────────────────────────────────────────────────────────

    private static readonly string[] Roster = { "Rome", "England", "France" };

    private static bool Evaluate(string candidate, HashSet<string> defeated, HashSet<string> subjectsOfCandidate,
        out int resolved, out int total, bool candidateSubordinated = false)
    {
        return VictoryRules.EvaluateDomination(
            Roster, candidate, candidateSubordinated,
            rival => defeated.Contains(rival),
            rival => subjectsOfCandidate.Contains(rival),
            out resolved, out total);
    }

    [Test]
    public void Domination_ConqueredRival_WinsWhenNoIndependentRivalsRemain()
    {
        var defeated = new HashSet<string> { "England", "France" };
        Assert.IsTrue(Evaluate("Rome", defeated, new HashSet<string>(), out int resolved, out int total));
        Assert.AreEqual(2, resolved);
        Assert.AreEqual(2, total);
    }

    [Test]
    public void Domination_VassalizedRival_CountsAsResolved()
    {
        var defeated = new HashSet<string> { "England" };
        var subjects = new HashSet<string> { "France" };
        Assert.IsTrue(Evaluate("Rome", defeated, subjects, out _, out _));
    }

    [Test]
    public void Domination_IndependentRivalRemains_NoVictory()
    {
        var defeated = new HashSet<string> { "England" };
        Assert.IsFalse(Evaluate("Rome", defeated, new HashSet<string>(), out int resolved, out int total));
        Assert.AreEqual(1, resolved);
        Assert.AreEqual(2, total);
    }

    [Test]
    public void Domination_RivalRegainsIndependence_BlocksAgain()
    {
        var subjects = new HashSet<string> { "England", "France" };
        Assert.IsTrue(Evaluate("Rome", new HashSet<string>(), subjects, out _, out _));
        subjects.Remove("France");
        Assert.IsFalse(Evaluate("Rome", new HashSet<string>(), subjects, out _, out _));
    }

    [Test]
    public void Domination_CandidateThatIsSubordinatedOrDefeated_CannotWin()
    {
        var defeated = new HashSet<string> { "England", "France" };
        Assert.IsFalse(Evaluate("Rome", defeated, new HashSet<string>(), out _, out _, candidateSubordinated: true));
    }

    [Test]
    public void Domination_SoloRoster_NeverWins()
    {
        bool result = VictoryRules.EvaluateDomination(new[] { "Rome" }, "Rome", false, _ => true, _ => true,
            out int resolved, out int total);
        Assert.IsFalse(result);
        Assert.AreEqual(0, total);
    }

    [Test]
    public void Domination_CandidateOutsideRoster_CannotWin()
    {
        Assert.IsFalse(VictoryRules.EvaluateDomination(Roster, "Rebels 1", false, _ => true, _ => true, out _, out _));
    }

    [Test]
    public void Domination_DuplicateRosterEntriesCountOnce()
    {
        var roster = new[] { "Rome", "England", "England" };
        VictoryRules.EvaluateDomination(roster, "Rome", false, _ => false, _ => false, out _, out int total);
        Assert.AreEqual(1, total);
    }

    // ── Persistence model ─────────────────────────────────────────────────────

    [Test]
    public void MatchState_RoundTripsThroughJsonUtility()
    {
        var state = new VictoryMatchState
        {
            majorCivNames = new List<string> { "Rome", "England", "France" },
            eliminatedCivNames = new List<string> { "England" },
            concluded = true,
            winnerCivName = "Rome",
            winnerLeaderName = "Julius Caesar",
            victoryType = (int)VictoryType.Domination,
            victoryTurn = 212,
            reason = "Rome has conquered or vassalized every rival civilization.",
            playerWon = true,
        };

        var restored = JsonUtility.FromJson<VictoryMatchState>(JsonUtility.ToJson(state));

        CollectionAssert.AreEqual(state.majorCivNames, restored.majorCivNames);
        CollectionAssert.AreEqual(state.eliminatedCivNames, restored.eliminatedCivNames);
        Assert.IsTrue(restored.concluded);
        Assert.AreEqual("Rome", restored.winnerCivName);
        Assert.AreEqual((int)VictoryType.Domination, restored.victoryType);
        Assert.AreEqual(212, restored.victoryTurn);
        Assert.IsTrue(restored.playerWon);
    }

    [Test]
    public void VictoryResult_DisplayNamesMatchSupportedConditions()
    {
        Assert.AreEqual("Domination Victory", new VictoryResult { Type = VictoryType.Domination }.TypeDisplayName);
        Assert.AreEqual("Science Victory", new VictoryResult { Type = VictoryType.Science }.TypeDisplayName);
        Assert.AreEqual("Culture Victory", new VictoryResult { Type = VictoryType.Culture }.TypeDisplayName);
        Assert.AreEqual("Match Concluded", new VictoryResult { Type = null }.TypeDisplayName);
    }
}
