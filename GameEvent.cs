using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(menuName = "Events/GameEvent")]
public class GameEvent : ScriptableObject
{
    public UnityEvent listeners = new UnityEvent();

    public void Raise()
    {
        listeners?.Invoke();
    }
}
