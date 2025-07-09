using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SpaceGraphicsToolkit.Landscape
{
	public partial class SgtLandscape
	{
		[BurstCompile]
		public struct UpdateTrianglesJob : IJob
		{
			[ReadOnly] public NativeList<Triangle> Topology;
			[ReadOnly] public NativeList<double3>  CameraPositions;
			[ReadOnly] public double               CameraDetailSq;
			[ReadOnly] public DeformType           Deform;
			[ReadOnly] public double               Radius;
			[ReadOnly] public int                  MaxDepth;
			[ReadOnly] public int                  MaxSteps;

			public NativeList<Triangle> Triangles;
			public NativeList<Triangle> CreateDiffs;
			public NativeList<Triangle> DeleteDiffs;
			public NativeList<Triangle> StatusDiffs;

			private bool TooLarge(double3 a, double3 b, double3 c, double3 e)
			{
				var m = (a + b + c) / 3.0f;

				var r2 = math.max(math.distancesq(m, a), math.max(math.distancesq(m, b), math.distancesq(m, c)));

				var d2 = math.distancesq(e, m);

				if (d2 > r2)
				{
					return (r2 / d2) > CameraDetailSq;
				}
				else
				{
					return true;
				}
			}

			public bool AnyTooLarge(double3 a, double3 b, double3 c, int depth)
			{
				switch (Deform)
				{
					case DeformType.Sphere:
					{
						a = math.normalize(a) * Radius;
						b = math.normalize(b) * Radius;
						c = math.normalize(c) * Radius;
					}
					break;
				}

				for (var i = 0; i < CameraPositions.Length; i++)
				{
					if (TooLarge(a, b, c, CameraPositions[i]) == true)
					{
						return true;
					}
				}

				return false;
			}

			private void CheckTriangles(NativeParallelHashSet<TriangleHash> oldHashes, NativeParallelHashSet<double3> pivots)
			{
				var newCount = 0;

				for (var i = 0; i < Triangles.Length; i++)
				{
					var triangle = Triangles[i];

					if (triangle.Depth < MaxDepth && AnyTooLarge(triangle.PositionA, triangle.PositionB, triangle.PositionC, triangle.Depth) == true)
					{
						var childA = new Triangle(triangle.Pivot2, triangle.PositionA, triangle.PositionB, triangle.Depth + 1, false);
						var childB = new Triangle(triangle.Pivot2, triangle.PositionC, triangle.PositionA, triangle.Depth + 1, false);

						if (oldHashes.Contains(childA.Hash) == true && oldHashes.Contains(childB.Hash) == true)
						{
							triangle.Split = true; Triangles[i] = triangle;

							pivots.Add(triangle.Pivot2);

							Triangles.Add(childA);
							Triangles.Add(childB);
						}
						else if (newCount < MaxSteps)
						{
							newCount += 1;

							triangle.Split = true; Triangles[i] = triangle;

							pivots.Add(triangle.Pivot2);

							Triangles.Add(childA);
							Triangles.Add(childB);
						}
					}
				}
			}

			private void FixTriangles(NativeParallelHashSet<double3> pivots)
			{
				for (var i = 0; i < Triangles.Length; i++)
				{
					var tri = Triangles[i];

					if (tri.Split == false)
					{
						if (pivots.Contains(tri.Pivot1) == true)
						{
							tri.Split = true; Triangles[i] = tri;

							Triangles.Add(new Triangle(tri.Pivot1, tri.PositionC, tri.PositionA, tri.Depth + 1, false));
							Triangles.Add(new Triangle(tri.Pivot1, tri.PositionB, tri.PositionC, tri.Depth + 1, false));
						}
						else if (pivots.Contains(tri.Pivot2) == true)
						{
							tri.Split = true; Triangles[i] = tri;

							Triangles.Add(new Triangle(tri.Pivot2, tri.PositionA, tri.PositionB, tri.Depth + 1, false));
							Triangles.Add(new Triangle(tri.Pivot2, tri.PositionC, tri.PositionA, tri.Depth + 1, false));
						}
						else if (pivots.Contains(tri.Pivot3) == true)
						{
							tri.Split = true; Triangles[i] = tri;

							Triangles.Add(new Triangle(tri.Pivot3, tri.PositionB, tri.PositionC, tri.Depth + 1, false));
							Triangles.Add(new Triangle(tri.Pivot3, tri.PositionA, tri.PositionB, tri.Depth + 1, false));
						}
					}
				}
			}

			private void CalculateDiff(NativeList<Triangle> oldTriangles, NativeList<Triangle> newTriangles)
			{
				var oldHashes = new NativeParallelHashMap<TriangleHash, Triangle>(oldTriangles.Length, Allocator.Temp);
				var newHashes = new NativeParallelHashMap<TriangleHash, Triangle>(newTriangles.Length, Allocator.Temp);

				for (var i = 0; i < oldTriangles.Length; i++)
				{
					var oldTriangle = oldTriangles[i]; oldHashes.Add(oldTriangle.Hash, oldTriangle);
				}

				for (var i = 0; i < newTriangles.Length; i++)
				{
					var newTriangle = newTriangles[i]; newHashes.Add(newTriangle.Hash, newTriangle);
				}

				for (var i = 0; i < oldTriangles.Length; i++)
				{
					var newTriangle = default(Triangle);
					var oldTriangle = oldTriangles[i];

					if (newHashes.TryGetValue(oldTriangle.Hash, out newTriangle) == false)
					{
						DeleteDiffs.Add(oldTriangle);
					}
				}

				for (var i = 0; i < newTriangles.Length; i++)
				{
					var newTriangle = newTriangles[i];
					var oldTriangle = default(Triangle);

					if (oldHashes.TryGetValue(newTriangle.Hash, out oldTriangle) == false)
					{
						CreateDiffs.Add(newTriangle);
					}
					else if (newTriangle.Split != oldTriangle.Split)
					{
						StatusDiffs.Add(newTriangle);
					}
				}
			}

			public void Execute()
			{
				var pivots       = new NativeParallelHashSet<double3>(1024 * 64, Allocator.Temp);
				var oldHashes    = new NativeParallelHashSet<TriangleHash>(Triangles.Length, Allocator.Temp);
				var oldTriangles = new NativeList<Triangle>(Triangles.Length, Allocator.Temp);

				oldTriangles.CopyFrom(Triangles);

				for (var i = 0; i < oldTriangles.Length; i++)
				{
					var oldTriangle = oldTriangles[i]; oldHashes.Add(oldTriangle.Hash);
				}

				Triangles.Clear();

				Triangles.AddRange(Topology.AsArray());

				CheckTriangles(oldHashes, pivots);

				FixTriangles(pivots);

				CreateDiffs.Clear();
				DeleteDiffs.Clear();
				StatusDiffs.Clear();

				CalculateDiff(oldTriangles, Triangles);
			}
		}

		[BurstCompile]
		public struct AdjustCameraPositionsJob : IJob
		{
			[ReadOnly] public NativeArray<double3> Directions;
			[ReadOnly] public NativeArray<double>  Heights;

			public NativeList<double3> CameraPositions;

			public void Execute()
			{
				for (var i = 0; i < CameraPositions.Length; i++)
				{
					CameraPositions[i] -= Directions[i] * Heights[i];
				}
			}
		}

		[BurstCompile]
		public struct PixelJob : IJob
		{
			[ReadOnly] public NativeArray<double3>  Points;
			[ReadOnly] public NativeArray<double3>  Directions;
			[ReadOnly] public NativeArray<double>   Heights;
			[ReadOnly] public NativeArray<double3>  Weights;

			[WriteOnly] public NativeArray<float4> PixelP;

			public void Execute()
			{
				var origin = math.floor(GetPosition(0));

				for (var i = 0; i < Directions.Length; i++)
				{
					PixelP[i] = new float4((float3)(GetPosition(i) - origin), 0.0f);
				}
			}

			public double3 GetPosition(int index)
			{
				return Points[index] + Directions[index] * Heights[index];
			}
		}

		public class PendingPoints
		{
			public NativeArray<double3> Points;
			public NativeArray<double3> Directions;
			public NativeArray<double > Heights;
			public NativeArray<double4> Coords;
			public NativeArray<double4> DataA;
			public NativeArray<double4> DataB;

			public int       Count;
			public JobHandle Handle;

			public PendingPoints(int total)
			{
				Count      = total;
				Points     = new NativeArray<double3>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				Directions = new NativeArray<double3>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				Heights    = new NativeArray<double >(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				Coords     = new NativeArray<double4>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				DataA      = new NativeArray<double4>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				DataB      = new NativeArray<double4>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}

			public virtual void Dispose()
			{
				Points.Dispose();
				Directions.Dispose();
				Heights.Dispose();
				Coords.Dispose();
				DataA.Dispose();
				DataB.Dispose();
			}

			public double3 GetPosition(int index)
			{
				return Points[index] + Directions[index] * Heights[index];
			}
		}

		public class PendingUpdate
		{
			public JobHandle Handle;

			public bool Running;

			public void Schedule(JobHandle handle)
			{
				Handle = handle;
				Running = true;
			}

			public void Complete()
			{
				if (Running == true)
				{
					Handle.Complete();

					Running = false;
				}
			}
		}

		public class PendingTriangle : PendingPoints
		{
			public NativeArray<float4> PixelP = new NativeArray<float4>(VERTEX_COUNT, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

			public Dictionary<SgtLandscapeFeature, System.IDisposable> AdditionalData = new Dictionary<SgtLandscapeFeature, System.IDisposable>();

			public static Stack<PendingTriangle> Pool = new Stack<PendingTriangle>();

			public SgtLandscape Core;
			public Triangle     Triangle;

			public PendingTriangle() : base(VERTEX_COUNT)
			{
			}

			public T GetAdditionalDataAndRemove<T>(SgtLandscapeFeature feature)
			{
				System.IDisposable o;

				AdditionalData.Remove(feature, out o);

				return (T)o;
			}

			public override void Dispose()
			{
				base.Dispose();

				PixelP.Dispose();

				foreach (var pair in AdditionalData)
				{
					pair.Value.Dispose();
				}

				AdditionalData.Clear();
			}
		}

		protected PendingTriangle Schedule(Triangle triangle)
		{
			var pending = PendingTriangle.Pool.Count > 0 ? PendingTriangle.Pool.Pop() : new PendingTriangle();

			pending.Core     = this;
			pending.Triangle = triangle;

			Schedule(pending);

			return pending;
		}

		protected virtual void Schedule(PendingTriangle pending)
		{
			var pixelJob = new PixelJob();

			pixelJob.Weights    = VERTEX_WEIGHTS;
			pixelJob.Points     = pending.Points;
			pixelJob.Directions = pending.Directions;
			pixelJob.Heights    = pending.Heights;
			pixelJob.PixelP     = pending.PixelP;

			pending.Handle = pixelJob.Schedule(pending.Handle);
		}

		protected void ScheduleFeatures(PendingPoints pending)
		{
			if (bundle != null)
			{
				foreach (var feature in features)
				{
					feature.ScheduleCpu(pending);
				}
			}
		}

		protected void ScheduleAdjustCameraPositions(PendingPoints pending)
		{
			var job = new AdjustCameraPositionsJob();

			job.CameraPositions = cameraPositions;
			job.Directions      = pending.Directions;
			job.Heights         = pending.Heights;

			pending.Handle = job.Schedule(pending.Handle);
		}

		private Visual CompleteAndAddVisual(PendingTriangle pending)
		{
			pending.Handle.Complete();

			var visual = AddVisual(pending);

			PendingTriangle.Pool.Push(pending);

			return visual;
		}

		protected abstract JobHandle ScheduleUpdateTriangles(float detail, int maxSteps);
	}
}