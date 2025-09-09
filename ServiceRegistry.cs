using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight service registry for runtime singletons.
/// Use as a simple DI substitute to avoid repeated Find calls.
/// </summary>
public static class ServiceRegistry
{
    private static readonly Dictionary<Type, object> _map = new();

    public static void Register<T>(T instance) where T : class
    {
        var t = typeof(T);
        _map[t] = instance;
    }

    public static void Unregister<T>() where T : class
    {
        var t = typeof(T);
        if (_map.ContainsKey(t)) _map.Remove(t);
    }

    public static T Get<T>() where T : class
    {
        var t = typeof(T);
        if (_map.TryGetValue(t, out var inst)) return inst as T;
        return null;
    }
}
