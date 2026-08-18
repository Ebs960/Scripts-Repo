using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds presentation-only tactical figures from campaign-authored unit visuals.</summary>
public static class BattleUnitVisualFactory
{
    private const float TargetFigureHeight = 0.58f;
    private const float MaximumFormationRadius = 0.46f;
    private static Material attackerRingMaterial;
    private static Material defenderRingMaterial;

    public static void Populate(
        BattleUnitView view,
        BattleUnitState state,
        Transform parent,
        List<GameObject> figures)
    {
        if (view == null || state?.Snapshot == null || parent == null || figures == null)
            return;

        var snapshot = state.Snapshot;
        int count = Mathf.Max(1, snapshot.TacticalFigureCount);
        GameObject leadSource = FindLeadVisualSource(snapshot.SourceUnit);
        var random = new System.Random(snapshot.CampaignRuntimeId != 0
            ? snapshot.CampaignRuntimeId
            : state.UnitId);

        for (int i = 0; i < count; i++)
        {
            GameObject source = i == 0 ? leadSource : PickVariant(snapshot.TacticalSoldierVariants, random);
            if (source == null && leadSource == null)
                source = PickVariant(snapshot.TacticalSoldierVariants, random);
            if (source == null)
                source = leadSource;

            GameObject figure = source != null
                ? Object.Instantiate(source, parent)
                : CreateFallbackFigure(parent, state.Side);
            if (figure == null)
                continue;

            figure.name = $"Figure {i + 1}";
            figure.transform.localPosition = Vector3.zero;
            figure.transform.localRotation = Quaternion.identity;
            SanitizeVisualClone(figure);
            NormalizeFigure(figure);
            if (source != null && source != leadSource)
                AttachEquipment(figure, snapshot.SourceUnit);
            figures.Add(figure);
        }

        Arrange(figures, snapshot.TacticalFormationType, snapshot.TacticalFormationSpacing);
    }

    public static GameObject CreateSelectionRing(Transform parent, BattleSide side)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Selection Ring";
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        ring.transform.localScale = new Vector3(0.55f, 0.012f, 0.55f);
        var collider = ring.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        var renderer = ring.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetRingMaterial(side);
        return ring;
    }

    private static GameObject FindLeadVisualSource(CombatUnit sourceUnit)
    {
        if (sourceUnit == null)
            return null;

        var animator = sourceUnit.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.gameObject != sourceUnit.gameObject
            && animator.GetComponent<BaseUnit>() == null)
            return animator.gameObject;

        for (int i = 0; i < sourceUnit.transform.childCount; i++)
        {
            var child = sourceUnit.transform.GetChild(i);
            if (child != null && child.GetComponentInChildren<Renderer>(true) != null
                && child.GetComponent<BaseUnit>() == null)
                return child.gameObject;
        }

        return null;
    }

    private static GameObject PickVariant(SoldierVariant[] variants, System.Random random)
    {
        if (variants == null || variants.Length == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < variants.Length; i++)
            if (variants[i].modelPrefab != null)
                totalWeight += Mathf.Max(0.01f, variants[i].weight);
        if (totalWeight <= 0f)
            return null;

        double pick = random.NextDouble() * totalWeight;
        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i].modelPrefab == null)
                continue;
            pick -= Mathf.Max(0.01f, variants[i].weight);
            if (pick <= 0d)
                return variants[i].modelPrefab;
        }

        for (int i = variants.Length - 1; i >= 0; i--)
            if (variants[i].modelPrefab != null)
                return variants[i].modelPrefab;
        return null;
    }

    private static GameObject CreateFallbackFigure(Transform parent, BattleSide side)
    {
        var figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        figure.transform.SetParent(parent, false);
        var renderer = figure.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetRingMaterial(side);
        return figure;
    }

    private static void SanitizeVisualClone(GameObject clone)
    {
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (var source in clone.GetComponentsInChildren<AudioSource>(true))
            source.enabled = false;
        foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
            canvas.enabled = false;
        foreach (var particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        var animator = clone.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private static void NormalizeFigure(GameObject figure)
    {
        var renderers = figure.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.y <= 0.001f)
            return;

        float scale = Mathf.Clamp(TargetFigureHeight / bounds.size.y, 0.01f, 10f);
        figure.transform.localScale *= scale;
        bounds = figure.GetComponentsInChildren<Renderer>(true)[0].bounds;
        renderers = figure.GetComponentsInChildren<Renderer>(true);
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        Transform parent = figure.transform.parent;
        Vector3 center = parent != null ? parent.InverseTransformPoint(bounds.center) : bounds.center;
        Vector3 bottom = parent != null
            ? parent.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z))
            : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        figure.transform.localPosition -= new Vector3(center.x, bottom.y, center.z);
    }

    private static void Arrange(List<GameObject> figures, FormationType formation, float authoredSpacing)
    {
        if (figures == null || figures.Count == 0)
            return;

        float spacing = Mathf.Clamp(authoredSpacing * 0.22f, 0.11f, 0.24f);
        var offsets = new List<Vector3>(figures.Count);
        if (formation == FormationType.Wedge)
        {
            offsets.Add(Vector3.zero);
            for (int i = 1; i < figures.Count; i++)
            {
                int rank = (i + 1) / 2;
                float side = i % 2 == 1 ? -1f : 1f;
                offsets.Add(new Vector3(side * rank * spacing, 0f, -rank * spacing));
            }
        }
        else
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(figures.Count));
            int rows = Mathf.CeilToInt(figures.Count / (float)columns);
            for (int i = 0; i < figures.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                offsets.Add(new Vector3(
                    (column - (columns - 1) * 0.5f) * spacing,
                    0f,
                    ((rows - 1) * 0.5f - row) * spacing));
            }
        }

        float radius = 0f;
        for (int i = 0; i < offsets.Count; i++)
            radius = Mathf.Max(radius, new Vector2(offsets[i].x, offsets[i].z).magnitude);
        float fitScale = radius > MaximumFormationRadius ? MaximumFormationRadius / radius : 1f;
        for (int i = 0; i < figures.Count; i++)
            if (figures[i] != null)
                figures[i].transform.localPosition += offsets[i] * fitScale;
    }

    private static void AttachEquipment(GameObject figure, CombatUnit source)
    {
        if (figure == null || source == null)
            return;

        AttachEquipmentItem(figure.transform, source.Weapon, "WeaponHolder", "Weapon Holder", "weaponHolder");
        AttachEquipmentItem(figure.transform, source.ProjectileWeapon, "ProjectileWeaponHolder", "Projectile Weapon Holder", "projectileWeaponHolder");
        AttachEquipmentItem(figure.transform, source.Shield, "ShieldHolder", "Shield Holder", "shieldHolder");
        AttachEquipmentItem(figure.transform, source.Armor, "ArmorHolder", "Armor Holder", "armorHolder");
        AttachEquipmentItem(figure.transform, source.Miscellaneous, "MiscHolder", "Misc Holder", "miscHolder");
    }

    private static void AttachEquipmentItem(Transform root, EquipmentData equipment, params string[] holderNames)
    {
        if (equipment == null || equipment.equipmentPrefab == null)
            return;

        Transform holder = FindHolder(root, holderNames);
        if (holder == null)
            return;

        var item = Object.Instantiate(equipment.equipmentPrefab, holder);
        item.transform.localPosition = Vector3.zero;
        SanitizeVisualClone(item);
    }

    private static Transform FindHolder(Transform root, string[] names)
    {
        if (root == null)
            return null;
        for (int i = 0; i < names.Length; i++)
            if (root.name == names[i])
                return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindHolder(root.GetChild(i), names);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Material GetRingMaterial(BattleSide side)
    {
        ref Material material = ref (side == BattleSide.Attacker
            ? ref attackerRingMaterial
            : ref defenderRingMaterial);
        if (material != null)
            return material;

        Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        material = new Material(shader);
        Color color = side == BattleSide.Attacker
            ? new Color(0.16f, 0.48f, 0.95f, 1f)
            : new Color(0.92f, 0.22f, 0.14f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        return material;
    }
}