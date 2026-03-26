using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface ISaveGameParticipant
{
    string SaveKey { get; }
    string CaptureStateJson();
    void RestoreStateJson(string json);
}

public static class SaveGameRegistry
{
    private static readonly List<ISaveGameParticipant> participants = new List<ISaveGameParticipant>();

    public static void Register(ISaveGameParticipant participant)
    {
        if (participant == null || string.IsNullOrWhiteSpace(participant.SaveKey) || participants.Contains(participant))
            return;

        participants.Add(participant);
    }

    public static void Unregister(ISaveGameParticipant participant)
    {
        if (participant == null)
            return;

        participants.Remove(participant);
    }

    public static List<PauseMenuManager.SaveParticipantStateData> CaptureAll()
    {
        var states = new List<PauseMenuManager.SaveParticipantStateData>();

        foreach (var participant in participants.OrderBy(p => p.SaveKey, StringComparer.OrdinalIgnoreCase))
        {
            if (participant == null || string.IsNullOrWhiteSpace(participant.SaveKey))
                continue;

            try
            {
                states.Add(new PauseMenuManager.SaveParticipantStateData
                {
                    key = participant.SaveKey,
                    json = participant.CaptureStateJson()
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveGameRegistry] Failed to capture state for '{participant.SaveKey}': {ex.Message}");
            }
        }

        return states;
    }

    public static void RestoreAll(List<PauseMenuManager.SaveParticipantStateData> states)
    {
        if (states == null || states.Count == 0)
            return;

        var lookup = participants
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.SaveKey))
            .GroupBy(p => p.SaveKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var state in states)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.key))
                continue;

            if (!lookup.TryGetValue(state.key, out var participant) || participant == null)
                continue;

            try
            {
                participant.RestoreStateJson(state.json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveGameRegistry] Failed to restore state for '{state.key}': {ex.Message}");
            }
        }
    }
}