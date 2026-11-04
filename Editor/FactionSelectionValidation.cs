#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class FactionSelectionValidation
{
    private static int assertions;

    [MenuItem("Tools/Main Menu/Validate Faction Selection")]
    public static void Run()
    {
        assertions = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        var temporaryData = new List<Object>();
        var cacheType = typeof(ResourceCache);
        var cacheField = cacheType.GetField("_allCivDatas", BindingFlags.Static | BindingFlags.NonPublic);
        var loadedField = cacheType.GetField("_civDatasLoaded", BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = cacheType.GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic);
        var previousCatalog = cacheField.GetValue(null);
        var previouslyLoaded = loadedField.GetValue(null);
        var previouslyInitialized = initializedField.GetValue(null);
        try
        {
            // Edit-mode fixtures must not start the game's persistent addressable loader.
            initializedField.SetValue(null, true);
            var canvasObject = new GameObject("FactionValidation", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)canvasObject.transform;
            canvas.sizeDelta = new Vector2(1920f, 1080f);
            var manager = canvasObject.AddComponent<MainMenuManager>();
            manager.mainMenuPanel = Rect("MainMenu", canvas).gameObject;
            manager.civSelectionPanel = Rect("FactionSelection", canvas).gameObject;
            manager.leaderSelectionPanel = Rect("LeaderSelection", canvas).gameObject;
            manager.leaderButtonContainer = Rect("Leaders", manager.leaderSelectionPanel.transform);
            manager.selectedCivIcon = Rect("Emblem", manager.civSelectionPanel.transform).gameObject.AddComponent<Image>();
            manager.selectedCivName = Text("Name", manager.civSelectionPanel.transform);
            manager.selectedCivDescription = Text("Description", manager.civSelectionPanel.transform);
            manager.selectedCivBonuses = Text("Bonuses", manager.civSelectionPanel.transform);
            manager.selectCivButton = Button("Next", manager.civSelectionPanel.transform);
            manager.backFromCivButton = Button("Back", manager.civSelectionPanel.transform);
            manager.leaderButtonPrefab = Button("LeaderTemplate", manager.leaderSelectionPanel.transform);
            manager.leaderButtonPrefab.gameObject.SetActive(false);
            manager.leaderSelectionBackgroundImage = manager.leaderSelectionPanel.AddComponent<Image>();

            var firstArtwork = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var secondArtwork = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Check(firstArtwork != null && secondArtwork != null && firstArtwork != secondArtwork, "Built-in test sprites exist");
            manager.placeholderCivIcon = firstArtwork;
            manager.leaderSelectionBackgroundImage.sprite = secondArtwork;
            var leader = ScriptableObject.CreateInstance<LeaderData>();
            temporaryData.Add(leader);
            leader.leaderName = "Validation leader";
            var catalog = new List<CivData>();
            for (int i = 0; i < 21; i++)
            {
                var civ = ScriptableObject.CreateInstance<CivData>();
                temporaryData.Add(civ);
                civ.civName = "Faction " + i;
                civ.icon = firstArtwork;
                civ.description = "Original description " + i;
                civ.foodModifier = .1f;
                civ.availableLeaders = new List<LeaderData> { leader };
                catalog.Add(civ);
            }
            catalog[0].factionSelectionArtwork = secondArtwork;
            catalog[3].factionSelectionArtwork = firstArtwork;
            var tribe = ScriptableObject.CreateInstance<CivData>();
            var cityState = ScriptableObject.CreateInstance<CivData>();
            temporaryData.Add(tribe);
            temporaryData.Add(cityState);
            tribe.isTribe = true;
            cityState.isCityState = true;
            var configured = new List<CivData>(catalog) { catalog[0], null, tribe, cityState };
            cacheField.SetValue(null, configured.ToArray());
            loadedField.SetValue(null, true);
            Invoke(manager, "PopulateCivilizationList");
            CheckRoster(manager, catalog);
            Check(!manager.selectCivButton.interactable && manager.selectedCivArtwork.sprite == null, "Entry resets selection and artwork");
            int externalClicks = 0;
            manager.civButtonContainer.GetComponentInChildren<Button>().onClick.AddListener(() => externalClicks++);

            Click(manager, catalog[0]);
            Check(externalClicks == 1, "Existing button listener is preserved");
            Check(manager.selectedCivArtwork.sprite == secondArtwork, "Civilization artwork is displayed");
            Check(manager.selectedCivIcon.sprite == catalog[0].icon, "Original emblem remains separate");
            Check(manager.selectedCivDescription.text == catalog[0].description, "Description is preserved");
            Check(manager.selectedCivBonuses.text.Contains($"+{catalog[0].foodModifier:P0} Food"), "Original bonuses display");
            Check(manager.selectCivButton.interactable, "Next is enabled on selection");
            Check(manager.selectedCivArtwork != manager.selectedCivIcon, "Hero and emblem are distinct Images");

            Click(manager, catalog[1]);
            Check(manager.selectedCivArtwork.sprite == null && !manager.selectedCivArtwork.enabled, "Unillustrated civilization displays the placeholder");
            Click(manager, catalog[2]);
            Check(manager.selectedCivArtwork.sprite == null && !manager.selectedCivArtwork.enabled, "Missing art clears previous illustration");
            manager.fallbackCivSelectionArtwork = firstArtwork;
            Click(manager, catalog[2]);
            Check(manager.selectedCivArtwork.sprite == firstArtwork, "Shared fallback is displayed");
            Click(manager, catalog[1]);
            Check(manager.selectedCivArtwork.sprite == firstArtwork, "Another unillustrated civilization uses the shared fallback");
            Click(manager, catalog[0]);
            Check(manager.selectedCivArtwork.sprite == secondArtwork, "Civilization artwork takes priority over the shared fallback");
            Click(manager, catalog[3]);
            Check(manager.selectedCivArtwork.sprite == firstArtwork, "Another illustrated civilization can be selected");

            foreach (var size in new[] {
                new Vector2(1920, 1080), new Vector2(2560, 1440),
                new Vector2(3440, 1440), new Vector2(1024, 768),
                new Vector2(800, 1280)
            })
            {
                canvas.sizeDelta = size;
                var presentation = manager.civSelectionPanel.GetComponent<FactionSelectionPresentation>();
                Canvas.ForceUpdateCanvases();
                Invoke(presentation, "LateUpdate");
                Canvas.ForceUpdateCanvases();
                var image = manager.selectedCivArtwork;
                var viewport = (RectTransform)image.transform.parent;
                Check(Mathf.Abs(viewport.rect.width / viewport.rect.height - 16f / 9f) < .001f,
                    $"16:9 viewport at {size}: viewport={viewport.rect.size}, slot={((RectTransform)viewport.parent).rect.size}, active={viewport.gameObject.activeInHierarchy}");
                Check(Mathf.Abs(image.rectTransform.rect.width / image.rectTransform.rect.height - image.sprite.rect.width / image.sprite.rect.height) < .001f, "Undistorted artwork at " + size);
                Check(image.rectTransform.rect.width + .1f >= viewport.rect.width && image.rectTransform.rect.height + .1f >= viewport.rect.height, "Cover cropping at " + size);
                Check(viewport.GetComponent<RectMask2D>() != null, "Artwork is masked");
                Check(((RectTransform)manager.civButtonContainer.parent).rect.height > 100f, "Usable scrollable roster at " + size);
                var details = manager.selectedCivDescription.GetComponentInParent<ScrollRect>();
                Check(details.viewport.rect.height > 100f, "Usable details at " + size);
                Check(manager.selectCivButton.gameObject.activeInHierarchy && manager.backFromCivButton.gameObject.activeInHierarchy, "Navigation remains visible at " + size);
            }

            int generatedCount = manager.civButtonContainer.childCount;
            Invoke(manager, "PopulateCivilizationList");
            Invoke(manager, "PopulateCivilizationList");
            CheckRoster(manager, catalog);
            Check(manager.civButtonContainer.childCount == generatedCount, "Reentry reuses generated buttons");
            Check(manager.selectedCivArtwork.sprite == firstArtwork && !manager.selectCivButton.interactable, "Reentry resets to fallback");
            Click(manager, catalog[0]);
            Check(externalClicks == 3, "Reentry does not duplicate external callbacks");
            Invoke(manager, "OnCivSelected");
            Check(!manager.civSelectionPanel.activeSelf && manager.leaderSelectionPanel.activeSelf, "Next transitions to Leader Selection");
            Check(manager.leaderSelectionBackgroundImage.sprite == secondArtwork, "Leader artwork system is untouched");
            Invoke(manager, "OnBackFromLeaderSelectionClicked");
            Check(manager.civSelectionPanel.activeSelf && !manager.leaderSelectionPanel.activeSelf, "Leader Back returns to factions");
            Check(manager.selectedCivArtwork.sprite == secondArtwork && manager.selectedCivName.text == catalog[0].civName, "Leader Back preserves selection and refreshes illustration");
            Invoke(manager, "OnBackFromCivSelectionClicked");
            Check(manager.mainMenuPanel.activeSelf && !manager.civSelectionPanel.activeSelf, "Faction Back returns to main menu");
            Debug.Log($"[Faction Selection] PASS: {assertions} assertions; 21 playable factions; five viewport sizes.");
        }
        finally
        {
            cacheField.SetValue(null, previousCatalog);
            loadedField.SetValue(null, previouslyLoaded);
            initializedField.SetValue(null, previouslyInitialized);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in temporaryData) Object.DestroyImmediate(item);
        }
    }

    [MenuItem("Tools/Main Menu/Validate Civilization Catalog")]
    public static void RunCatalog()
    {
        assertions = 0;
        const string root = "Assets/Scripts Repo/";
        var database = AssetDatabase.LoadAssetAtPath<BaseGameContentDatabase>(root + "Content Database/BaseGameContentDatabase.asset");
        Check(database != null, "Base-game catalog exists");
        var playable = database.civilizations.Where(civ => civ != null && !civ.isTribe && !civ.isCityState).ToList();
        Check(playable.Count == 21 && playable.Distinct().Count() == 21, "Catalog contains 21 distinct playable civilizations");
        var names = new[] { "America", "Incan Empire", "India", "Greece", "Spain" };
        var leaderNames = new[] {
            new[] { "George Washington", "Theodore Roosevelt" },
            new[] { "Pachacuti", "Huayna Capac" },
            new[] { "Ashoka", "Chandragupta Maurya" },
            new[] { "Pericles", "Alexander the Great" },
            new[] { "Isabella I", "Philip II" }
        };
        var expectedBonuses = new[] {
            new[] { 0f, .1f, 0f, 0f, .05f, 0f },
            new[] { .1f, .05f, 0f, 0f, 0f, 0f },
            new[] { .05f, 0f, 0f, 0f, 0f, .1f },
            new[] { 0f, 0f, .05f, .05f, .05f, 0f },
            new[] { 0f, .05f, .05f, 0f, 0f, .05f }
        };
        var scene = EditorSceneManager.NewPreviewScene();
        var cacheType = typeof(ResourceCache);
        var cacheField = cacheType.GetField("_allCivDatas", BindingFlags.Static | BindingFlags.NonPublic);
        var loadedField = cacheType.GetField("_civDatasLoaded", BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = cacheType.GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic);
        var previousCatalog = cacheField.GetValue(null);
        var previouslyLoaded = loadedField.GetValue(null);
        var previouslyInitialized = initializedField.GetValue(null);
        var randomState = UnityEngine.Random.state;
        try
        {
            initializedField.SetValue(null, true);
            ResourceCache.SetCivDatas(database.civilizations);
            for (int i = 0; i < names.Length; i++)
            {
                var civ = playable.SingleOrDefault(value => value.civName == names[i]);
                Check(civ != null, names[i] + " is registered");
                Check(civ.availableLeaders != null && civ.availableLeaders.Count == 2, names[i] + " has exactly two leaders");
                Check(civ.availableLeaders.Select(leader => leader.leaderName).SequenceEqual(leaderNames[i]), names[i] + " has the confirmed leader pair");
                Check(!string.IsNullOrWhiteSpace(civ.description) && civ.cityNames.Count >= 12 && civ.herdNames.Count >= 4, names[i] + " has complete identity data");
                Check(civ.cityNames.Distinct().Count() == civ.cityNames.Count, names[i] + " has distinct city names");
                Check(civ.icon == null && civ.factionSelectionArtwork == null, names[i] + " leaves artwork empty");
                Check(Enum.IsDefined(typeof(CultureGroup), civ.cultureGroup), names[i] + " has a defined culture group");
                var actualBonuses = new[] { civ.foodModifier, civ.productionModifier, civ.goldModifier, civ.scienceModifier, civ.cultureModifier, civ.faithModifier };
                Check(actualBonuses.SequenceEqual(expectedBonuses[i]), names[i] + " has the documented base bonuses");

                var canvasObject = new GameObject(names[i] + "Validation", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var canvas = (RectTransform)canvasObject.transform;
                canvas.sizeDelta = new Vector2(1920f, 1080f);
                var menu = canvasObject.AddComponent<MainMenuManager>();
                menu.mainMenuPanel = Rect("MainMenu", canvas).gameObject;
                menu.civSelectionPanel = Rect("Factions", canvas).gameObject;
                menu.leaderSelectionPanel = Rect("Leaders", canvas).gameObject;
                menu.gameSetupPanel = Rect("GameSetup", canvas).gameObject;
                menu.gameSetupPanel.SetActive(false);
                menu.selectedCivIcon = Rect("Emblem", menu.civSelectionPanel.transform).gameObject.AddComponent<Image>();
                menu.selectedCivName = Text("Name", menu.civSelectionPanel.transform);
                menu.selectedCivDescription = Text("Description", menu.civSelectionPanel.transform);
                menu.selectedCivBonuses = Text("Bonuses", menu.civSelectionPanel.transform);
                menu.selectCivButton = Button("Next", menu.civSelectionPanel.transform);
                menu.backFromCivButton = Button("Back", menu.civSelectionPanel.transform);
                menu.leaderButtonContainer = Rect("LeaderButtons", menu.leaderSelectionPanel.transform);
                menu.leaderButtonPrefab = Button("LeaderTemplate", canvas);
                menu.selectedLeaderName = Text("LeaderName", menu.leaderSelectionPanel.transform);
                menu.selectedLeaderDescription = Text("LeaderDescription", menu.leaderSelectionPanel.transform);
                menu.selectedLeaderBonuses = Text("LeaderBonuses", menu.leaderSelectionPanel.transform);
                menu.selectLeaderButton = Button("LeaderNext", menu.leaderSelectionPanel.transform);
                Invoke(menu, "PopulateCivilizationList");
                CheckRoster(menu, playable);
                Click(menu, civ);
                Check(menu.selectedCivName.text == civ.civName && menu.selectedCivDescription.text == civ.description, "Selected civilization identity displays");
                Check(menu.selectedCivBonuses.text != "No civilization bonuses.", "Civilization bonuses display");
                Check(!menu.selectedCivArtwork.enabled, "New civilization uses the artwork placeholder");
                Invoke(menu, "OnCivSelected");
                var buttons = menu.leaderButtonContainer.GetComponentsInChildren<Button>();
                Check(buttons.Length == 2, "Both leader buttons are created");
                foreach (var leader in civ.availableLeaders)
                {
                    Check(database.leaders.Count(value => value == leader) == 1, leader.leaderName + " is registered once");
                    Check(!string.IsNullOrWhiteSpace(leader.biography) && !string.IsNullOrWhiteSpace(leader.historicalEra), "Leader historical identity exists");
                    Check(!string.IsNullOrWhiteSpace(leader.abilityName) && !string.IsNullOrWhiteSpace(leader.abilityDescription), "Leader ability is described");
                    Check(leader.portrait == null && leader.background == null && leader.leaderAnimator == null, "Leader artwork remains unassigned");
                    var bonuses = new[] { leader.foodModifier, leader.productionModifier, leader.goldModifier, leader.scienceModifier, leader.cultureModifier, leader.faithModifier, leader.militaryStrengthModifier };
                    Check(bonuses.All(value => value >= 0f && value <= .15f) && Mathf.Abs(bonuses.Sum() - .2f) < .0001f, "Leader bonuses match the 20% initial budget");
                    Check(new[] { leader.aggressiveness, leader.diplomacy, leader.science, leader.expansion, leader.religion }.All(value => value >= 0 && value <= 10), "AI ratings are valid");
                    Check(Enum.IsDefined(typeof(LeaderAgenda), leader.primaryAgenda) && Enum.IsDefined(typeof(LeaderAgenda), leader.secondaryAgenda) &&
                        Enum.IsDefined(typeof(VictoryType), leader.preferredVictory), "AI agendas and victory preference are valid");
                    Check(leader.likesTraits.Length > 0 && leader.dislikesTraits.Length > 0 &&
                        leader.likesTraits.Concat(leader.dislikesTraits).All(value => Enum.IsDefined(typeof(CivilizationTrait), value)), "Diplomatic traits deserialize correctly");
                    Check(Enum.GetValues(typeof(FocusArea)).Cast<FocusArea>().All(area => leader.GetFocusPriority(area) > 0f && leader.GetFocusPriority(area) <= 2f), "AI focus weights are valid");
                    buttons.Single(button => button.GetComponentInChildren<TextMeshProUGUI>().text == leader.leaderName).onClick.Invoke();
                    Check(menu.selectedLeaderName.text == leader.leaderName && menu.selectedLeaderDescription.text.Contains(leader.abilityName), "Leader identity and ability display immediately");
                    Check(menu.selectedLeaderBonuses.text != "No leader bonuses." && menu.selectLeaderButton.interactable, "Leader bonuses display and Next enables");
                    var simulation = Rect("BonusSimulation", canvas).gameObject;
                    simulation.SetActive(false);
                    var civilization = simulation.AddComponent<Civilization>();
                    civilization.Initialize(civ, leader, false);
                    Check(new[] { civilization.foodModifier, civilization.productionModifier, civilization.goldModifier, civilization.scienceModifier,
                        civilization.cultureModifier, civilization.faithModifier, civilization.attackBonus }.SequenceEqual(bonuses), "Production leader bonus application matches the asset");
                }
                Invoke(menu, "OnLeaderSelected");
                Check(menu.gameSetupPanel.activeSelf && !menu.leaderSelectionPanel.activeSelf, "Leader selection advances to Game Setup");
            }
            var nameLibrary = AssetDatabase.LoadAssetAtPath<GovernorCandidateNameLibrary>(root + "Governor Candidate Names.asset");
            Check(nameLibrary != null && nameLibrary.GetRandomName(CultureGroup.Andean) != "Unnamed Candidate", "Andean governors have a name pool");
            Debug.Log($"[Civilization Catalog] PASS: {assertions} assertions; 21 playable civilizations; all ten new leaders selectable.");
        }
        finally
        {
            cacheField.SetValue(null, previousCatalog);
            loadedField.SetValue(null, previouslyLoaded);
            initializedField.SetValue(null, previouslyInitialized);
            UnityEngine.Random.state = randomState;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckRoster(MainMenuManager manager, List<CivData> expected)
    {
        var found = new HashSet<string>();
        foreach (var button in manager.civButtonContainer.GetComponentsInChildren<Button>())
            Check(found.Add(button.GetComponentInChildren<TextMeshProUGUI>().text), "No duplicate playable buttons");
        Check(found.Count == expected.Count, $"All {expected.Count} playable civilizations appear");
        foreach (var civ in expected) Check(found.Contains(civ.civName), civ.civName + " is selectable");
    }

    private static void Click(MainMenuManager manager, CivData civ)
    {
        foreach (var button in manager.civButtonContainer.GetComponentsInChildren<Button>())
        {
            if (button.GetComponentInChildren<TextMeshProUGUI>().text != civ.civName) continue;
            button.onClick.Invoke();
            return;
        }
        throw new InvalidOperationException("Missing civilization button: " + civ.civName);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("[Faction Selection] " + message);
    }

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static TextMeshProUGUI Text(string name, Transform parent)
    {
        return Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
    }

    private static Button Button(string name, Transform parent)
    {
        var rect = Rect(name, parent);
        rect.gameObject.AddComponent<Image>();
        var button = rect.gameObject.AddComponent<Button>();
        Text("Label", rect).text = name;
        return button;
    }
}
#endif
