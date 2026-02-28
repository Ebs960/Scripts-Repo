Shader "Custom/MenuPlanetPreview"
{
    Properties
    {
        _LandScale("Land Scale", Range(0.5, 5.0)) = 2.0
        _LandThreshold("Land Threshold", Range(0, 1)) = 0.4
        _Temperature("Temperature", Range(0, 1)) = 0.5
        _Moisture("Moisture", Range(0, 1)) = 0.5
        _Elevation("Elevation", Range(0, 1)) = 0.3
            _BiomeTint("Biome Tint", Color) = (0.33,0.6,0.26,1)
            _DesertFactor("Desert Factor", Range(0,1)) = 0.0
            _TropicalFactor("Tropical Factor", Range(0,1)) = 0.0
            _SnowFactor("Snow Factor", Range(0,1)) = 0.0
            _DetailScale("Detail Scale", Float) = 18.0
            _DetailStrength("Detail Strength", Range(0,1)) = 0.18
            // _AtmosphereColor, _AtmospherePower, _AtmosphereRadius kept in CBUFFER
            // for SRP Batcher layout but no longer exposed — atmosphere is handled by
            // the separate MenuPlanetAtmosphere shell shader.
            [HideInInspector] _AtmosphereColor("Atmosphere Color", Color) = (0.62,0.78,0.95,1)
            [HideInInspector] _AtmospherePower("Atmosphere Power", Range(0.5,6)) = 3.5
            [HideInInspector] _AtmosphereRadius("Atmosphere Radius", Float) = 1.0
            _MapStyle("Map Style", Range(0, 1)) = 0.0
        [Header(Displacement)]
            _DisplacementScale("Displacement Scale", Range(0, 0.15)) = 0.035
        [Header(Sun)]
            _SunDirection("Sun Direction", Vector) = (-0.5, -0.7, 0.3, 0)
            _SunColor("Sun Color", Color) = (1, 0.95, 0.85, 1)
            _SunIntensity("Sun Intensity", Float) = 1.0
        [Header(Civilization)]
            _CivCount("Civilization Count", Float) = 4.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // =====================================================================
        //  Forward rendering pass — main color output
        // =====================================================================
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            // SRP Batcher compatible CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float _MapStyle;  // 0 = normal, 1 = infernal/demonic
                    float _DetailScale;
                    float _DetailStrength;
                    float4 _AtmosphereColor;
                    float _AtmospherePower;
                    float _AtmosphereRadius;
                float _DisplacementScale;
                float4 _SunDirection;
                float4 _SunColor;
                float _SunIntensity;
                float _CivCount;
            CBUFFER_END

            // -----------------------------------------------------------------
            //  Structs
            // -----------------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // -----------------------------------------------------------------
            //  Noise — value noise with smooth interpolation (3D)
            // -----------------------------------------------------------------
            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float n000 = hash31(i + float3(0, 0, 0));
                float n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0));
                float n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1));
                float n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1));
                float n111 = hash31(i + float3(1, 1, 1));

                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z
                );
            }

            // Fractal Brownian Motion — 4 octaves gives nice organic blobs
            float fbm(float3 p)
            {
                float value = 0.0;
                float amp   = 0.5;
                float freq  = 1.0;
                for (int i = 0; i < 4; i++)
                {
                    value += amp * noise3D(p * freq);
                    freq  *= 2.0;
                    amp   *= 0.5;
                }
                return value;
            }

            // -----------------------------------------------------------------
            //  Color helpers
            // -----------------------------------------------------------------
            float3 GetLandColor(float temp, float moist)
            {
                // Temperature gradient: cold(0) → temperate(0.5) → hot(1)
                float3 coldColor = float3(0.78, 0.85, 0.90); // icy blue-gray
                float3 tempColor = float3(0.30, 0.58, 0.22); // rich green
                float3 hotColor  = float3(0.82, 0.72, 0.42); // sandy tan

                float t1 = saturate(temp * 2.0);
                float t2 = saturate((temp - 0.5) * 2.0);
                float3 baseColor = lerp(lerp(coldColor, tempColor, t1), hotColor, t2);

                // Moisture: dry → browner/duller, wet → greener/richer
                float3 dryShift = float3(0.80, 0.68, 0.50);
                float3 wetShift = float3(0.40, 0.75, 0.35);
                float3 moistTint = lerp(dryShift, wetShift, moist);
                baseColor = saturate(baseColor * moistTint * 1.7);

                return baseColor;
            }

            float3 GetOceanColor(float temp)
            {
                float3 coldOcean = float3(0.08, 0.15, 0.30); // dark slate blue
                float3 warmOcean = float3(0.06, 0.22, 0.45); // medium blue
                float3 hotOcean  = float3(0.10, 0.32, 0.52); // tropical blue

                float t1 = saturate(temp * 2.0);
                float t2 = saturate((temp - 0.5) * 2.0);
                return lerp(lerp(coldOcean, warmOcean, t1), hotOcean, t2);
            }

            // -----------------------------------------------------------------
            //  Infernal / Demonic color helpers
            // -----------------------------------------------------------------
            float3 GetHellLandColor(float moist, float elevH)
            {
                // Charred, scorched earth tones
                float3 dryAsh    = float3(0.18, 0.12, 0.10); // dark charcoal
                float3 wetAsh    = float3(0.25, 0.15, 0.12); // slightly warmer charcoal
                float3 baseLand  = lerp(dryAsh, wetAsh, moist);

                // Higher elevation → darker, almost black obsidian
                float3 obsidian  = float3(0.08, 0.06, 0.05);
                baseLand = lerp(baseLand, obsidian, saturate(elevH * 1.2));

                return baseLand;
            }

            float3 GetLavaOceanColor(float3 sampleP, float timeVal)
            {
                // Animated lava: dark crust with bright orange/red cracks
                float lavaCrust = fbm(sampleP * 3.0 + float3(13.7, 7.3, 29.1));
                float lavaFlow  = noise3D(sampleP * 5.0 + float3(timeVal * 0.3, timeVal * 0.2, timeVal * 0.1));

                float3 darkCrust   = float3(0.12, 0.04, 0.02); // cooled lava crust
                float3 hotCrack    = float3(0.95, 0.35, 0.05); // bright orange
                float3 whiteHot    = float3(1.00, 0.75, 0.25); // yellow-white hot spots

                // Cracks where the noise creates thin bright lines
                float crackMask = smoothstep(0.42, 0.50, lavaCrust);
                float hotSpot   = smoothstep(0.60, 0.70, lavaFlow) * crackMask;

                float3 lavaColor = lerp(darkCrust, hotCrack, crackMask * 0.7);
                lavaColor = lerp(lavaColor, whiteHot, hotSpot * 0.5);

                return lavaColor;
            }

            // -----------------------------------------------------------------
            //  Vertex displacement helper
            // -----------------------------------------------------------------
            float GetDisplacement(float3 objNorm)
            {
                float3 samplePos = objNorm * _LandScale;
                float n = fbm(samplePos + float3(42.3, 17.1, 83.7));
                // Wide transition (0.12 each side) so coastlines are gentle slopes, not cliffs
                float edge = smoothstep(_LandThreshold - 0.12, _LandThreshold + 0.12, n);
                float elevNoise = fbm(samplePos * 1.5 + float3(99.1, 55.3, 12.7));
                // Displace land outward; oceans stay at base radius
                // Note: _Elevation only affects color banding, NOT geometry displacement
                return edge * elevNoise * _DisplacementScale;
            }

            // -----------------------------------------------------------------
            //  Vertex
            // -----------------------------------------------------------------
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // --- Vertex displacement ---
                float baseRadius = max(1e-5, length(input.positionOS.xyz));
                float3 objNorm = input.positionOS.xyz / baseRadius;

                float disp = GetDisplacement(objNorm);
                float3 displacedOS = objNorm * (baseRadius * (1.0 + disp));

                // Compute displaced normal from displaced positions (more stable than
                // trying to perturb the normal directly).
                float eps = 0.012;
                float3 tangent1 = normalize(cross(objNorm, abs(objNorm.y) < 0.99 ? float3(0,1,0) : float3(1,0,0)));
                float3 tangent2 = cross(objNorm, tangent1);

                float3 nU = normalize(objNorm + tangent1 * eps);
                float3 nV = normalize(objNorm + tangent2 * eps);
                float dispU = GetDisplacement(nU);
                float dispV = GetDisplacement(nV);
                float3 p  = displacedOS;
                float3 pU = nU * (baseRadius * (1.0 + dispU));
                float3 pV = nV * (baseRadius * (1.0 + dispV));
                float3 dispNormalOS = normalize(cross(pV - p, pU - p));

                float3 worldPos = TransformObjectToWorld(displacedOS);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(dispNormalOS);
                output.positionOS = input.positionOS.xyz; // undisplaced for fragment noise sampling

                return output;
            }

            // -----------------------------------------------------------------
            //  Fragment
            // -----------------------------------------------------------------
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // _MapStyle: 0 = normal, 0.5 = infernal, 1.0 = demonic
                float style = saturate(_MapStyle);
                // infernal blend: ramps 0→1 over style 0.0→0.5
                float infernal = saturate(style * 2.0);
                // demonic blend: ramps 0→1 over style 0.5→1.0 (on top of infernal)
                float demonic = saturate((style - 0.5) * 2.0);
                float timeVal = _Time.y;

                // Sample noise at object-space position (seamless on sphere surface)
                float3 objNorm = normalize(input.positionOS);
                float3 samplePos = objNorm * _LandScale;
                float n = fbm(samplePos + float3(42.3, 17.1, 83.7));

                // Soft edge between land and ocean
                float edge = smoothstep(_LandThreshold - 0.04, _LandThreshold + 0.04, n);

                // ---- Elevation noise ----
                float elevNoise = fbm(samplePos * 1.5 + float3(99.1, 55.3, 12.7));
                float terrainHeight = elevNoise * _Elevation;

                float midBand  = smoothstep(0.12, 0.30, terrainHeight);
                float highBand = smoothstep(0.30, 0.50, terrainHeight);
                float mtnBand  = smoothstep(0.50, 0.65, terrainHeight);
                float snowBand = smoothstep(0.62, 0.78, terrainHeight);

                float latitude = abs(objNorm.y);

                // ==============================================================
                //  Latitude-based local climate (biome zoning)
                // ==============================================================
                // Equator is warmer, poles are colder. Temperature slider shifts the overall range.
                // ±0.45 gives dramatic visible variation: jungle equator → tundra poles
                float latTempBias = (1.0 - latitude) * 0.9 - 0.45;
                float localTemp = saturate(_Temperature + latTempBias);
                // Moisture: equator & ~60° wetter, ~30° drier (Hadley cell approximation)
                // Also add noise so the wet/dry bands aren't perfect rings
                float moistLatitude = cos(latitude * 3.14159 * 2.0) * 0.3;
                float moistNoise = (noise3D(objNorm * 3.5 + float3(77.7, 33.3, 11.1)) - 0.5) * 0.25;
                float localMoist = saturate(_Moisture + moistLatitude + moistNoise);

                // ==============================================================
                //  Rivers (shared noise — used by normal, infernal, and demonic)
                // ==============================================================
                // Multi-octave domain warping for natural, sinuous river curves.
                // First warp layer: large-scale bends
                float3 warp1 = float3(
                    noise3D(samplePos * 1.2 + float3(12.5, 34.7, 56.9)),
                    noise3D(samplePos * 1.2 + float3(78.1, 23.4, 91.6)),
                    noise3D(samplePos * 1.2 + float3(45.3, 67.8, 11.2))
                ) * 0.5 - 0.25;
                // Second warp layer: smaller meanders
                float3 warp2 = float3(
                    noise3D(samplePos * 3.5 + float3(91.3, 17.8, 42.1)),
                    noise3D(samplePos * 3.5 + float3(33.7, 82.5, 64.9)),
                    noise3D(samplePos * 3.5 + float3(58.2, 11.6, 73.4))
                ) * 0.15 - 0.075;

                float3 riverSample = samplePos + warp1 + warp2;
                // Primary river: one or two big continental rivers
                float riverNoise1 = noise3D(riverSample * 2.0 + float3(3.3, 77.7, 21.1));
                // Secondary: smaller tributary
                float riverNoise2 = noise3D(riverSample * 3.8 + float3(61.2, 9.8, 44.4));
                // Thinner bands for more realistic width
                float river1 = 1.0 - smoothstep(0.0, 0.040, abs(riverNoise1 - 0.5));
                float river2 = 1.0 - smoothstep(0.0, 0.028, abs(riverNoise2 - 0.5));
                float riverMask = max(river1 * 1.0, river2 * 0.4);
                riverMask *= edge; // on land only

                // ==============================================================
                //  NORMAL WORLD colors
                // ==============================================================
                // Use latitude-based local temperature & moisture for per-pixel biome diversity
                float3 landColor  = GetLandColor(localTemp, localMoist);
                float3 baseOceanColor = GetOceanColor(localTemp);

                // Ocean depth: shallow turquoise near coasts, deep navy in open ocean
                float oceanDepthFactor = saturate((_LandThreshold - n) / max(0.01, _LandThreshold * 0.5));
                float3 shallowOcean = lerp(float3(0.10, 0.42, 0.50), float3(0.08, 0.48, 0.55), localTemp);
                float3 oceanColor = lerp(shallowOcean, baseOceanColor * 0.7, smoothstep(0.0, 0.7, oceanDepthFactor));
                // Warm tropical shallows
                oceanColor = lerp(oceanColor, float3(0.08, 0.50, 0.52),
                    saturate(localTemp - 0.6) * (1.0 - oceanDepthFactor) * 0.3);

                // Apply biome tint + desert/tropical overlays — computed PER-PIXEL from local climate
                // Desert: hot + dry regions. Tropical: hot + wet regions.
                float localDesertAmt = saturate(localTemp * (1.0 - localMoist) * 1.8);
                float localTropicalAmt = saturate(localMoist * localTemp * 2.0);
                    // Suppress biome overlays on infernal/demonic worlds
                    float hellBlend = saturate(infernal + demonic);
                    localDesertAmt *= (1.0 - hellBlend);
                    localTropicalAmt *= (1.0 - hellBlend);

                // Desert tint (sandy) and tropical tint (deep green)
                float3 desertTint = float3(0.85, 0.70, 0.45);
                float3 tropicalTint = float3(0.08, 0.45, 0.12);

                // Blend landColor toward desert/tropical based on per-pixel amounts + latitude bands
                // Desert tint peaks at subtropical latitudes (~30°)
                float subtropicalBand = 1.0 - smoothstep(0.0, 0.35, abs(latitude - 0.45));
                landColor = lerp(landColor, desertTint, localDesertAmt * (subtropicalBand * 0.5 + 0.5));
                // Tropical tint strongest near equator
                float equatorialBand = 1.0 - smoothstep(0.0, 0.30, latitude);
                landColor = lerp(landColor, tropicalTint, localTropicalAmt * (equatorialBand * 0.5 + 0.5));
                // --------------------------------------------------------------
                //  High-frequency detail normal perturbation
                // --------------------------------------------------------------
                float eps = 0.0015 * max(1.0, _DetailScale);
                float d0 = fbm(samplePos * _DetailScale);
                float dx = fbm((samplePos + float3(eps,0,0)) * _DetailScale);
                float dy = fbm((samplePos + float3(0,eps,0)) * _DetailScale);
                float dz = fbm((samplePos + float3(0,0,eps)) * _DetailScale);
                float3 grad = normalize(float3(dx - d0, dy - d0, dz - d0));
                float3 normal = normalize(input.normalWS + grad * _DetailStrength * 1.2);

                // _BiomeTint used only as a very subtle hint — per-pixel latitude colors dominate
                landColor = lerp(landColor, _BiomeTint.rgb, 0.05);

                // Elevation shading
                float3 highlandColor = lerp(landColor, float3(0.55, 0.50, 0.42), 0.6);
                float3 mountainColor = float3(0.58, 0.56, 0.52);
                float3 snowPeakColor = float3(0.92, 0.93, 0.96);

                float3 elevatedLand = landColor * lerp(0.85, 1.0, midBand);
                elevatedLand = lerp(elevatedLand, highlandColor, highBand);
                elevatedLand = lerp(elevatedLand, mountainColor, mtnBand);
                // Snow: uses local temperature so equatorial peaks have less snow
                float snowAmount = snowBand * saturate(1.0 - localTemp * 1.3 + _SnowFactor * 0.8);
                elevatedLand = lerp(elevatedLand, snowPeakColor, snowAmount);

                // Slope coloring: steep cliff faces show exposed rock
                float3 sphereNormalWS = normalize(TransformObjectToWorldNormal(objNorm));
                float slopeDot = dot(normalize(input.normalWS), sphereNormalWS);
                float slopeFactor = smoothstep(0.88, 0.65, slopeDot) * edge;
                float3 rockColor = lerp(float3(0.45, 0.40, 0.35), float3(0.55, 0.50, 0.44), elevNoise);
                elevatedLand = lerp(elevatedLand, rockColor, slopeFactor * 0.7);

                // Biome micro-textures: high-freq noise variation per biome type
                float microNoise = noise3D(samplePos * 35.0 + float3(7.1, 13.3, 21.7));
                float microNoise2 = noise3D(samplePos * 55.0 + float3(41.2, 8.8, 63.1));
                // Forest canopy dapple (temperate/tropical zones)
                float forestMask = saturate(localMoist * (1.0 - highBand)) * saturate(localTemp * 2.0);
                elevatedLand = lerp(elevatedLand, elevatedLand * lerp(0.82, 1.15, microNoise), forestMask * 0.4);
                // Desert dune ripple (hot+dry zones)
                float desertMicroMask = saturate((localTemp - 0.5) * 2.0) * saturate((1.0 - localMoist) * 1.5) * (1.0 - highBand);
                float3 duneLight = elevatedLand * 1.12;
                float3 duneDark = elevatedLand * 0.85;
                elevatedLand = lerp(elevatedLand, lerp(duneDark, duneLight, microNoise2), desertMicroMask * 0.35);
                // Tundra lichen speckle (cold zones)
                float tundraMask = saturate((0.35 - localTemp) * 4.0) * (1.0 - snowAmount) * (1.0 - highBand);
                // Savanna/grassland transition zone (warm, moderate moisture)
                float savannaMask = saturate(localTemp - 0.4) * saturate(localMoist) * saturate(1.0 - localMoist) * (1.0 - highBand) * 2.0;
                float3 savannaColor = lerp(float3(0.65, 0.60, 0.30), float3(0.55, 0.50, 0.25), microNoise);
                elevatedLand = lerp(elevatedLand, savannaColor, savannaMask * 0.35);
                float3 lichenColor = lerp(float3(0.50, 0.55, 0.42), float3(0.60, 0.58, 0.50), microNoise);
                elevatedLand = lerp(elevatedLand, lichenColor, tundraMask * 0.25);

                float3 normalAlbedo = lerp(oceanColor, elevatedLand, edge);

                // Normal rivers (moisture-gated, not on mountains)
                float normalRiverMask = riverMask * saturate((localMoist - 0.20) * 2.0)
                                      * saturate(1.0 - mtnBand * 0.8);
                normalAlbedo = lerp(normalAlbedo, float3(0.10, 0.25, 0.45), saturate(normalRiverMask));

                // Lakes (normal)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9));
                float lakeMask  = smoothstep(0.72, 0.78, lakeNoise)
                                * saturate((localMoist - 0.6) * 2.5) * step(0.5, edge);
                normalAlbedo = lerp(normalAlbedo, float3(0.12, 0.30, 0.50), saturate(lakeMask));

                // ---- Ice caps / Frozen world logic ----
                // For frozen climates (temp < 0.15), ice covers much of the surface
                float frozenWorld = saturate((0.15 - _Temperature) * 8.0); // 1 when temp≈0, 0 when temp>0.15

                // Polar ice caps (scale with temperature as before)
                float capStart = lerp(0.55, 1.10, _Temperature);
                float capMask = smoothstep(capStart - 0.08, capStart + 0.08, latitude);
                float iceEdgeNoise = noise3D(objNorm * 6.0 + float3(11.1, 5.5, 22.2));
                capMask *= smoothstep(capStart - 0.12, capStart + 0.04, latitude + (iceEdgeNoise - 0.5) * 0.15);

                // Frozen world: additional ice coverage across ALL land + ocean
                float frozenIceNoise = noise3D(objNorm * 4.0 + float3(55.5, 11.1, 33.3));
                // Noise-based ice patches that cover most of the surface in frozen worlds
                float frozenIceMask = smoothstep(0.20, 0.45, frozenIceNoise) * frozenWorld;
                // Combine: polar caps + frozen-world general ice
                float totalIceMask = saturate(capMask + frozenIceMask);

                // Ice colors: icy blue tint for frozen worlds, white for caps
                float3 icyBlue     = float3(0.72, 0.82, 0.95); // icy blue for frozen poles
                float3 iceWhite    = float3(0.90, 0.93, 0.97); // standard ice
                // Frozen worlds: poles get icy blue, mid-latitudes get whiter ice
                float polarBlueMask = smoothstep(0.5, 0.85, latitude) * frozenWorld;
                float3 iceColor = lerp(iceWhite, icyBlue, polarBlueMask);
                // Ocean sea-ice is slightly more blue
                iceColor = lerp(iceColor * 0.92, iceColor, edge);

                normalAlbedo = lerp(normalAlbedo, iceColor, saturate(totalIceMask));

                // ==============================================================
                //  INFERNAL WORLD colors (style ≈ 0.5)
                // ==============================================================
                float3 hellLand = GetHellLandColor(_Moisture, terrainHeight);

                // Volcanic glow on highlands
                float3 volcanicGlow = float3(0.60, 0.15, 0.03);
                float glowAmount = mtnBand * 0.4 + highBand * 0.15;
                float glowPulse = sin(timeVal * 1.5 + elevNoise * 12.0) * 0.5 + 0.5;
                glowAmount *= lerp(0.7, 1.0, glowPulse);
                hellLand = lerp(hellLand, volcanicGlow, glowAmount);

                float3 hellOcean = GetLavaOceanColor(samplePos, timeVal);
                float3 hellAlbedo = lerp(hellOcean, hellLand, edge);

                // Lava rivers
                float lavaRiverMask = riverMask * saturate(1.0 - mtnBand * 0.5);
                float3 lavaRiverColor = float3(0.90, 0.30, 0.04);
                float riverPulse = sin(timeVal * 2.0 + riverNoise1 * 8.0) * 0.5 + 0.5;
                lavaRiverColor = lerp(lavaRiverColor, float3(1.0, 0.65, 0.15), riverPulse * 0.4);
                hellAlbedo = lerp(hellAlbedo, lavaRiverColor, saturate(lavaRiverMask));

                // Volcanic vent spots
                float ventNoise = noise3D(samplePos * 10.0 + float3(44.4, 88.8, 22.2));
                float ventMask = smoothstep(0.78, 0.84, ventNoise) * edge;
                float ventFlicker = sin(timeVal * 4.0 + ventNoise * 20.0) * 0.5 + 0.5;
                float3 ventColor = lerp(float3(0.85, 0.25, 0.02), float3(1.0, 0.60, 0.10), ventFlicker);
                hellAlbedo = lerp(hellAlbedo, ventColor, ventMask * 0.8);

                // Lava lakes
                float hellLakeMask = smoothstep(0.70, 0.77, lakeNoise) * step(0.5, edge);
                float3 lavaLakeColor = float3(0.80, 0.22, 0.03);
                float lakePulse = sin(timeVal * 1.2 + lakeNoise * 6.0) * 0.5 + 0.5;
                lavaLakeColor = lerp(lavaLakeColor, float3(1.0, 0.55, 0.12), lakePulse * 0.5);
                hellAlbedo = lerp(hellAlbedo, lavaLakeColor, hellLakeMask);

                // Ash polar caps
                float ashCapStart = 0.78;
                float ashCapMask = smoothstep(ashCapStart - 0.06, ashCapStart + 0.06, latitude);
                float ashEdgeNoise = noise3D(objNorm * 5.0 + float3(33.3, 66.6, 99.9));
                ashCapMask *= smoothstep(ashCapStart - 0.10, ashCapStart + 0.03, latitude + (ashEdgeNoise - 0.5) * 0.12);
                float3 ashColor = lerp(float3(0.22, 0.18, 0.16), float3(0.30, 0.25, 0.22), edge);
                hellAlbedo = lerp(hellAlbedo, ashColor, saturate(ashCapMask));

                // ==============================================================
                //  DEMONIC WORLD colors (style ≈ 1.0, built on top of infernal)
                //  Much darker land, brighter/more intense lava, deeper reds
                // ==============================================================

                // Near-black land with blood-red undertone
                float3 demonLand = float3(0.06, 0.03, 0.02); // near-black
                float3 bloodTint = float3(0.25, 0.04, 0.02);  // deep blood red
                // Subtle variation from elevation noise
                demonLand = lerp(demonLand, bloodTint, elevNoise * 0.5);
                // Cracks of hellfire visible through the dark crust
                float crackNoise = noise3D(samplePos * 7.0 + float3(66.6, 13.1, 99.9));
                float crackMask = smoothstep(0.46, 0.50, crackNoise) * edge;
                float3 hellfireColor = float3(1.0, 0.20, 0.02); // intense hellfire red
                float crackPulse = sin(timeVal * 3.0 + crackNoise * 15.0) * 0.5 + 0.5;
                hellfireColor = lerp(hellfireColor, float3(1.0, 0.50, 0.05), crackPulse * 0.3);
                demonLand = lerp(demonLand, hellfireColor, crackMask * 0.7);

                // Brighter, more intense lava ocean
                float3 demonOcean = GetLavaOceanColor(samplePos, timeVal);
                // Boost brightness and shift toward pure red
                demonOcean = demonOcean * 1.4 + float3(0.15, 0.0, 0.0);

                float3 demonAlbedo = lerp(demonOcean, demonLand, edge);

                // Demonic lava rivers — brighter, wider presence
                float demonRiverMask = riverMask * saturate(1.0 - mtnBand * 0.3);
                float3 demonRiverColor = float3(1.0, 0.18, 0.02); // intense red-orange
                float demonRiverPulse = sin(timeVal * 2.5 + riverNoise1 * 10.0) * 0.5 + 0.5;
                demonRiverColor = lerp(demonRiverColor, float3(1.0, 0.55, 0.08), demonRiverPulse * 0.4);
                demonAlbedo = lerp(demonAlbedo, demonRiverColor, saturate(demonRiverMask));

                // More volcanic vents, brighter
                float demonVentMask = smoothstep(0.72, 0.80, ventNoise) * edge; // lower threshold = more vents
                float3 demonVentColor = lerp(float3(1.0, 0.15, 0.01), float3(1.0, 0.45, 0.05), ventFlicker);
                demonAlbedo = lerp(demonAlbedo, demonVentColor, demonVentMask * 0.9);

                // Lava lakes — brighter
                float3 demonLavaLake = float3(1.0, 0.25, 0.02);
                demonLavaLake = lerp(demonLavaLake, float3(1.0, 0.50, 0.08), lakePulse * 0.4);
                demonAlbedo = lerp(demonAlbedo, demonLavaLake, hellLakeMask);

                // No ash caps — pure scorched nothing at poles
                // (intentionally no cap blending for demonic)

                // ==============================================================
                //  Blend: normal → infernal → demonic
                // ==============================================================
                // First blend normal to infernal, then infernal to demonic
                float3 albedo = lerp(normalAlbedo, hellAlbedo, infernal);
                albedo = lerp(albedo, demonAlbedo, demonic);

                // ==============================================================
                //  Cloud shadows on surface
                // ==============================================================
                float3 cloudSamplePos = objNorm * 3.0;
                float cloudAngle = timeVal * 0.05;
                float cCos = cos(cloudAngle); float cSin = sin(cloudAngle);
                cloudSamplePos.xz = float2(
                    cloudSamplePos.x * cCos - cloudSamplePos.z * cSin,
                    cloudSamplePos.x * cSin + cloudSamplePos.z * cCos);
                float cloudShadow = fbm(cloudSamplePos + float3(5.5, 2.2, 8.8));
                float cloudShadowMask = smoothstep(0.35, 0.55, cloudShadow) * 0.2 * (1.0 - infernal);
                albedo *= (1.0 - cloudShadowMask);

                // ==============================================================
                //  Lighting (property-driven sun direction + color)
                // ==============================================================
                float3 lightDir = normalize(-_SunDirection.xyz);
                float3 sunCol   = _SunColor.rgb * _SunIntensity;

                float NdotL   = dot(normal, lightDir);
                float diffuse = saturate(NdotL * 0.6 + 0.4);

                float ambient = saturate(normal.y * -0.15 + 0.20);
                float lighting = diffuse + ambient;

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 halfVec = normalize(lightDir + viewDir);
                float specPow = lerp(48.0, 16.0, infernal);
                float spec = pow(saturate(dot(normal, halfVec)), specPow)
                           * (1.0 - edge)
                           * lerp(0.35, 0.5, infernal);

                // Sun glint on oceans — tight specular for "NASA photo" look
                float oceanGlint = pow(saturate(dot(normal, halfVec)), 256.0)
                                 * (1.0 - edge) * (1.0 - infernal) * 1.8;
                float glintRipple = noise3D(samplePos * 40.0 + float3(timeVal * 0.5, 0, 0));
                oceanGlint *= lerp(0.6, 1.4, glintRipple);

                float3 finalColor = albedo * lighting * sunCol + spec * sunCol + oceanGlint * sunCol;

                // ==============================================================
                //  Emissive additions (infernal + demonic)
                // ==============================================================
                float3 lavaEmit = GetLavaOceanColor(samplePos, timeVal);

                // Infernal emissives (scale with infernal blend)
                float oceanEmissive = (1.0 - edge) * infernal;
                finalColor += lavaEmit * oceanEmissive * 0.4;
                finalColor += ventColor * ventMask * infernal * 0.6;
                finalColor += lavaRiverColor * saturate(lavaRiverMask) * infernal * 0.35;
                finalColor += lavaLakeColor * hellLakeMask * infernal * 0.3;

                // Demonic extra emissives (brighter, on top of infernal)
                finalColor += demonOcean * (1.0 - edge) * demonic * 0.3;
                finalColor += hellfireColor * crackMask * demonic * 0.8;
                finalColor += demonRiverColor * saturate(demonRiverMask) * demonic * 0.5;
                finalColor += demonVentColor * demonVentMask * demonic * 0.7;
                finalColor += demonLavaLake * hellLakeMask * demonic * 0.4;

                // ==============================================================
                //  Terminator scattering (warm band at day/night boundary)
                // ==============================================================
                float terminatorMask = 1.0 - smoothstep(0.0, 0.18, abs(NdotL));
                float3 terminatorColor = float3(0.85, 0.35, 0.12);
                finalColor += terminatorColor * terminatorMask * 0.15 * (1.0 - infernal);

                // ==============================================================
                //  Night-side city lights
                // ==============================================================
                float nightMask = smoothstep(0.0, -0.1, NdotL);
                float cityNoise1 = noise3D(samplePos * 45.0 + float3(88.1, 22.4, 55.7));
                float cityNoise2 = noise3D(samplePos * 80.0 + float3(14.3, 67.8, 39.2));
                float coastProximity = 1.0 - smoothstep(0.0, 0.15, abs(n - _LandThreshold));
                float midLatitude = 1.0 - smoothstep(0.0, 0.4, abs(latitude - 0.4));
                float cityDensity = saturate(_CivCount / 8.0);
                float cityThreshold = lerp(0.88, 0.72, cityDensity);
                float cityMask = step(cityThreshold, cityNoise1) * edge;
                cityMask += step(cityThreshold + 0.03, cityNoise2) * edge
                          * (coastProximity * 0.5 + midLatitude * 0.3);
                cityMask = saturate(cityMask) * nightMask * (1.0 - infernal);
                float3 cityLightColor = float3(1.0, 0.85, 0.55);
                float cityFlicker = lerp(0.8, 1.0, noise3D(samplePos * 100.0 + float3(timeVal * 2.0, 0, 0)));
                finalColor += cityLightColor * cityMask * 0.12 * cityFlicker;

                // ==============================================================
                //  Atmosphere rim glow
                // ==============================================================
                float fresnel = 1.0 - saturate(dot(normal, viewDir));

                // Infernal rim: red-orange
                float infernalRimMask = pow(fresnel, 3.0) * infernal;
                float3 infernalRimColor = lerp(float3(0.70, 0.12, 0.02), float3(0.90, 0.35, 0.05),
                                               sin(timeVal * 0.8) * 0.5 + 0.5);
                finalColor += infernalRimColor * infernalRimMask * 0.6;

                // Demonic rim: deeper, more intense blood-red with faster pulse
                float demonicRimMask = pow(fresnel, 2.5) * demonic; // wider rim
                float3 demonicRimColor = lerp(float3(0.80, 0.05, 0.01), float3(1.0, 0.15, 0.02),
                                              sin(timeVal * 1.5) * 0.5 + 0.5);
                finalColor += demonicRimColor * demonicRimMask * 0.8;

                // ==============================================================
                //  Frozen world: subtle icy blue rim glow
                // ==============================================================
                float frozenRimMask = pow(fresnel, 4.0) * frozenWorld * (1.0 - infernal);
                float3 frozenRimColor = float3(0.55, 0.70, 0.95);
                // Old analytic atmosphere rim removed — handled by separate atmosphere shell
                finalColor += frozenRimColor * frozenRimMask * 0.25;

                // ==============================================================
                //  Polar aurora (night side, high latitude, cold worlds)
                // ==============================================================
                float auroraMask = smoothstep(0.65, 0.85, latitude) * nightMask * (1.0 - infernal);
                float auroraStrength = saturate((0.5 - _Temperature) * 2.5);
                float auroraNoise = noise3D(float3(objNorm.x * 8.0, objNorm.z * 8.0, timeVal * 0.3));
                float auroraCurtain = smoothstep(0.35, 0.55, auroraNoise);
                float3 auroraColor = lerp(float3(0.15, 0.85, 0.35), float3(0.30, 0.45, 0.90),
                    noise3D(float3(objNorm.xz * 4.0, timeVal * 0.15)));
                auroraColor = lerp(auroraColor, float3(0.55, 0.20, 0.80),
                    smoothstep(0.6, 0.8, auroraNoise) * 0.3);
                finalColor += auroraColor * auroraMask * auroraCurtain * auroraStrength * 0.15;

                return float4(finalColor, 1.0);
            }

            ENDHLSL
        }

        // =====================================================================
        //  Depth pre-pass — required for HDRP forward rendering
        // =====================================================================
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Must match main pass CBUFFER layout exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float4 _SunDirection;
                float4 _SunColor;
                float _SunIntensity;
                float _CivCount;
            CBUFFER_END

            // Inline noise for displacement (same as main pass)
            float hash31_d(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float noise3D_d(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31_d(i); float n100 = hash31_d(i + float3(1,0,0));
                float n010 = hash31_d(i + float3(0,1,0)); float n110 = hash31_d(i + float3(1,1,0));
                float n001 = hash31_d(i + float3(0,0,1)); float n101 = hash31_d(i + float3(1,0,1));
                float n011 = hash31_d(i + float3(0,1,1)); float n111 = hash31_d(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),
                            lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }
            float fbm_d(float3 p)
            {
                float v=0; float a=0.5; float fr=1;
                for(int i=0;i<4;i++){v+=a*noise3D_d(p*fr);fr*=2;a*=0.5;}
                return v;
            }
            float GetDisp(float3 objNorm)
            {
                float3 sp = objNorm * _LandScale;
                float n = fbm_d(sp + float3(42.3,17.1,83.7));
                float edge = smoothstep(_LandThreshold-0.12,_LandThreshold+0.12,n);
                float elev = fbm_d(sp*1.5+float3(99.1,55.3,12.7));
                return edge*elev*_DisplacementScale;
            }

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float d = GetDisp(n);
                float3 displaced = n * (r * (1.0 + d));
                float3 worldPos = TransformObjectToWorld(displaced);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragDepth(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // =====================================================================
        //  Shadow caster — lets the sphere cast shadows if needed
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_DOTS_INSTANCING_ENABLED

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Must match main pass CBUFFER layout exactly for SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float _LandScale;
                float _LandThreshold;
                float _Temperature;
                float _Moisture;
                float _Elevation;
                float4 _BiomeTint;
                float _DesertFactor;
                float _TropicalFactor;
                float _SnowFactor;
                float _MapStyle;
                float _DetailScale;
                float _DetailStrength;
                float4 _AtmosphereColor;
                float _AtmospherePower;
                float _AtmosphereRadius;
                float _DisplacementScale;
                float4 _SunDirection;
                float4 _SunColor;
                float _SunIntensity;
                float _CivCount;
            CBUFFER_END

            float hash31_s(float3 p){p=frac(p*float3(0.1031,0.1030,0.0973));p+=dot(p,p.yxz+33.33);return frac((p.x+p.y)*p.z);}
            float noise3D_s(float3 p){float3 i=floor(p);float3 f=frac(p);f=f*f*(3.0-2.0*f);
                return lerp(lerp(lerp(hash31_s(i),hash31_s(i+float3(1,0,0)),f.x),lerp(hash31_s(i+float3(0,1,0)),hash31_s(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(hash31_s(i+float3(0,0,1)),hash31_s(i+float3(1,0,1)),f.x),lerp(hash31_s(i+float3(0,1,1)),hash31_s(i+float3(1,1,1)),f.x),f.y),f.z);}
            float fbm_s(float3 p){float v=0;float a=0.5;float fr=1;for(int i=0;i<4;i++){v+=a*noise3D_s(p*fr);fr*=2;a*=0.5;}return v;}
            float GetDispS(float3 n){float3 sp=n*_LandScale;float e=smoothstep(_LandThreshold-0.12,_LandThreshold+0.12,fbm_s(sp+float3(42.3,17.1,83.7)));
                return e*fbm_s(sp*1.5+float3(99.1,55.3,12.7))*_DisplacementScale;}

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float r = max(1e-5, length(input.positionOS.xyz));
                float3 n = input.positionOS.xyz / r;
                float3 displaced = n * (r * (1.0 + GetDispS(n)));
                float3 worldPos = TransformObjectToWorld(displaced);
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }

    Fallback "Hidden/InternalErrorShader"
}
