#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BandCampfireParticleSetup : EditorWindow
{
    private const string ConfigureMenuPath = "Tools/Campaign/Configure Selected Band Campfire Flame";
    private const string ValidateMenuPath = "Tools/Campaign/Validate Band Campfire Flame";

    private ParticleSystem targetParticleSystem;
    private Material flameMaterial;

    [MenuItem(ConfigureMenuPath)]
    private static void OpenWindow()
    {
        if (!TryGetSelectedParticleSystem(out ParticleSystem selectedParticleSystem))
            return;

        BandCampfireParticleSetup window = GetWindow<BandCampfireParticleSetup>(true, "Band Campfire Setup");
        window.minSize = new Vector2(390f, 145f);
        window.targetParticleSystem = selectedParticleSystem;
        window.flameMaterial = selectedParticleSystem.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Band Campfire Flame", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Assign an HDRP-compatible transparent flame material, then apply the compact flame preset.",
            MessageType.Info);

        targetParticleSystem = (ParticleSystem)EditorGUILayout.ObjectField(
            "Target Particle System", targetParticleSystem, typeof(ParticleSystem), true);
        flameMaterial = (Material)EditorGUILayout.ObjectField(
            "Flame Material", flameMaterial, typeof(Material), false);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetParticleSystem == null))
        {
            if (GUILayout.Button("Apply Campfire Preset"))
                ApplyPreset();
        }
    }

    private void ApplyPreset()
    {
        if (targetParticleSystem == null)
        {
            EditorUtility.DisplayDialog("Band Campfire Setup", "Choose a target ParticleSystem first.", "OK");
            return;
        }

        if (flameMaterial == null)
        {
            EditorUtility.DisplayDialog(
                "Band Campfire Setup",
                "Assign a transparent flame Material before applying the preset.",
                "OK");
            return;
        }

        ParticleSystemRenderer particleRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
        GameObject targetObject = targetParticleSystem.gameObject;
        Undo.RecordObjects(
            new Object[] { targetObject, targetParticleSystem, particleRenderer },
            "Configure Band Campfire Flame");

        ParticleSystem.MainModule main = targetParticleSystem.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.maxParticles = 20;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.23f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-10f * Mathf.Deg2Rad, 10f * Mathf.Deg2Rad);
        main.startColor = Color.white;
        main.prewarm = true;

        ParticleSystem.EmissionModule emission = targetParticleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = 12f;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        ParticleSystem.ShapeModule shape = targetParticleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.03f;
        shape.radiusThickness = 1f;
        shape.position = Vector3.zero;
        // A Circle emits along its local Z axis; this rotation points that axis up (+Y).
        shape.rotation = new Vector3(-90f, 0f, 0f);

        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.65f),
            new Keyframe(0.20f, 1f),
            new Keyframe(0.70f, 0.75f),
            new Keyframe(1f, 0f));
        for (int i = 0; i < sizeCurve.length; i++)
            AnimationUtility.SetKeyLeftTangentMode(sizeCurve, i, AnimationUtility.TangentMode.ClampedAuto);
        for (int i = 0; i < sizeCurve.length; i++)
            AnimationUtility.SetKeyRightTangentMode(sizeCurve, i, AnimationUtility.TangentMode.ClampedAuto);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = targetParticleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        Gradient fireGradient = new Gradient();
        fireGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.70f), 0f),
                new GradientColorKey(new Color(1f, 0.62f, 0.12f), 0.35f),
                new GradientColorKey(new Color(1f, 0.24f, 0.035f), 0.75f),
                new GradientColorKey(new Color(0.72f, 0.10f, 0.015f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.35f),
                new GradientAlphaKey(0.8f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            });
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = targetParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fireGradient);

        ParticleSystem.NoiseModule noise = targetParticleSystem.noise;
        noise.enabled = true;
        noise.separateAxes = false;
        noise.strength = 0.05f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.2f;

        ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = targetParticleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = false;
        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = targetParticleSystem.rotationOverLifetime;
        rotationOverLifetime.enabled = false;
        ParticleSystem.CollisionModule collision = targetParticleSystem.collision;
        collision.enabled = false;
        ParticleSystem.TrailModule trails = targetParticleSystem.trails;
        trails.enabled = false;
        ParticleSystem.LightsModule lights = targetParticleSystem.lights;
        lights.enabled = false;

        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        particleRenderer.sortingFudge = 0.05f;
        particleRenderer.sharedMaterial = flameMaterial;

        EditorUtility.SetDirty(targetObject);
        EditorUtility.SetDirty(targetParticleSystem);
        EditorUtility.SetDirty(particleRenderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(targetParticleSystem);
        PrefabUtility.RecordPrefabInstancePropertyModifications(particleRenderer);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Band Campfire Setup] Configured '{targetObject.name}' with compact local-space flame particles " +
            "(20 max, 12/sec, warm fade, subtle noise, no collision/trails/shadows) and the selected material.",
            targetObject);
    }

    [MenuItem(ValidateMenuPath)]
    private static void ValidateSelectedFlame()
    {
        if (!TryGetSelectedParticleSystem(out ParticleSystem particleSystem))
            return;

        var warnings = new List<string>();
        ParticleSystem.MainModule main = particleSystem.main;
        ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();

        if (!main.loop)
            warnings.Add("ParticleSystem is not looping.");
        if (!main.playOnAwake)
            warnings.Add("Play On Awake is disabled.");
        if (main.maxParticles > 50)
            warnings.Add($"Max Particles is excessively high ({main.maxParticles}; expected 50 or fewer).");
        if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
            warnings.Add("Simulation Space is not Local.");
        if (particleRenderer.sharedMaterial == null)
            warnings.Add("ParticleSystemRenderer material is null.");
        if (particleRenderer.shadowCastingMode != ShadowCastingMode.Off)
            warnings.Add("ParticleSystemRenderer casts shadows.");

        foreach (ParticleSystem child in particleSystem.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (child != particleSystem && child.name.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) >= 0)
                warnings.Add($"Smoke-like child ParticleSystem found: '{child.name}'.");
        }

        if (warnings.Count == 0)
        {
            Debug.Log($"[Band Campfire Validation] '{particleSystem.name}' passed all checks.", particleSystem);
            EditorUtility.DisplayDialog("Band Campfire Validation", "The selected flame passed all checks.", "OK");
            return;
        }

        foreach (string warning in warnings)
            Debug.LogWarning($"[Band Campfire Validation] {warning}", particleSystem);

        EditorUtility.DisplayDialog(
            "Band Campfire Validation",
            $"Found {warnings.Count} warning(s). See the Console for details.",
            "OK");
    }

    private static bool TryGetSelectedParticleSystem(out ParticleSystem particleSystem)
    {
        particleSystem = null;
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length != 1)
        {
            const string message = "Select exactly one GameObject containing a ParticleSystem.";
            Debug.LogError($"[Band Campfire Setup] {message}");
            EditorUtility.DisplayDialog("Band Campfire Setup", message, "OK");
            return false;
        }

        particleSystem = selectedObjects[0].GetComponent<ParticleSystem>();
        if (particleSystem != null)
            return true;

        string missingMessage = $"'{selectedObjects[0].name}' does not contain a ParticleSystem. No changes were made.";
        Debug.LogError($"[Band Campfire Setup] {missingMessage}", selectedObjects[0]);
        EditorUtility.DisplayDialog("Band Campfire Setup", missingMessage, "OK");
        return false;
    }
}
#endif
