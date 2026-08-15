using UnityEngine;

/// <summary>Scene bootstrap that supplies the serialized library without Resources.Load paths.</summary>
public class GovernorPortraitServiceHost : MonoBehaviour
{
    [SerializeField] private GovernorPortraitLibrary portraitLibrary;
    private void Awake() { GovernorPortraitService.Configure(portraitLibrary); }
}
