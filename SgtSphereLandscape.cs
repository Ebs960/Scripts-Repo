using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using SpaceGraphicsToolkit.Cloud;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component allows you to create a landscape with a spherical topology.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Sphere Landscape")]
	public class SgtSphereLandscape : SgtLandscape
	{
		/// <summary>The base radius of the landscape in local space.</summary>
		public float Radius { set { radius = value; } get { return radius; } } [SerializeField] private float radius = 50;

		/// <summary>If you want cloud shadows to appear on the surface of the planet, specify them here.</summary>
		public SgtCloud CloudShadow { set { cloudShadow = value; } get { return cloudShadow; } } [SerializeField] protected SgtCloud cloudShadow;

		/// <summary>The albedo texture given to the landscape.
		/// 
		/// None = White.
		/// 
		/// NOTE: This texture must use equirectangular (cylindrical) projection.</summary>
		public Texture2D AlbedoTex { set { albedoTex = value; } get { return albedoTex; } } [SerializeField] private Texture2D albedoTex;

		/// <summary>The landscape's <b>Radius</b> will be displaced by this heightmap.
		/// 
		/// NOTE: This texture must have <b>Read/Write</b> enabled.
		/// NOTE: This texture must be <b>Single Channel</b>, and use the <b>Alpha8</b> or <b>R8</b> or <b>R16</b> format.
		/// NOTE: This texture must use equirectangular (cylindrical) projection.</summary>
		public Texture2D HeightTex { set { heightTex = value; } get { return heightTex; } } [SerializeField] private Texture2D heightTex;

		/// <summary>This allows you to set where in the heightmap's height data is 0.
		/// 
		/// 0.0 = Displacement between 0 and <b>HeightRange</b>.
		/// 0.5 = Displacement between -<b>HeightRange/2</b> and <b>HeightRange/2</b>.
		/// 1.0 = Displacement between -<b>HeightRange</b> and 0.</summary>
		public float HeightMidpoint { set { heightMidpoint = value; } get { return heightMidpoint; } } [SerializeField] [Range(0.0f, 1.0f)] private float heightMidpoint;

		/// <summary>The maximum difference in height displacement from the heightmap.</summary>
		public float HeightRange { set { heightRange = value; } get { return heightRange; } } [SerializeField] private float heightRange = 10.0f;

		/// <summary>This allows you to adjust how deep the heightmap penetrates into the landscape texture when it gets colored.</summary>
		public float Strata { set { strata = value; } get { return strata; } } [SerializeField] protected float strata = 1.0f;

		[System.NonSerialized]
		private Material blitMaterial;

		[System.NonSerialized]
		private HeightData heightData;

		[System.NonSerialized]
		private TopologyData topologyData;

		[BurstCompile]
		private struct PointJob : IJob
		{
			[ReadOnly] public double Radius;

			[WriteOnly] public NativeArray<double3> Directions;
			[WriteOnly] public NativeArray<double4> Coords;
			[WriteOnly] public NativeArray<double4> DataA;
			[WriteOnly] public NativeArray<double4> DataB;

			public NativeArray<double3> Points;

			public void Execute()
			{
				for (var i = 0; i < Points.Length; i++)
				{
					var m = math.length(Points[i]);
					var d = m > 0.0 ? Points[i] / m : new double3(0.0, 0.0, 1.0);
					var t = math.smoothstep(0.0f, 1.0f, math.saturate((math.abs(d.y) - 0.7f) * 30.0f));
					var c = CalculateCoord(d);

					Points[i] = d * Radius;
					Directions[i] = d;

					Coords[i] = new double4(c.x, c.y, c.z, c.w);
					DataA[i] = new double4(c.z, c.w * 2.0, t, 1.0 - t);
					DataB[i] = new double4(0.0, math.sign(d.y), 0.0, 0.0);
				}
			}
		}

		[BurstCompile]
		private struct TriangleJob : IJob
		{
			[ReadOnly] public double               Radius;
			[ReadOnly] public double3              PositionA;
			[ReadOnly] public double3              PositionB;
			[ReadOnly] public double3              PositionC;
			[ReadOnly] public NativeArray<double3> Weights;

			[WriteOnly] public NativeArray<double3> Points;
			[WriteOnly] public NativeArray<double3> Directions;
			[WriteOnly] public NativeArray<double4> Coords;
			[WriteOnly] public NativeArray<double4> DataA;
			[WriteOnly] public NativeArray<double4> DataB;

			public void Execute()
			{
				for (var i = 0; i < Directions.Length; i++)
				{
					var w = Weights[i];
					var p = PositionA * w.x + PositionB * w.y + PositionC * w.z;
					var d = math.normalize(p);
					var t = math.smoothstep(0.0f, 1.0f, math.saturate((math.abs(d.y) - 0.7f) * 30.0f));
					var c = CalculateCoord(d);

					Points[i] = d * Radius;
					Directions[i] = d;

					Coords[i] = new double4(c.x, c.y, c.z, c.w);
					DataA[i] = new double4(c.z, c.w * 2.0, 1.0 - t, t);
					DataB[i] = new double4(0.0, math.sign(d.y), 0.0, 0.0);
				}
			}
		}

		[BurstCompile]
		public struct BaseJob : IJob
		{
			[ReadOnly] public int2                HeightSize;
			[ReadOnly] public double2             HeightRange;
			[ReadOnly] public NativeArray<byte  > HeightData08;
			[ReadOnly] public NativeArray<ushort> HeightData16;

			[ReadOnly] public NativeArray<double4> DataA;

			[WriteOnly] public NativeArray<double> Heights;

			public void Execute()
			{
				if (HeightData16.Length > 0)
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						Heights[i] = HeightRange.x + HeightRange.y * Sample_Cubic_WrapX(HeightData16, HeightSize, DataA[i].xy * HeightSize);
					}
				}
				else if (HeightData08.Length > 0)
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						Heights[i] = HeightRange.x + HeightRange.y * Sample_Cubic_WrapX(HeightData08, HeightSize, DataA[i].xy * HeightSize);
					}
				}
				else
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						Heights[i] = 0.0;
					}
				}
			}
		}

		protected override void UpdateBatchBeforeRender(Batch batch)
		{
			if (cloudShadow != null && cloudShadow.GeneratedTexture != null)
			{
				batch.Properties.SetTexture(_SGT_CloudTex, cloudShadow.GeneratedTexture);
				batch.Properties.SetMatrix(_SGT_CloudMatrix, cloudShadow.GeneratedMatrix);
				batch.Properties.SetVector(_SGT_CloudOpacity, cloudShadow.GeneratedOpacity);
				batch.Properties.SetFloat(_SGT_CloudWarp, cloudShadow.Warp);
			}
		}

		public override int CalculateLodDepth(float triangleSize)
		{
			var scale = 2 * triangleSize;

			if (scale > 0.0)
			{
				var depth = (int)(math.log(radius / scale) / math.log(1.5));

				return math.clamp(depth, 0, 120);
			}

			return 0;
		}

		public static SgtSphereLandscape Create(int layer = 0, Transform parent = null)
		{
			return Create(layer, parent, Vector3.zero, Quaternion.identity, Vector3.one);
		}

		public static SgtSphereLandscape Create(int layer, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
		{
			return CW.Common.CwHelper.CreateGameObject("Landscape", layer, parent, localPosition, localRotation, localScale).AddComponent<SgtSphereLandscape>();
		}

		public override float GetApproximateWorldDistance(Vector3 worldPoint)
		{
			var localSurface = Vector3.Normalize(transform.InverseTransformPoint(worldPoint)) * radius;
			var worldSurface = transform.TransformPoint(localSurface);

			return Vector3.Distance(worldSurface, worldPoint);
		}

		private static double Asin(double x)
		{
			return x + (x * x * x / 6.0) + ((3.0 * x * x * x * x * x) / 40.0);
		}

		private static double4 CalculateCoord(double3 position)
		{
			var d = math.normalize(position);
			var u = math.atan2(d.z, d.x) / (math.PI_DBL * 2.0) + 0.5;
			var v = Asin(d.y) / math.PI_DBL + 0.5;
			//var v = d.y * 0.3 + 0.5;

			return new double4(u, v * 0.5f, d.xz * 0.25);
		}

		private static double4 CalculateCoord2(double3 position)
		{
			var d = math.normalize(position);
			var u = math.atan2(-d.z, -d.x) / (math.PI_DBL * 2.0);
			var v = Asin(d.y) / math.PI_DBL + 0.5;
			//var v = d.y * 0.3 + 0.5;

			return new double4(u, v * 0.5f, d.xz * 0.25);
		}

		protected override Material GetVisualBlitMaterial(PendingTriangle pending)
		{
			blitMaterial.SetVector(_CwPositionA, (Vector3)(float3)pending.Triangle.PositionA);
			blitMaterial.SetVector(_CwPositionB, (Vector3)(float3)pending.Triangle.PositionB);
			blitMaterial.SetVector(_CwPositionC, (Vector3)(float3)pending.Triangle.PositionC);

			var directionA = math.normalize(pending.Triangle.PositionA);
			var directionB = math.normalize(pending.Triangle.PositionB);
			var directionC = math.normalize(pending.Triangle.PositionC);
			var positionA  = directionA * radius;
			var positionB  = directionB * radius;
			var positionC  = directionC * radius;

			var triangleD = math.normalize(directionA + directionB + directionC);

			var coordA = triangleD.x > 0.0f ? CalculateCoord(pending.Triangle.PositionA.xyz) : CalculateCoord2(pending.Triangle.PositionA.xyz);
			var coordB = triangleD.x > 0.0f ? CalculateCoord(pending.Triangle.PositionB.xyz) : CalculateCoord2(pending.Triangle.PositionB.xyz);
			var coordC = triangleD.x > 0.0f ? CalculateCoord(pending.Triangle.PositionC.xyz) : CalculateCoord2(pending.Triangle.PositionC.xyz);

			var coordX = math.floor(coordA * globalTiling.x);
			var coordY = math.floor(coordA * globalTiling.y);
			var coordZ = math.floor(coordA * globalTiling.z);
			var coordW = math.floor(coordA * globalTiling.w);

			var coordXA = coordA * globalTiling.x - coordX;
			var coordXB = coordB * globalTiling.x - coordX;
			var coordXC = coordC * globalTiling.x - coordX;

			var coordYA = coordA * globalTiling.y - coordY;
			var coordYB = coordB * globalTiling.y - coordY;
			var coordYC = coordC * globalTiling.y - coordY;

			var coordZA = coordA * globalTiling.z - coordZ;
			var coordZB = coordB * globalTiling.z - coordZ;
			var coordZC = coordC * globalTiling.z - coordZ;

			var coordWA = coordA * globalTiling.w - coordW;
			var coordWB = coordB * globalTiling.w - coordW;
			var coordWC = coordC * globalTiling.w - coordW;

			blitMaterial.SetMatrix(_CwCoordX, new Matrix4x4((Vector4)(float4)coordXA, (Vector4)(float4)coordXB, (Vector4)(float4)coordXC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordY, new Matrix4x4((Vector4)(float4)coordYA, (Vector4)(float4)coordYB, (Vector4)(float4)coordYC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordZ, new Matrix4x4((Vector4)(float4)coordZA, (Vector4)(float4)coordZB, (Vector4)(float4)coordZC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordW, new Matrix4x4((Vector4)(float4)coordWA, (Vector4)(float4)coordWB, (Vector4)(float4)coordWC, default(Vector4)));
			blitMaterial.SetInt(_CwDepth, pending.Triangle.Depth);

			return blitMaterial;
		}

		protected override void Schedule(PendingTriangle pending)
		{
			var job = new TriangleJob();

			job.Radius    = radius;
			job.PositionA = pending.Triangle.PositionA;
			job.PositionB = pending.Triangle.PositionB;
			job.PositionC = pending.Triangle.PositionC;

			job.Weights    = VERTEX_WEIGHTS;
			job.Points     = pending.Points;
			job.Directions = pending.Directions;
			job.Coords     = pending.Coords;
			job.DataA      = pending.DataA;
			job.DataB      = pending.DataB;

			pending.Handle = job.Schedule();

			ScheduleBase(pending);
			ScheduleFeatures(pending);

			base.Schedule(pending);
		}

		private void SchedulePoints(PendingPoints pendingPoints)
		{
			var job = new PointJob();

			job.Radius = radius;

			job.Points     = pendingPoints.Points;
			job.Directions = pendingPoints.Directions;
			job.Coords     = pendingPoints.Coords;
			job.DataA      = pendingPoints.DataA;
			job.DataB      = pendingPoints.DataB;

			pendingPoints.Handle = job.Schedule(pendingPoints.Handle);
		}

		private void ScheduleBase(PendingPoints pending)
		{
			var baseJob = new BaseJob();

			baseJob.HeightSize   = heightData.Size;
			baseJob.HeightRange  = heightData.Range;
			baseJob.HeightData08 = heightData.Data08;
			baseJob.HeightData16 = heightData.Data16;

			baseJob.DataA   = pending.DataA;
			baseJob.Heights = pending.Heights;

			pending.Handle = baseJob.Schedule(pending.Handle);
		}

		public override double3 GetLocalPoint(double3 localPoint)
		{
			var pending = new PendingPoints(1);

			pending.Points[0] = localPoint;

			SchedulePoints(pending);
			ScheduleBase(pending);
			ScheduleFeatures(pending);

			pending.Handle.Complete();

			localPoint = pending.GetPosition(0);

			pending.Dispose();

			return localPoint;
		}

		protected override void Prepare()
		{
			var circ = 2.0 * math.PI_DBL * radius;
			var tile = math.max(math.round(circ / (double4)(float4)globalSizes), 1);

			globalTiling = (float4)tile;

			globalTilingNormalized = (float4)(tile / circ) * 1000.0f;

			base.Prepare();

			heightData.Create(heightTex, heightMidpoint, heightRange);

			topologyData.CreateSphere(heightTex, heightMidpoint, heightRange, radius, strata);

			blitMaterial = CW.Common.CwHelper.CreateTempMaterial("SgtSphereLandscape", "Hidden/SgtSphereLandscape");

			blitMaterial.SetTexture(_CwAlbedo, albedoTex);
			blitMaterial.SetVector(_CwAlbedoSize, albedoTex != null ? new Vector2(albedoTex.width, albedoTex.height) : Vector2.one);

			blitMaterial.SetTexture(_CwTopology, topologyData.Texture);
			blitMaterial.SetVector(_CwTopologySize, (Vector2)topologyData.Size);
			blitMaterial.SetVector(_CwTopologyData, (Vector3)topologyData.Data);

			blitMaterial.SetFloat(_CwRadius, radius);

			if (registeredBundle != null)
			{
				blitMaterial.SetTexture(_CwHeightTopologyAtlas, registeredBundle.HeightTopologyAtlas);
				blitMaterial.SetTexture(_CwMaskTopologyAtlas, registeredBundle.MaskTopologyAtlas);
				blitMaterial.SetTexture(_CwGradientAtlas, registeredBundle.GradientAtlas);
				blitMaterial.SetTexture(_CwDetailAtlas, registeredBundle.DetailAtlas);
			}

			var pointN = new double3( 0.0,  0.0,  1.0);
			var pointE = new double3(+1.0,  0.0,  0.0);
			var pointS = new double3( 0.0,  0.0, -1.0);
			var pointW = new double3(-1.0,  0.0,  0.0);
			var pointU = new double3( 0.0, +1.0,  0.0);
			var pointD = new double3( 0.0, -1.0,  0.0);

			topology.Add(new Triangle(pointU, pointW, pointS, 0, false));
			topology.Add(new Triangle(pointU, pointS, pointE, 0, false));
			topology.Add(new Triangle(pointU, pointE, pointN, 0, false));
			topology.Add(new Triangle(pointU, pointN, pointW, 0, false));

			topology.Add(new Triangle(pointD, pointS, pointW, 0, false));
			topology.Add(new Triangle(pointD, pointE, pointS, 0, false));
			topology.Add(new Triangle(pointD, pointN, pointE, 0, false));
			topology.Add(new Triangle(pointD, pointW, pointN, 0, false));

			blitMaterial.SetVector(_CwGlobalTiling, (float4)globalTiling);

			ApplyBlitVariables(blitMaterial);
		}

		protected override void OnEnable()
		{
			base.OnEnable();

			//SgtCommon.OnCalculateDistance += HandleCalculateDistance;
		}

		protected override void OnDisable()
		{
			base.OnDisable();

			//SgtCommon.OnCalculateDistance -= HandleCalculateDistance;
		}

		private void HandleCalculateDistance(Vector3 worldPosition, ref float distance)
		{
			var surfacePoint    = (float3)GetWorldPoint((float3)worldPosition);
			var surfaceDistance = math.distance(surfacePoint, worldPosition);

			if (surfaceDistance < distance)
			{
				distance = surfaceDistance;
			}
		}

		public override double3 GetWorldPivot(double3 worldPoint)
		{
			return (float3)transform.position;
		}

		protected override void Dispose()
		{
			base.Dispose();

			heightData.Dispose();
			topologyData.Dispose();

			DestroyImmediate(blitMaterial);
		}

		protected override JobHandle ScheduleUpdateTriangles(float detail, int maxSteps)
		{
			SchedulePoints(cameraPoints);
			ScheduleBase(cameraPoints);
			ScheduleFeatures(cameraPoints);
			ScheduleAdjustCameraPositions(cameraPoints);

			var job = new UpdateTrianglesJob();

			job.Topology        = topology;
			job.CameraPositions = cameraPositions;
			job.CameraDetailSq  = 1.0f / (detail * detail);
			job.CreateDiffs     = createDiffs;
			job.DeleteDiffs     = deleteDiffs;
			job.StatusDiffs     = statusDiffs;
			job.Triangles       = triangles;
			job.MaxDepth        = CalculateLodDepth(MinimumTriangleSize);
			job.MaxSteps        = maxSteps;

			job.Deform = DeformType.Sphere;
			job.Radius = Radius;

			return job.Schedule(cameraPoints.Handle);
		}

#if UNITY_EDITOR
	protected override void OnDrawGizmosSelected()
	{
		base.OnDrawGizmosSelected();

		if (IsActivated == false)
		{
			Gizmos.DrawWireSphere(Vector3.zero, Radius);
		}
	}

	protected override void UpdateGizmoTriangles()
	{
		for (var i = 0; i < gizmoTriangles.Count; i++)
		{
			var gizmoTriangle = gizmoTriangles[i];

			gizmoTriangle.c0 = math.normalize(gizmoTriangle.c0) * radius;
			gizmoTriangle.c1 = math.normalize(gizmoTriangle.c1) * radius;
			gizmoTriangle.c2 = math.normalize(gizmoTriangle.c2) * radius;

			gizmoTriangles[i] = gizmoTriangle;
		}
	}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtSphereLandscape))]
	public class SgtSphereLandscape_Editor : SgtLandscape_Editor
	{
		protected override void OnInspector()
		{
			base.OnInspector();

			SgtSphereLandscape tgt; SgtSphereLandscape[] tgts; GetTargets(out tgt, out tgts);

			var markForRebuild = false;

			Separator();

			Draw("radius", ref markForRebuild, "The base radius of the landscape in local space.");
			Draw("cloudShadow", ref markForRebuild, "If you want cloud shadows to appear on the surface of the planet, specify them here.");
			Draw("albedoTex", ref markForRebuild, "The albedo texture given to the landscape.\n\nNone = White.\n\nNOTE: This texture must use equirectangular (cylindrical) projection.");
			Draw("heightTex", ref markForRebuild, "The landscape's <b>Radius</b> will be displaced by this heightmap.\n\nNOTE: This texture must have <b>Read/Write</b> enabled.\n\nNOTE: This texture must be <b>Single Channel</b>, and use the <b>Alpha8</b> or <b>R8</b> or <b>R16</b> format.\n\nNOTE: This texture must use equirectangular (cylindrical) projection.");

			if (Any(tgts, t => t.HeightTex != null))
			{
				Draw("heightMidpoint", ref markForRebuild, "This allows you to set where in the heightmap's height data is 0.\n\n0.0 = Displacement between 0 and <b>HeightRange</b>.\n\n0.5 = Displacement between -<b>HeightRange/2</b> and <b>HeightRange/2</b>.\n\n1.0 = Displacement between -<b>HeightRange</b> and 0.");
				Draw("heightRange", ref markForRebuild, "The maximum difference in height displacement from the heightmap.");
				Draw("strata", ref markForRebuild, "This allows you to adjust how deep the heightmap penetrates into the landscape texture when it gets colored.");
			}

			Separator();

			if (markForRebuild == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}

		[UnityEditor.MenuItem("GameObject/CW/Space Graphics Toolkit/Landscape/Sphere", false, 10)]
		public static void CreateMenuItem()
		{
			var parent   = CW.Common.CwHelper.GetSelectedParent();
			var instance = SgtSphereLandscape.Create(parent != null ? parent.gameObject.layer : 0, parent);

			CW.Common.CwHelper.SelectAndPing(instance);
		}
	}
}
#endif