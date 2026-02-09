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
            _JungleFactor("Jungle Factor", Range(0,1)) = 0.0
            _SnowFactor("Snow Factor", Range(0,1)) = 0.0
            _DetailScale("Detail Scale", Float) = 18.0
            _DetailStrength("Detail Strength", Range(0,1)) = 0.18
            _AtmosphereColor("Atmosphere Color", Color) = (0.62,0.78,0.95,1)
            _AtmospherePower("Atmosphere Power", Range(0.5,6)) = 3.5
            _MapStyle("Map Style", Range(0, 1)) = 0.0
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
                float _JungleFactor;
                float _SnowFactor;
                float _MapStyle;  // 0 = normal, 1 = infernal/demonic
                     float _DetailScale;
                     float _DetailStrength;
                     float4 _AtmosphereColor;
                     float _AtmospherePower;
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
            //  Vertex
            // -----------------------------------------------------------------
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;

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
                float3 landColor  = GetLandColor(_Temperature, _Moisture);
                float3 oceanColor = GetOceanColor(_Temperature);

                // Apply biome tint + desert/jungle overlays driven by CPU-side factors
                // Desert pushes colors toward sandy tones, jungle towards darker saturated green
                float desertAmt = saturate(_DesertFactor);
                float jungleAmt = saturate(_JungleFactor);
                    // Suppress biome overlays on infernal/demonic worlds
                    float hellBlend = saturate(infernal + demonic);
                    desertAmt *= (1.0 - hellBlend);
                    jungleAmt *= (1.0 - hellBlend);

                // Desert tint (sandy) and jungle tint (deep green)
                float3 desertTint = float3(0.85, 0.70, 0.45);
                float3 jungleTint = float3(0.08, 0.45, 0.12);

                // Blend landColor toward desert/jungle based on their amounts
                landColor = lerp(landColor, desertTint, desertAmt * 0.9);
                landColor = lerp(landColor, jungleTint, jungleAmt * 0.9);
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

                // Subtly mix in the overall biome tint color (from C# computed blend)
                landColor = lerp(landColor, _BiomeTint.rgb, saturate((desertAmt + jungleAmt) * 0.5 + 0.15));

                // Elevation shading
                float3 highlandColor = lerp(landColor, float3(0.55, 0.50, 0.42), 0.6);
                float3 mountainColor = float3(0.58, 0.56, 0.52);
                float3 snowPeakColor = float3(0.92, 0.93, 0.96);

                float3 elevatedLand = landColor * lerp(0.85, 1.0, midBand);
                elevatedLand = lerp(elevatedLand, highlandColor, highBand);
                elevatedLand = lerp(elevatedLand, mountainColor, mtnBand);
                // Snow amount includes shader-controlled snow factor to allow CPU tweaks
                float snowAmount = snowBand * saturate(1.0 - _Temperature * 1.3 + _SnowFactor * 0.8);
                elevatedLand = lerp(elevatedLand, snowPeakColor, snowAmount);

                float3 normalAlbedo = lerp(oceanColor, elevatedLand, edge);

                // Normal rivers (moisture-gated, not on mountains)
                float normalRiverMask = riverMask * saturate((_Moisture - 0.20) * 2.0)
                                      * saturate(1.0 - mtnBand * 0.8);
                normalAlbedo = lerp(normalAlbedo, float3(0.10, 0.25, 0.45), saturate(normalRiverMask));

                // Lakes (normal)
                float lakeNoise = noise3D(samplePos * 12.0 + float3(7.7, 3.3, 9.9));
                float lakeMask  = smoothstep(0.72, 0.78, lakeNoise)
                                * saturate((_Moisture - 0.6) * 2.5) * step(0.5, edge);
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
                //  Lighting
                // ==============================================================
                // use perturbed normal computed earlier
                // (previous plain normal calculation removed to retain detail perturbation)
                float3 lightDir = normalize(float3(0.5, 0.7, -0.3));

                float NdotL   = dot(normal, lightDir);
                float diffuse = saturate(NdotL * 0.6 + 0.4);

                float ambient = saturate(normal.y * -0.15 + 0.20);
                float lighting = diffuse + ambient;

                float3 viewDir = normalize(-input.positionWS);
                float3 halfVec = normalize(lightDir + viewDir);
                float specPow = lerp(48.0, 16.0, infernal);
                float spec = pow(saturate(dot(normal, halfVec)), specPow)
                           * (1.0 - edge)
                           * lerp(0.35, 0.5, infernal);

                float3 finalColor = albedo * lighting + spec;

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
                    // --------------------------------------------------------------
                    //  Atmosphere scattering rim (simple analytic approach)
                    //  Stronger on limb (high fresnel) and modulated by temperature/hellBlend
                    // --------------------------------------------------------------
                    float atmos = pow(fresnel, _AtmospherePower) * (1.0 - hellBlend);
                    float3 atmosCol = _AtmosphereColor.rgb * atmos * 0.7;
                    finalColor = lerp(finalColor, finalColor + atmosCol, atmos * 0.9);
                finalColor += frozenRimColor * frozenRimMask * 0.25;

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

            Varyings vertDepth(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
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

            Varyings vertShadow(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
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
