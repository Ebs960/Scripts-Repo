using UnityEngine;

public sealed class BattleInputController : MonoBehaviour
{
    public bool IsActive { get; private set; }

    public void SetActive(bool active)
    {
        IsActive = active;
    }
}
