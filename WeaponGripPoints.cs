using UnityEngine;

// Attach this to weapon prefabs to define hand grip points for Animation Rigging
public class WeaponGripPoints : MonoBehaviour
{
    [Tooltip("Where the right hand should grip the weapon")] 
    public Transform rightHandGrip;
    [Tooltip("Where the left hand should grip the weapon")] 
    public Transform leftHandGrip;
}
