using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component creates a flat landscape in the shape of a square, similar to Unity's built-in terrain system.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Square Landscape")]
	public class SgtSquareLandscape : SgtLandscape
	{
		/// <summary>The size of the landscape along the X and Z axes in local space.</summary>
		public int Size { set { size = value; } get { return size; } } [SerializeField] private int size = 1024;

		/// <summary>The albedo texture given to the landscape.
		/// 
		/// None = White.</summary>
		public Texture2D AlbedoTex { set { albedoTex = value; } get { return albedoTex; } } [SerializeField] private Texture2D albedoTex;

		/// <summary>The landscape's <b>Radius</b> will be displaced by this heightmap.
		/// 
		/// NOTE: This texture must have <b>Read/Write</b> enabled.
		/// NOTE: This texture must be <b>Single Channel</b>, and use the <b>Alpha8</b> or <b>R8</b> or <b>R16</b> format.</summary>
		public Texture2D HeightTex { set { heightTex = value; } get { return heightTex; } } [SerializeField] private Texture2D heightTex;

		/// <summary>This allows you to set where in the heightmap's height data is 0.
		/// 
		/// 0.0 = Displacement between 0 and <b>HeightRange</b>.
		/// 0.5 = Displacement between -<b>HeightRange/2</b> and <b>HeightRange/2</b>.
		/// 1.0 = Displacement between -<b>HeightRange</b> and 0.</summary>
		public float HeightMidpoint { set { heightMidpoint = value; } get { return heightMidpoint; } } [SerializeField] [Range(0.0f, 1.0f)] private float heightMidpoint;

		/// <summary>The maximum difference in height displacement from the heightmap.</summary>
		public float HeightRange { set { heightRange = value; } get { return heightRange; } } [SerializeField] private float heightRange = 10.0f;

		/// <summary>The height scale of the landscape when coloring it.</summary>
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
			[ReadOnly] public double Size;

			[WriteOnly] public NativeArray<double3> Directions;
			[WriteOnly] public NativeArray<double4> Coords;
			[WriteOnly] public NativeArray<double4> DataA;
			[WriteOnly] public NativeArray<double4> DataB;

			public NativeArray<double3> Points;

			public void Execute()
			{
				for (var i = 0; i < Directions.Length; i++)
				{
					var p = new double3(Points[i].x, 0.0, Points[i].z);
					var c = (p.xz / Size) + 0.5;

					Points[i] = p;
					Directions[i] = new double3(0.0, 1.0, 0.0);

					Coords[i] = new double4(c.x, c.y, 0.0, 0.0);
					DataA[i] = new double4(c.x, c.y, -1.0, -1.0);
					DataB[i] = new double4(0.0, 0.0, 0.0, 0.0);
				}
			}
		}

		[BurstCompile]
		private struct TriangleJob : IJob
		{
			[ReadOnly] public int                  Size;
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
				var delta  = math.floor(PositionA);
				var pointA = PositionA - delta;
				var pointB = PositionB - delta;
				var pointC = PositionC - delta;

				for (var i = 0; i < Directions.Length; i++)
				{
					var w = Weights[i];
					var p = pointA * w.x + pointB * w.y + pointC * w.z + delta;
					var c = p.xz / Size + 0.5;

					Points[i] = p;
					Directions[i] = new double3(0.0, 1.0, 0.0);

					Coords[i] = new double4(c.x, c.y, 0.0, 0.0);
					DataA[i] = new double4(c.x, c.y, -1.0, -1.0);
					DataB[i] = new double4(0.0, 0.0, 0.0, 0.0);
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
						Heights[i] = HeightRange.x + HeightRange.y * Sample_Cubic(HeightData16, HeightSize, HeightSize * DataA[i].xy);
					}
				}
				else if (HeightData08.Length > 0)
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						Heights[i] = HeightRange.x + HeightRange.y * Sample_Cubic(HeightData08, HeightSize, HeightSize * DataA[i].xy);
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
		}

		public override int CalculateLodDepth(float triangleSize)
		{
			var scale = 4 * triangleSize;

			if (scale > 0.0)
			{
				var depth = (int)(math.log(size / scale) / math.log(1.5));

				return math.clamp(depth, 0, 120);
			}

			return 0;
		}

		public static SgtSquareLandscape Create(int layer = 0, Transform parent = null)
		{
			return Create(layer, parent, Vector3.zero, Quaternion.identity, Vector3.one);
		}

		public static SgtSquareLandscape Create(int layer, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
		{
			return CW.Common.CwHelper.CreateGameObject("SgtSquareLandscape", layer, parent, localPosition, localRotation, localScale).AddComponent<SgtSquareLandscape>();
		}

		public override float GetApproximateWorldDistance(Vector3 worldPoint)
		{
			var localSurface = transform.InverseTransformPoint(worldPoint);

			localSurface.x = Mathf.Clamp(localSurface.x, -size * 0.5f, size * 0.5f);
			localSurface.y = 0.0f;
			localSurface.z = Mathf.Clamp(localSurface.z, -size * 0.5f, size * 0.5f);

			var worldSurface = transform.TransformPoint(localSurface);

			return Vector3.Distance(worldSurface, worldPoint);
		}

		private double2 CalculateCoord(double3 position, float size)
		{
			return position.xz / size;
		}

		protected override Material GetVisualBlitMaterial(PendingTriangle pending)
		{
			blitMaterial.SetVector(_CwPositionA, (Vector3)(float3)pending.Triangle.PositionA);
			blitMaterial.SetVector(_CwPositionB, (Vector3)(float3)pending.Triangle.PositionB);
			blitMaterial.SetVector(_CwPositionC, (Vector3)(float3)pending.Triangle.PositionC);

			var coordX = math.floor(CalculateCoord(pending.Triangle.PositionA, globalSizes.x));
			var coordY = math.floor(CalculateCoord(pending.Triangle.PositionA, globalSizes.y));
			var coordZ = math.floor(CalculateCoord(pending.Triangle.PositionA, globalSizes.z));
			var coordW = math.floor(CalculateCoord(pending.Triangle.PositionA, globalSizes.w));

			var coordXA = CalculateCoord(pending.Triangle.PositionA, globalSizes.x) - coordX;
			var coordXB = CalculateCoord(pending.Triangle.PositionB, globalSizes.x) - coordX;
			var coordXC = CalculateCoord(pending.Triangle.PositionC, globalSizes.x) - coordX;

			var coordYA = CalculateCoord(pending.Triangle.PositionA, globalSizes.y) - coordY;
			var coordYB = CalculateCoord(pending.Triangle.PositionB, globalSizes.y) - coordY;
			var coordYC = CalculateCoord(pending.Triangle.PositionC, globalSizes.y) - coordY;

			var coordZA = CalculateCoord(pending.Triangle.PositionA, globalSizes.z) - coordZ;
			var coordZB = CalculateCoord(pending.Triangle.PositionB, globalSizes.z) - coordZ;
			var coordZC = CalculateCoord(pending.Triangle.PositionC, globalSizes.z) - coordZ;

			var coordWA = CalculateCoord(pending.Triangle.PositionA, globalSizes.w) - coordW;
			var coordWB = CalculateCoord(pending.Triangle.PositionB, globalSizes.w) - coordW;
			var coordWC = CalculateCoord(pending.Triangle.PositionC, globalSizes.w) - coordW;

			blitMaterial.SetMatrix(_CwCoordX, new Matrix4x4((Vector2)(float2)coordXA, (Vector2)(float2)coordXB, (Vector2)(float2)coordXC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordY, new Matrix4x4((Vector2)(float2)coordYA, (Vector2)(float2)coordYB, (Vector2)(float2)coordYC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordZ, new Matrix4x4((Vector2)(float2)coordZA, (Vector2)(float2)coordZB, (Vector2)(float2)coordZC, default(Vector4)));
			blitMaterial.SetMatrix(_CwCoordW, new Matrix4x4((Vector2)(float2)coordWA, (Vector2)(float2)coordWB, (Vector2)(float2)coordWC, default(Vector4)));

			return blitMaterial;
		}

		protected override void Schedule(PendingTriangle pending)
		{
			var job = new TriangleJob();

			job.Size      = size;
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

		private void SchedulePoints(PendingPoints pending)
		{
			var job = new PointJob();

			job.Size = size;

			job.Points     = pending.Points;
			job.Directions = pending.Directions;
			job.Coords     = pending.Coords;
			job.DataA      = pending.DataA;
			job.DataB      = pending.DataB;

			pending.Handle = job.Schedule(pending.Handle);
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

		public override double3 GetLocalPoint(double3 localPosition)
		{
			var pending = new PendingPoints(1);

			pending.Points[0] = localPosition;

			SchedulePoints(pending);
			ScheduleBase(pending);
			ScheduleFeatures(pending);

			foreach (var feature in features)
			{
				feature.ScheduleCpu(pending);
			}

			pending.Handle.Complete();

			localPosition = pending.GetPosition(0);

			pending.Dispose();

			return localPosition;
		}

		public override double3 GetWorldPivot(double3 worldPoint)
		{
			var localPoint = transform.InverseTransformPoint((float3)worldPoint);

			localPoint.y = -10000.0f;

			return (float3)transform.TransformPoint(localPoint);
		}

		protected override void Prepare()
		{
			var tile = math.max(math.round(size / (double4)(float4)globalSizes), 1);

			globalTiling = (float4)tile;

			globalTilingNormalized = (float4)(tile / size) * 1000.0f;
			//globalTiling = size / (float4)globalSizes;

			//globalTilingNormalized = globalSizes * 1000.0f;

			base.Prepare();

			heightData.Create(heightTex, heightMidpoint, heightRange);

			topologyData.Create(heightTex, heightMidpoint, heightRange, size, strata);

			blitMaterial = CW.Common.CwHelper.CreateTempMaterial("SgtSquareLandscape", "Hidden/SgtSquareLandscape");

			blitMaterial.SetTexture(_CwAlbedo, albedoTex);
			blitMaterial.SetVector(_CwAlbedoSize, albedoTex != null ? new Vector2(albedoTex.width, albedoTex.height) : Vector2.one);

			blitMaterial.SetTexture(_CwTopology, topologyData.Texture);
			blitMaterial.SetVector(_CwTopologySize, (Vector2)topologyData.Size);
			blitMaterial.SetVector(_CwTopologyData, (Vector3)topologyData.Data);

			blitMaterial.SetFloat(_CwSquareSize, size);

			if (registeredBundle != null)
			{
				blitMaterial.SetTexture(_CwHeightTopologyAtlas, registeredBundle.HeightTopologyAtlas);
				blitMaterial.SetTexture(_CwMaskTopologyAtlas, registeredBundle.MaskTopologyAtlas);
				blitMaterial.SetTexture(_CwGradientAtlas, registeredBundle.GradientAtlas);
				blitMaterial.SetTexture(_CwDetailAtlas, registeredBundle.DetailAtlas);
			}

			var siz = size * 0.5;

			var coordBL = new double3(-siz, 0.0, -siz);
			var coordBR = new double3(+siz, 0.0, -siz);
			var coordTL = new double3(-siz, 0.0, +siz);
			var coordTR = new double3(+siz, 0.0, +siz);

			topology.Add(new Triangle(coordBL, coordBR, coordTL, 0, false));
			topology.Add(new Triangle(coordTR, coordTL, coordBR, 0, false));

			ApplyBlitVariables(blitMaterial);
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

			return job.Schedule(cameraPoints.Handle);
		}

#if UNITY_EDITOR
	protected override void OnDrawGizmosSelected()
	{
		base.OnDrawGizmosSelected();

		Gizmos.matrix = transform.localToWorldMatrix;

		if (IsActivated == false)
		{
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(size, 0.0f, size));
		}
	}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtSquareLandscape))]
	public class SgtRectangleLandscape_Editor : SgtLandscape_Editor
	{
		protected override void OnInspector()
		{
			base.OnInspector();

			SgtSquareLandscape tgt; SgtSquareLandscape[] tgts; GetTargets(out tgt, out tgts);

			var markForRebuild = false;

			Draw("size", ref markForRebuild, "The size of the landscape along the X and Z axes in local space.");
			Draw("albedoTex", ref markForRebuild, "The albedo texture given to the landscape.\n\nNone = White.");
			Draw("heightTex", ref markForRebuild, "The landscape's <b>Radius</b> will be displaced by this heightmap.\n\nNOTE: This texture must have <b>Read/Write</b> enabled.\n\nNOTE: This texture must be <b>Single Channel</b>, and use the <b>Alpha8</b> or <b>R8</b> or <b>R16</b> format.");

			if (Any(tgts, t => t.HeightTex != null))
			{
				Draw("heightMidpoint", ref markForRebuild, "This allows you to set where in the heightmap's height data is 0.\n\n0.0 = Displacement between 0 and <b>HeightRange</b>.\n\n0.5 = Displacement between -<b>HeightRange/2</b> and <b>HeightRange/2</b>.\n\n1.0 = Displacement between -<b>HeightRange</b> and 0.");
				Draw("heightRange", ref markForRebuild, "The maximum difference in height displacement from the heightmap.");
				Draw("strata", ref markForRebuild, "The height scale of the landscape when coloring it.");
			}

			if (markForRebuild == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}

		[UnityEditor.MenuItem("GameObject/CW/Space Graphics Toolkit/Landscape/Square", false, 10)]
		public static void CreateMenuItem()
		{
			var parent   = CW.Common.CwHelper.GetSelectedParent();
			var instance = SgtSquareLandscape.Create(parent != null ? parent.gameObject.layer : 0, parent);

			CW.Common.CwHelper.SelectAndPing(instance);
		}
	}
}
#endif