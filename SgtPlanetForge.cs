#if UNITY_2021_3 && !(UNITY_2021_3_0 || UNITY_2021_3_1 || UNITY_2021_3_2 || UNITY_2021_3_3 || UNITY_2021_3_4 || UNITY_2021_3_5 || UNITY_2021_3_6 || UNITY_2021_3_7 || UNITY_2021_3_8 || UNITY_2021_3_9 || UNITY_2021_3_10 || UNITY_2021_3_11 || UNITY_2021_3_12 || UNITY_2021_3_13 || UNITY_2021_3_14 || UNITY_2021_3_15 || UNITY_2021_3_16 || UNITY_2021_3_17)
	#define CW_HAS_NEW_FIND
#elif UNITY_2022_2 && !(UNITY_2022_2_0 || UNITY_2022_2_1 || UNITY_2022_2_2 || UNITY_2022_2_3 || UNITY_2022_2_4)
	#define CW_HAS_NEW_FIND
#elif UNITY_2023_1_OR_NEWER
	#define CW_HAS_NEW_FIND
#endif

using UnityEngine;
using CW.Common;
using SpaceGraphicsToolkit.LightAndShadow;
using SpaceGraphicsToolkit.Landscape;
using SpaceGraphicsToolkit.Sky;
using SpaceGraphicsToolkit.Cloud;
using SpaceGraphicsToolkit.Ocean;
using SpaceGraphicsToolkit.Volumetrics;

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit
{
	/// <summary>This script provides editor menu options to create planets.</summary>
	public static class SgtPlanetForge
	{
		private static Texture2D ExampleCloudDetail
		{
			get
			{
				var guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D Clouds30");

				foreach (var guid in guids)
				{
					var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);

					if (string.IsNullOrEmpty(path) == false)
					{
						var texture2D = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);

						if (texture2D != null)
						{
							return texture2D;
						}
					}
				}

				return null;
			}
		}

		[UnityEditor.MenuItem("GameObject/CW/Planet Forge/Planet (Radius = 500)", false, 10)]
		public static void CreateMenuItem500()
		{
			CheckForComponents();
			CreatePlanet(500);
		}

		[UnityEditor.MenuItem("GameObject/CW/Planet Forge/Planet (Radius = 5,000)", false, 10)]
		public static void CreateMenuItem5000()
		{
			CheckForComponents();
			CreatePlanet(5000);
		}

		[UnityEditor.MenuItem("GameObject/CW/Planet Forge/Planet (Radius = 5,000,000)", false, 10)]
		public static void CreateMenuItem5000000()
		{
			CheckForComponents();
			CreatePlanet(5000000);
		}

		private static void CreatePlanet(float radius)
		{
			var parent    = CwHelper.GetSelectedParent();
			var layer     = parent != null ? parent.gameObject.layer : 0;
			var root      = CwHelper.CreateGameObject("Planet", layer, parent, Vector3.zero, Quaternion.identity, Vector3.one);
			var landscape = SgtSphereLandscape.Create(layer, root.transform);
			var biome     = SgtLandscapeBiome.Create(layer, landscape.transform);
			var sky       = SgtSky.Create(layer, root.transform);
			var cloud     = SgtCloud.Create(layer, root.transform);
			var detail    = SgtCloudDetail.Create(layer, cloud.transform);
			var ocean     = SgtOcean.Create(layer, root.transform);
			var rays      = ocean.gameObject.AddComponent<SgtOceanRays>();
			var debris    = ocean.gameObject.AddComponent<SgtOceanDebris>();

			landscape.Radius      = radius - 5;
			landscape.CloudShadow = cloud;

			biome.Color = true;
			biome.Layers.Add(new SgtLandscapeBiome.SgtLandscapeBiomeLayer() { Enabled = true, Displace = true , HeightIndex = 0, HeightMidpoint = 0, HeightRange = 10.0f, GlobalSize = 10000, LocalTiling = new Vector2(1, 1), Strata = 1.0f });
			biome.Layers.Add(new SgtLandscapeBiome.SgtLandscapeBiomeLayer() { Enabled = true, Displace = false, HeightIndex = 0, HeightMidpoint = 0, HeightRange =  0.1f, GlobalSize = 50   , LocalTiling = new Vector2(1, 1), Strata = 0.2f });

			sky.Clouds          = cloud;
			sky.InnerMeshRadius = radius;
			sky.Height          = radius / 10.0f;
			sky.Lighting        = SgtLight.InstanceCount > 0;

			ocean.CloudShadow          = cloud;
			ocean.Radius               = radius;
			ocean.SurfaceDensity       = 0.2f;
			ocean.UnderwaterDensity    = 0.2f;
			ocean.UnderwaterExtinction = new Vector4(1.0f, 1.0f, 2.0f, 0.1f);

			cloud.CloudLayers.Add(new SgtCloud.CloudLayerType() { Height = 0.2f, Thickness = 0.3f, Density = 1.0f, Shadow = 2.0f, Shape = 1.0f });

			detail.CoverageTex = ExampleCloudDetail;
			detail.CarveCore   = 1.0f;
			detail.Channels    = new Vector4(1.3f, 1.0f, 1.0f, 1.0f);

			CwHelper.SelectAndPing(root);
		}

		private static void CheckForComponents()
		{
			CheckForVolumeManager();
			CheckForVolumeCamera();
			CheckForLights();
		}

		private static void CheckForVolumeManager()
		{
			if (SgtVolumeManager.Instances.Count == 0)
			{
				var vm = new GameObject("SgtVolumeManager").AddComponent<SgtVolumeManager>();

				Debug.Log("Your scene didn't contain the SgtVolumeManager component, so one was added.", vm);
			}
		}

		private static void CheckForVolumeCamera()
		{
			foreach (var camera in Camera.allCameras)
			{
				if (camera.GetComponent<SgtVolumeCamera>() != null)
				{
					return;
				}
			}

			if (Camera.main != null)
			{
				var vc = CwHelper.GetOrAddComponent<SgtVolumeCamera>(Camera.main.gameObject);

				Debug.Log("Your scene didn't contain the SgtVolumeManager component, so one was added.", vc);
			}
			else
			{
				Debug.Log("None of the cameras in your scene have the SgtVolumeCamera component, please add it to at least one so the planet atmosphere can be rendered.");
			}
		}

		private static void CheckForLights()
		{
			if (SgtLight.InstanceCount == 0)
			{
				if (TryAddLight(RenderSettings.sun) == true)
				{
					return;
				}

				var lights        = FindObjectsByType<Light>();
				var bestLight     = default(Light);
				var bestIntensity = default(float);

				foreach (var light in lights)
				{
					if (light.intensity > bestIntensity)
					{
						bestLight     = light;
						bestIntensity = light.intensity;
					}
				}

				if (TryAddLight(bestLight) == true)
				{
					return;
				}

				Debug.Log("None of the lights in your scene have the SgtLight component, please add it to at least one so the planet atmosphere can be lit.");
			}
		}

		private static bool TryAddLight(Light light)
		{
			if (light != null)
			{
				var sl = light.gameObject.AddComponent<SgtLight>();

				Debug.Log("Your scene lights contain the SgtLight component, so one was added.", sl);
			}

			return false;
		}

		public static T[] FindObjectsByType<T>()
			where T : Object
		{
#if CW_HAS_NEW_FIND
			return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
			return Object.FindObjectsOfType<T>();
#endif
		}
	}
}
#endif