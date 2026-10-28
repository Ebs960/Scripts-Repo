using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>Short-lived toast line for rejections, round changes and side changes.</summary>
public sealed class BattleNotificationView : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField, Min(0.5f)] private float holdSeconds = 2.2f;
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.4f;
    private Coroutine routine;

    public void Post(string message)
    {
        if (string.IsNullOrEmpty(message) || label == null || group == null) return;
        label.text = message;
        group.alpha = 1f;
        if (!isActiveAndEnabled) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Fade());
    }

    public void Clear()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (group != null) group.alpha = 0f;
    }

    private IEnumerator Fade()
    {
        yield return new WaitForSecondsRealtime(holdSeconds);
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / fadeSeconds;
            yield return null;
        }
        group.alpha = 0f;
        routine = null;
    }
}
