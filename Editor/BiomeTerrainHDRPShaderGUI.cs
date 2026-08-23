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

    private static void DrawShaderProperty(MaterialEditor materialEditor, MaterialProperty property, string label)
    {
        if (property != null)
            materialEditor.ShaderProperty(property, label);
    }

    private static void DrawColorProperty(MaterialEditor materialEditor, MaterialProperty property, string label)
    {
        if (property != null)
            materialEditor.ColorProperty(property, label);
    }

    private static void DrawVectorProperty(MaterialEditor materialEditor, MaterialProperty property, string label)
    {
        if (property != null)
            materialEditor.VectorProperty(property, label);
    }

    private static void DrawTextureProperty(MaterialEditor materialEditor, MaterialProperty property, GUIContent label)
    {
        if (property != null)
            materialEditor.TexturePropertySingleLine(label, property);
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

            DrawTextureProperty(materialEditor, biomeIndexMap, new GUIContent("Biome Index Map", "R = texture array slice index per tile/pixel"));
            DrawTextureProperty(materialEditor, heightmap, new GUIContent("Heightmap", "R = elevation (scaled by _ElevationScale)"));
            DrawTextureProperty(materialEditor, lut, new GUIContent("Tile LUT", "Used for tile highlight / decoding"));

            Header("Tile Season Mask");
            DrawTextureProperty(materialEditor, Find(properties, "_TileSeasonMask"), new GUIContent("Tile Season Mask", "R=snow, G=wet, B=dry"));
            DrawVectorProperty(materialEditor, Find(properties, "_TileSeasonMask_TexSize"), "Tile Season Mask TexSize");
            DrawVectorProperty(materialEditor, Find(properties, "_TileSeasonMask_ST"), "Tile Season Mask ST");

            Header("Map Dimensions");
            DrawShaderProperty(materialEditor, Find(properties, "_MapWidth"), "Map Width");
            DrawShaderProperty(materialEditor, Find(properties, "_MapHeight"), "Map Height");
            DrawShaderProperty(materialEditor, Find(properties, "_BiomeCount"), "Biome Count");
            DrawShaderProperty(materialEditor, Find(properties, "_TotalSlices"), "Total Texture Slices");

            EditorGUI.indentLevel--;
        }

        // ===================== Texture Arrays =====================
        if (Foldout("TextureArrays", "Biome Texture Arrays", true))
        {
            EditorGUI.indentLevel++;

            DrawTextureProperty(materialEditor, Find(properties, "_BiomeAlbedoArray"), new GUIContent("Albedo Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_BiomeNormalArray"), new GUIContent("Normal Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_BiomeMaskArray"), new GUIContent("Mask Array", "R=metallic, G=AO, B=height (optional), A=smoothness"));
            DrawTextureProperty(materialEditor, Find(properties, "_SurfaceEmissiveArray"), new GUIContent("Emissive Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_BiomeHeightArray"), new GUIContent("Height Array"));

            EditorGUI.indentLevel--;
        }

        // ===================== Biome Lookup Data =====================
        if (Foldout("BiomeLookup", "Biome Lookup Data", false))
        {
            EditorGUI.indentLevel++;

            DrawTextureProperty(materialEditor, Find(properties, "_SliceToBiomeMap"), new GUIContent("Slice→Biome Map"));
            DrawTextureProperty(materialEditor, Find(properties, "_BiomeSurfaceMapTex"), new GUIContent("Biome Surface Map"));
            DrawTextureProperty(materialEditor, Find(properties, "_BiomeEmissiveMapTex"), new GUIContent("Biome Emissive Map"));

            // Note: _BiomeTints and _BiomeParams are vector arrays set from C# (HexMapChunkManager).
            EditorGUILayout.HelpBox("Per-biome tint/params are driven by C# via SetVectorArray (_BiomeTints/_BiomeParams).", MessageType.Info);

            EditorGUI.indentLevel--;
        }

        // ===================== Displacement & Normals =====================
        if (Foldout("Displacement", "Displacement & Height-Normals", true))
        {
            EditorGUI.indentLevel++;
            DrawShaderProperty(materialEditor, Find(properties, "_ElevationScale"), "Elevation Scale");
            DrawShaderProperty(materialEditor, Find(properties, "_SurfaceHeightScale"), "Surface Height Scale");
            DrawShaderProperty(materialEditor, Find(properties, "_NormalStrength"), "Heightmap Normal Strength");
            DrawShaderProperty(materialEditor, Find(properties, "_BiomeNormalStrength"), "Biome Normal Strength");
            DrawShaderProperty(materialEditor, Find(properties, "_NormalSampleRadius"), "Normal Sample Radius (texels)");
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

        // ===================== Frozen Water =====================
        if (Foldout("FrozenWater", "Frozen Water", false))
        {
            EditorGUI.indentLevel++;

            DrawTextureProperty(materialEditor, Find(properties, "_FreezeMaskTex"), new GUIContent("Freeze Mask", "R=freeze, G=lake, B=river"));
            DrawVectorProperty(materialEditor, Find(properties, "_FreezeMask_ST"), "Freeze Mask ST");
            DrawShaderProperty(materialEditor, Find(properties, "_FreezeProgress"), "Freeze Progress");

            Header("Ice Texture Arrays");
            DrawTextureProperty(materialEditor, Find(properties, "_IceAlbedoArray"), new GUIContent("Ice Albedo Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_IceNormalArray"), new GUIContent("Ice Normal Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_IceMaskArray"), new GUIContent("Ice Mask Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_IceHeightArray"), new GUIContent("Ice Height Array"));

            Header("Ice Material");
            DrawColorProperty(materialEditor, Find(properties, "_LakeIceTint"), "Lake Ice Tint");
            DrawColorProperty(materialEditor, Find(properties, "_RiverIceTint"), "River Ice Tint");
            DrawShaderProperty(materialEditor, Find(properties, "_LakeIceTiling"), "Lake Ice Tiling");
            DrawShaderProperty(materialEditor, Find(properties, "_RiverIceTiling"), "River Ice Tiling");
            DrawShaderProperty(materialEditor, Find(properties, "_IceSliceCount"), "Ice Slice Count");
            DrawShaderProperty(materialEditor, Find(properties, "_IceNormalStrength"), "Ice Normal Strength");
            DrawShaderProperty(materialEditor, Find(properties, "_IceSmoothness"), "Ice Smoothness");
            DrawShaderProperty(materialEditor, Find(properties, "_IceMetallic"), "Ice Metallic");
            DrawShaderProperty(materialEditor, Find(properties, "_FreezeOpaqueThreshold"), "Freeze Opaque Threshold");

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

            DrawTextureProperty(materialEditor, Find(properties, "_CliffAlbedoArray"), new GUIContent("Cliff Albedo Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_CliffNormalArray"), new GUIContent("Cliff Normal Array"));
            DrawTextureProperty(materialEditor, Find(properties, "_CliffAlbedoPreview"), new GUIContent("Cliff Albedo Preview (fallback)"));
            DrawTextureProperty(materialEditor, Find(properties, "_CliffNormalPreview"), new GUIContent("Cliff Normal Preview (fallback)"));
            DrawShaderProperty(materialEditor, Find(properties, "_CliffTiling"), "Cliff Tiling");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffStrength"), "Cliff Strength");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffSlopeThreshold"), "Slope Threshold");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffSlopeBlend"), "Slope Blend");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffStepThreshold"), "Step Threshold");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffStepBlend"), "Step Blend");
            DrawShaderProperty(materialEditor, Find(properties, "_CliffSliceCount"), "Cliff Slice Count");

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
            DrawShaderProperty(materialEditor, Find(properties, "_GlobalWetness"), "Global Wetness");
            DrawShaderProperty(materialEditor, Find(properties, "_WetNormalStrength"), "Wet Normal Strength");
            DrawShaderProperty(materialEditor, Find(properties, "_WetNormalTiling"), "Wet Normal Tiling");
            DrawShaderProperty(materialEditor, Find(properties, "_WetSmoothnessBoost"), "Wet Smoothness Boost");
            DrawShaderProperty(materialEditor, Find(properties, "_WetAlbedoDarken"), "Wet Albedo Darken");

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
            DrawTextureProperty(materialEditor, Find(properties, "_FogMask"), new GUIContent("Fog Mask"));
            DrawShaderProperty(materialEditor, Find(properties, "_EnableFog"), "Enable Fog Overlay");
            DrawColorProperty(materialEditor, Find(properties, "_TerrainFogColor"), "Fog Color");

            Header("Campaign Map Mode Overlay");
            DrawTextureProperty(materialEditor, Find(properties, "_MapModeOverlay"), new GUIContent("Map Mode Overlay"));
            DrawShaderProperty(materialEditor, Find(properties, "_EnableMapMode"), "Enable Map Mode Overlay");

            EditorGUILayout.Space(4);
            Header("Hex Grid Overlay");
            DrawShaderProperty(materialEditor, Find(properties, "_ShowHexGrid"), "Show Hex Grid");
            DrawColorProperty(materialEditor, Find(properties, "_HexGridColor"), "Hex Grid Color");
            DrawShaderProperty(materialEditor, Find(properties, "_HexGridWidth"), "Hex Grid Width (texels)");
            DrawShaderProperty(materialEditor, Find(properties, "_HexGridFadeDistance"), "Hex Grid Fade Distance");

            EditorGUI.indentLevel--;
        }

        // ===================== Tessellation =====================
        if (Foldout("Tessellation", "Tessellation", false))
        {
            EditorGUI.indentLevel++;
            DrawShaderProperty(materialEditor, Find(properties, "_EnableTessellation"), "Enable Tessellation");
            DrawShaderProperty(materialEditor, Find(properties, "_TessellationFactor"), "Tessellation Factor");
            DrawShaderProperty(materialEditor, Find(properties, "_TessellationFadeStart"), "Tessellation Fade Start");
            DrawShaderProperty(materialEditor, Find(properties, "_TessellationFadeEnd"), "Tessellation Fade End");
            EditorGUI.indentLevel--;
        }

        // ===================== Lighting (fallback controls) =====================
        if (Foldout("Lighting", "Lighting (fallback controls)", false))
        {
            EditorGUI.indentLevel++;

            DrawVectorProperty(materialEditor, Find(properties, "_FallbackSunDirectionWS"), "Fallback Sun Direction WS");
            DrawColorProperty(materialEditor, Find(properties, "_FallbackSunColor"), "Fallback Sun Color");
            DrawShaderProperty(materialEditor, Find(properties, "_FallbackSunIntensity"), "Fallback Sun Intensity");
            DrawShaderProperty(materialEditor, Find(properties, "_FallbackAmbient"), "Fallback Ambient");

            EditorGUI.indentLevel--;
        }

        // ===================== Debug / Highlight =====================
        if (Foldout("Debug", "Debug / Highlight", false))
        {
            EditorGUI.indentLevel++;
            DrawShaderProperty(materialEditor, Find(properties, "_TerrainDebugMode"), "Terrain Debug Mode");
            DrawShaderProperty(materialEditor, Find(properties, "_HighlightTileIndex"), "Highlight Tile Index");
            DrawColorProperty(materialEditor, Find(properties, "_HighlightColor"), "Highlight Color");
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
