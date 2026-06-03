// Custom inspector for Custom/BiomeTerrainHDRP shader.
// Place this file under any folder named "Editor" so Unity compiles it into the editor assembly.
//
// IMPORTANT:
// - This is UI/organization only. It does not change shader behavior.
// - Properties are grouped into foldouts so tuning becomes manageable.
//
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public sealed class BiomeTerrainHDRPShaderGUI : ShaderGUI
{
    private const string PrefKeyPrefix = "BiomeTerrainHDRPShaderGUI.";

    private static bool GetFoldout(string key, bool defaultValue)
        => EditorPrefs.GetBool(PrefKeyPrefix + key, defaultValue);

    private static void SetFoldout(string key, bool value)
        => EditorPrefs.SetBool(PrefKeyPrefix + key, value);

    private static bool Foldout(string key, string title, bool defaultValue)
    {
        bool v = GetFoldout(key, defaultValue);
        EditorGUI.BeginChangeCheck();
        v = EditorGUILayout.Foldout(v, title, true);
        if (EditorGUI.EndChangeCheck())
            SetFoldout(key, v);
        return v;
    }

    private static MaterialProperty Find(MaterialProperty[] props, string name, bool optional = true)
    {
        try { return FindProperty(name, props); }
        catch (ArgumentException)
        {
            if (!optional) throw;
            return null;
        }
    }

    private static void Header(string text)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
    }

    private static void HelpIf(bool condition, string message, MessageType type)
    {
        if (condition) EditorGUILayout.HelpBox(message, type);
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        if (materialEditor == null) return;

        var mat = materialEditor.target as Material;
        if (mat == null)
        {
            base.OnGUI(materialEditor, properties);
            return;
        }

        // Keep Unity’s default render queue / instancing controls visible.
        materialEditor.RenderQueueField();
        materialEditor.EnableInstancingField();
        EditorGUILayout.Space(6);

        DrawValidation(mat, properties);

        // ===================== Core Maps =====================
        if (Foldout("CoreMaps", "Core Maps (required)", true))
        {
            EditorGUI.indentLevel++;

            var biomeIndexMap = Find(properties, "_BiomeIndexMap");
            var heightmap = Find(properties, "_Heightmap");
            var lut = Find(properties, "_LUT");

            materialEditor.TexturePropertySingleLine(new GUIContent("Biome Index Map", "R = texture array slice index per tile/pixel"), biomeIndexMap);
            materialEditor.TexturePropertySingleLine(new GUIContent("Heightmap", "R = elevation (scaled by _ElevationScale)"), heightmap);
            materialEditor.TexturePropertySingleLine(new GUIContent("Tile LUT", "Used for tile highlight / decoding"), lut);

            EditorGUI.indentLevel--;
        }

        // ===================== Texture Arrays =====================
        if (Foldout("TextureArrays", "Biome Texture Arrays", true))
        {
            EditorGUI.indentLevel++;

            materialEditor.TexturePropertySingleLine(new GUIContent("Albedo Array"), Find(properties, "_BiomeAlbedoArray"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Normal Array"), Find(properties, "_BiomeNormalArray"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Mask Array", "R=metallic, G=AO, B=height (optional), A=smoothness"), Find(properties, "_BiomeMaskArray"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Emissive Array"), Find(properties, "_SurfaceEmissiveArray"));

            EditorGUI.indentLevel--;
        }

        // ===================== Biome Lookup Data =====================
        if (Foldout("BiomeLookup", "Biome Lookup Data", false))
        {
            EditorGUI.indentLevel++;

            materialEditor.TexturePropertySingleLine(new GUIContent("Slice→Biome Map"), Find(properties, "_SliceToBiomeMap"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Biome Surface Map"), Find(properties, "_BiomeSurfaceMapTex"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Biome Emissive Map"), Find(properties, "_BiomeEmissiveMapTex"));

            // Note: _BiomeTints and _BiomeParams are vector arrays set from C# (HexMapChunkManager).
            EditorGUILayout.HelpBox("Per-biome tint/params are driven by C# via SetVectorArray (_BiomeTints/_BiomeParams).", MessageType.Info);

            EditorGUI.indentLevel--;
        }

        // ===================== Displacement & Normals =====================
        if (Foldout("Displacement", "Displacement & Height-Normals", true))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_ElevationScale"), "Elevation Scale");
            materialEditor.ShaderProperty(Find(properties, "_NormalStrength"), "Heightmap Normal Strength");
            materialEditor.ShaderProperty(Find(properties, "_BiomeNormalStrength"), "Biome Normal Strength");
            materialEditor.ShaderProperty(Find(properties, "_NormalSampleRadius"), "Normal Sample Radius (texels)");
            EditorGUI.indentLevel--;
        }

        // ===================== Triplanar =====================
        if (Foldout("Triplanar", "Triplanar", true))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_TriTiling"), "Global Tiling");
            materialEditor.ShaderProperty(Find(properties, "_TriBlend"), "Blend Sharpness");
            materialEditor.ShaderProperty(Find(properties, "_UseTriplanar"), "Use Triplanar");

            var lodStart = Find(properties, "_TriplanarLODStart");
            var lodEnd = Find(properties, "_TriplanarLODEnd");
            if (lodStart != null && lodEnd != null)
            {
                Header("Distance LOD");
                materialEditor.ShaderProperty(lodStart, "LOD Start Distance");
                materialEditor.ShaderProperty(lodEnd, "LOD End Distance");
            }
            EditorGUI.indentLevel--;
        }

        // ===================== Anti-Tiling =====================
        if (Foldout("AntiTiling", "Anti-Tiling", false))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_AntiTileStrength"), "Anti-Tile Strength");
            EditorGUILayout.HelpBox("Anti-tiling breaks large-scale repetition but costs extra texture samples. If performance drops, reduce Anti-Tile Strength or push Triplanar LOD closer.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        // ===================== Biome Blending =====================
        if (Foldout("BiomeBlending", "Biome Transition Blending", false))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_BiomeBlendRadius"), "Blend Radius (texels)");
            materialEditor.ShaderProperty(Find(properties, "_BiomeBlendSharpness"), "Height Blend Sharpness");
            EditorGUILayout.HelpBox("Blending quality depends on mask map B channel providing a usable height signal. If you don't have height packed, keep sharpness low.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        // ===================== Cliffs =====================
        if (Foldout("Cliffs", "Cliffs", false))
        {
            EditorGUI.indentLevel++;

            materialEditor.TexturePropertySingleLine(new GUIContent("Cliff Albedo Array"), Find(properties, "_CliffAlbedoArray"));
            materialEditor.TexturePropertySingleLine(new GUIContent("Cliff Normal Array"), Find(properties, "_CliffNormalArray"));
            materialEditor.ShaderProperty(Find(properties, "_CliffTiling"), "Cliff Tiling");
            materialEditor.ShaderProperty(Find(properties, "_CliffStrength"), "Cliff Strength");
            materialEditor.ShaderProperty(Find(properties, "_CliffSlopeThreshold"), "Slope Threshold");
            materialEditor.ShaderProperty(Find(properties, "_CliffSlopeBlend"), "Slope Blend");
            materialEditor.ShaderProperty(Find(properties, "_CliffStepThreshold"), "Step Threshold");
            materialEditor.ShaderProperty(Find(properties, "_CliffStepBlend"), "Step Blend");

            EditorGUI.indentLevel--;
        }

        // ===================== Snow / Wetness =====================
        if (Foldout("SnowWet", "Snow / Wetness", false))
        {
            EditorGUI.indentLevel++;

            Header("Global Snow");
            materialEditor.ShaderProperty(Find(properties, "_GlobalSnowAmount"), "Global Snow Amount");
            materialEditor.ColorProperty(Find(properties, "_SnowColor"), "Snow Color");
            materialEditor.ShaderProperty(Find(properties, "_SnowSmoothness"), "Snow Smoothness");

            var snowNStr = Find(properties, "_SnowNormalStrength");
            var snowNTile = Find(properties, "_SnowNormalTiling");
            var snowSparkle = Find(properties, "_SnowSparkleStrength");
            if (snowNStr != null || snowNTile != null || snowSparkle != null)
            {
                Header("Snow Surface Detail");
                if (snowNStr != null) materialEditor.ShaderProperty(snowNStr, "Snow Normal Strength");
                if (snowNTile != null) materialEditor.ShaderProperty(snowNTile, "Snow Normal Tiling");
                if (snowSparkle != null) materialEditor.ShaderProperty(snowSparkle, "Sparkle Strength");
            }

            Header("Global Wetness");
            materialEditor.ShaderProperty(Find(properties, "_GlobalWetness"), "Global Wetness");

            EditorGUI.indentLevel--;
        }

        // ===================== Material Modifiers =====================
        if (Foldout("MaterialModifiers", "Material Modifiers", true))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_MetallicMultiplier"), "Global Metallic Multiplier");
            materialEditor.ShaderProperty(Find(properties, "_AOIntensity"), "Global AO Intensity");
            materialEditor.ShaderProperty(Find(properties, "_SmoothnessMultiplier"), "Global Smoothness Multiplier");
            EditorGUI.indentLevel--;
        }

        // ===================== Overlays =====================
        if (Foldout("Overlays", "Overlays", false))
        {
            EditorGUI.indentLevel++;

            Header("Fog Overlay");
            materialEditor.TexturePropertySingleLine(new GUIContent("Fog Mask"), Find(properties, "_FogMask"));
            materialEditor.ShaderProperty(Find(properties, "_EnableFog"), "Enable Fog Overlay");
            materialEditor.ColorProperty(Find(properties, "_TerrainFogColor"), "Fog Color");

            Header("Ownership Overlay");
            materialEditor.TexturePropertySingleLine(new GUIContent("Ownership Overlay"), Find(properties, "_OwnershipOverlay"));
            materialEditor.ShaderProperty(Find(properties, "_EnableOwnership"), "Enable Ownership Overlay");
            materialEditor.ShaderProperty(Find(properties, "_OwnershipAlpha"), "Ownership Alpha");

            EditorGUILayout.Space(4);
            Header("Hex Grid Overlay");
            materialEditor.ShaderProperty(Find(properties, "_ShowHexGrid"), "Show Hex Grid");
            materialEditor.ColorProperty(Find(properties, "_HexGridColor"), "Hex Grid Color");
            materialEditor.ShaderProperty(Find(properties, "_HexGridWidth"), "Hex Grid Width (texels)");
            materialEditor.ShaderProperty(Find(properties, "_HexGridFadeDistance"), "Hex Grid Fade Distance");

            EditorGUI.indentLevel--;
        }

        // ===================== Lighting (fallback controls) =====================
        if (Foldout("Lighting", "Lighting (fallback controls)", false))
        {
            EditorGUI.indentLevel++;

            materialEditor.VectorProperty(Find(properties, "_SunDir"), "Sun Direction");
            materialEditor.ColorProperty(Find(properties, "_SunColor"), "Sun Color");
            materialEditor.ShaderProperty(Find(properties, "_SunIntensity"), "Sun Intensity");

            materialEditor.ColorProperty(Find(properties, "_AmbientSkyColor"), "Ambient Sky");
            materialEditor.ColorProperty(Find(properties, "_AmbientGroundColor"), "Ambient Ground");
            materialEditor.ShaderProperty(Find(properties, "_AmbientIntensity"), "Ambient Intensity");

            EditorGUI.indentLevel--;
        }

        // ===================== Debug / Highlight =====================
        if (Foldout("Debug", "Debug / Highlight", false))
        {
            EditorGUI.indentLevel++;
            materialEditor.ShaderProperty(Find(properties, "_HighlightTileIndex"), "Highlight Tile Index");
            materialEditor.ColorProperty(Find(properties, "_HighlightColor"), "Highlight Color");
            EditorGUI.indentLevel--;
        }

        // Keep Unity’s default options (advanced settings like double-sided GI etc.)
        EditorGUILayout.Space(8);
        materialEditor.DoubleSidedGIField();
    }

    private static void DrawValidation(Material mat, MaterialProperty[] properties)
    {
        var biomeIndexMap = Find(properties, "_BiomeIndexMap");
        var heightmap = Find(properties, "_Heightmap");
        var albedoArr = Find(properties, "_BiomeAlbedoArray");
        var normalArr = Find(properties, "_BiomeNormalArray");
        var maskArr = Find(properties, "_BiomeMaskArray");

        bool missing =
            (biomeIndexMap != null && biomeIndexMap.textureValue == null) ||
            (heightmap != null && heightmap.textureValue == null) ||
            (albedoArr != null && albedoArr.textureValue == null) ||
            (normalArr != null && normalArr.textureValue == null) ||
            (maskArr != null && maskArr.textureValue == null);

        HelpIf(missing,
            "One or more required textures are missing. This shader will usually render solid/incorrect if BiomeIndexMap/Heightmap or the texture arrays are not assigned.\n\n" +
            "Required: _BiomeIndexMap, _Heightmap, _BiomeAlbedoArray, _BiomeNormalArray, _BiomeMaskArray.",
            MessageType.Warning);

        // Tessellation properties exist in the shader, but the tessellation path is currently disabled
        // (Unity pragma limitations). Make that explicit so it doesn't confuse tuning.
        var tessToggle = Find(properties, "_EnableTessellation");
        if (tessToggle != null && tessToggle.floatValue > 0.5f)
        {
            EditorGUILayout.HelpBox(
                "Tessellation is currently not compiled in this shader build (UI-only properties remain). If you want tessellation back, we should implement it as a separate tessellation variant shader/pass so Unity can compile it reliably.",
                MessageType.Info);
        }
    }
}
#endif

