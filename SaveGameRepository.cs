using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static class SaveGameRepository
{
    [Serializable]
    internal sealed class SaveMetadata
    {
        public string saveName;
        public string dateTime;
        public int currentTurn;
        public string playerCivName;
        public GameManager.MapSize mapSize;
        public int currentPlanetIndex;
        public bool gameInProgress;
        public int saveFormatVersion;
        public SetupMetadata generationSettings;
        public List<PlanetSeedMetadata> planetSeeds;
        public WorldMetadata worldSnapshot;
    }

    [Serializable]
    private sealed class SaveEnvelopeMetadata
    {
        public bool hasData;
        public SaveMetadata saveData;
    }

    [Serializable]
    internal sealed class SetupMetadata
    {
        public string selectedCivilizationName;
    }

    [Serializable]
    internal sealed class PlanetSeedMetadata
    {
        public int planetIndex;
    }

    [Serializable]
    internal sealed class WorldMetadata
    {
        public string marker;
    }

    public sealed class SaveEntry
    {
        public string Id { get; internal set; }
        public int SlotIndex { get; internal set; }
        public bool IsAutosave { get; internal set; }
        public bool IsEmpty { get; internal set; }
        public bool IsValid { get; internal set; }
        public bool CanLoad { get; internal set; }
        public string FileName { get; internal set; }
        public string Error { get; internal set; }
        public PauseMenuManager.GameSaveData Data { get; internal set; }
        internal SaveMetadata Metadata { get; set; }
        internal string FilePath { get; set; }

        public string SaveName => !string.IsNullOrWhiteSpace(Metadata?.saveName)
            ? Metadata.saveName
            : IsAutosave ? FileName : $"Save Slot {SlotIndex + 1}";
        public string Civilization => !string.IsNullOrWhiteSpace(Metadata?.playerCivName)
            ? Metadata.playerCivName
            : "Unknown civilization";
        public int Turn => Metadata != null ? Metadata.currentTurn : 0;
        public string Date => !string.IsNullOrWhiteSpace(Metadata?.dateTime)
            ? Metadata.dateTime
            : "Date unavailable";
        public string MapSize => Metadata != null ? Metadata.mapSize.ToString() : "Unavailable";
        public string Planet => Metadata != null ? Metadata.currentPlanetIndex.ToString(CultureInfo.InvariantCulture) : "Unavailable";
    }

    private const int DefaultManualCapacity = 50;
    private const string SavesFolderName = "Saves";

    public static int GetManualSlotCapacity(int configuredCapacity = 0)
    {
        if (configuredCapacity > 0)
            return configuredCapacity;
        return PauseMenuManager.Instance != null && PauseMenuManager.Instance.maxSaveSlots > 0
            ? PauseMenuManager.Instance.maxSaveSlots
            : DefaultManualCapacity;
    }

    public static List<SaveEntry> Discover(int manualCapacity)
    {
        var entries = new List<SaveEntry>();
        int capacity = GetManualSlotCapacity(manualCapacity);

        for (int slot = 0; slot < capacity; slot++)
            entries.Add(ReadManualSlot(slot));

        string autosaveDirectory = GetAutosaveDirectory();
        if (Directory.Exists(autosaveDirectory))
        {
            foreach (string path in Directory.GetFiles(autosaveDirectory, "autosave_*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                entries.Add(ReadFile(path, -1, true, Path.GetFileName(path)));
            }
        }

        return entries;
    }

    public static SaveEntry ReadManualSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= GetManualSlotCapacity())
            throw new ArgumentOutOfRangeException(nameof(slotIndex), "Save slot is outside the configured capacity.");

        string path = GetManualSlotPath(slotIndex);
        if (!File.Exists(path))
        {
            return new SaveEntry
            {
                Id = $"manual:{slotIndex}",
                SlotIndex = slotIndex,
                IsEmpty = true,
                IsValid = true
            };
        }

        return ReadFile(path, slotIndex, false, Path.GetFileName(path));
    }

    public static SaveEntry ReadAutosave(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
            || !fileName.StartsWith("autosave_", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Autosave name is invalid.", nameof(fileName));

        return ReadFile(Path.Combine(GetAutosaveDirectory(), fileName), -1, true, fileName);
    }

    public static void WriteManualSlot(int slotIndex, PauseMenuManager.GameSaveData data)
    {
        if (slotIndex < 0 || slotIndex >= GetManualSlotCapacity())
            throw new ArgumentOutOfRangeException(nameof(slotIndex), "Save slot is outside the configured capacity.");
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(GetSaveDirectory());
        var wrapper = new PauseMenuManager.SaveSlotData(slotIndex)
        {
            hasData = true,
            saveData = data
        };
        WriteJsonAtomically(GetManualSlotPath(slotIndex), JsonUtility.ToJson(wrapper, true));
    }

    public static void WriteAutosave(string fileName, PauseMenuManager.GameSaveData data)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
            || !fileName.StartsWith("autosave_", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Autosave name is invalid.", nameof(fileName));
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        string directory = GetAutosaveDirectory();
        Directory.CreateDirectory(directory);
        var wrapper = new PauseMenuManager.SaveSlotData(-1)
        {
            hasData = true,
            saveData = data
        };
        WriteJsonAtomically(Path.Combine(directory, fileName), JsonUtility.ToJson(wrapper, true));
    }

    public static void CleanupAutosaves(int maximumCount)
    {
        string directory = GetAutosaveDirectory();
        if (!Directory.Exists(directory))
            return;

        var files = Directory.GetFiles(directory, "autosave_*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        for (int i = Mathf.Max(0, maximumCount); i < files.Length; i++)
        {
            string fileName = Path.GetFileName(files[i]);
            if (Path.GetFileName(files[i]) != fileName)
                throw new InvalidOperationException("Autosave cleanup encountered an unsafe path.");
            File.Delete(Path.Combine(directory, fileName));
        }
    }

    public static void WriteDirectSave(string saveName, PauseMenuManager.GameSaveData data)
    {
        string safeName = ValidateDirectSaveName(saveName);
        Directory.CreateDirectory(GetSaveDirectory());
        WriteJsonAtomically(Path.Combine(GetSaveDirectory(), safeName + ".json"), JsonUtility.ToJson(data, true));
    }

    public static void Delete(SaveEntry entry)
    {
        if (entry == null || entry.IsEmpty)
            throw new ArgumentException("An occupied save entry must be selected.", nameof(entry));

        string path = entry.IsAutosave
            ? GetAutosavePath(entry.FileName)
            : GetManualSlotPath(entry.SlotIndex);
        if (!File.Exists(path))
            throw new FileNotFoundException("The selected save no longer exists.", path);
        File.Delete(path);
    }

    public static bool TryReadDirectSave(string saveName, out PauseMenuManager.GameSaveData data)
    {
        string safeName = ValidateDirectSaveName(saveName);
        string path = Path.Combine(GetSaveDirectory(), safeName + ".json");
        data = null;
        if (!File.Exists(path))
            return false;

        data = ReadFullData(path);
        return data != null;
    }

    public static PauseMenuManager.GameSaveData Load(SaveEntry entry)
    {
        if (entry == null || entry.IsEmpty || !entry.IsValid || string.IsNullOrEmpty(entry.FilePath))
            throw new ArgumentException("A valid save entry must be selected.", nameof(entry));

        entry.Data = ReadFullData(entry.FilePath);
        entry.CanLoad = IsRestorable(entry.Data, out string reason);
        entry.Error = reason;
        return entry.Data;
    }

    public static bool IsRestorable(PauseMenuManager.GameSaveData data, out string reason)
    {
        if (data == null)
        {
            reason = "The save contains no game data.";
            return false;
        }
        if (data.saveFormatVersion < 2 || data.generationSettings == null)
        {
            reason = "This save predates procedural-world restoration data and cannot be safely restored.";
            return false;
        }
        if (data.worldSnapshot == null || !data.worldSnapshot.HasState())
        {
            reason = "The save does not contain a complete campaign state.";
            return false;
        }
        if (data.planetSeeds == null || data.planetSeeds.Count == 0
            || !data.planetSeeds.Any(seed => seed != null && seed.planetIndex == data.currentPlanetIndex))
        {
            reason = "The save does not contain the procedural world seeds needed for restoration.";
            return false;
        }
        if (!data.gameInProgress)
        {
            reason = "The save does not contain an active campaign.";
            return false;
        }

        reason = null;
        return true;
    }

    private static SaveEntry ReadFile(string path, int slotIndex, bool autosave, string fileName)
    {
        var entry = new SaveEntry
        {
            Id = autosave ? $"autosave:{fileName}" : $"manual:{slotIndex}",
            SlotIndex = slotIndex,
            IsAutosave = autosave,
            FileName = fileName,
            FilePath = path
        };

        try
        {
            if (!File.Exists(path))
            {
                entry.Error = "Save file is missing.";
                return entry;
            }

            string json = File.ReadAllText(path);
            SaveMetadata metadata = null;
            var envelope = JsonUtility.FromJson<SaveEnvelopeMetadata>(json);
            if (envelope != null && envelope.hasData)
                metadata = envelope.saveData;
            if (metadata == null)
                metadata = JsonUtility.FromJson<SaveMetadata>(json);
            if (metadata == null || (string.IsNullOrEmpty(metadata.saveName)
                && string.IsNullOrEmpty(metadata.dateTime) && metadata.saveFormatVersion == 0))
            {
                entry.Error = "Save file is empty or invalid.";
                return entry;
            }

            entry.Metadata = metadata;
            entry.IsValid = true;
            entry.CanLoad = IsMetadataRestorable(metadata, out string reason);
            entry.Error = reason;
        }
        catch (Exception ex)
        {
            entry.Error = $"Could not read save: {ex.Message}";
            Debug.LogWarning($"[SaveGameRepository] {entry.Error} ({fileName})");
        }

        return entry;
    }

    private static PauseMenuManager.GameSaveData ReadFullData(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The selected save no longer exists.", path);

        string json = File.ReadAllText(path);
        var wrapper = JsonUtility.FromJson<PauseMenuManager.SaveSlotData>(json);
        if (wrapper != null && wrapper.hasData && wrapper.saveData != null)
            return wrapper.saveData;
        return JsonUtility.FromJson<PauseMenuManager.GameSaveData>(json);
    }

    private static bool IsMetadataRestorable(SaveMetadata data, out string reason)
    {
        if (data.saveFormatVersion < 2 || data.generationSettings == null)
            reason = "This save predates procedural-world restoration data and cannot be safely restored.";
        else if (data.worldSnapshot == null || data.planetSeeds == null || data.planetSeeds.Count == 0
                 || !data.planetSeeds.Any(seed => seed != null && seed.planetIndex == data.currentPlanetIndex))
            reason = "The save does not contain the procedural world and campaign state needed for restoration.";
        else if (!data.gameInProgress)
            reason = "The save does not contain an active campaign.";
        else
            reason = null;
        return reason == null;
    }

    private static string GetSaveDirectory() => Path.Combine(Application.persistentDataPath, SavesFolderName);
    private static string GetAutosaveDirectory() => Path.Combine(GetSaveDirectory(), "Autosaves");
    private static string GetManualSlotPath(int slotIndex) =>
        Path.Combine(GetSaveDirectory(), $"save_slot_{slotIndex}.json");

    private static string GetAutosavePath(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Autosave path is invalid.", nameof(fileName));
        return Path.Combine(GetAutosaveDirectory(), fileName);
    }

    private static string ValidateDirectSaveName(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName) || saveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || saveName.Contains("/") || saveName.Contains("\\") || saveName == "." || saveName == "..")
            throw new ArgumentException("Save name must be a plain file name.", nameof(saveName));
        return saveName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? saveName.Substring(0, saveName.Length - 5)
            : saveName;
    }

    private static void WriteJsonAtomically(string path, string json)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
