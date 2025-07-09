using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component can be added alongside a terrain to procedurally spawn prefabs on its surface. This works similarly to <b>SgtLandscapePrefabSpawner</b>, but it's much higher performance.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Static Spawner")]
	public class SgtLandscapeStaticSpawner : MonoBehaviour
	{
		public enum RotateType
		{
			Randomly,
			ToLandscapeCenter,
			ToSurfaceNormal,
		}

		private class Group
		{
			public List<Matrix4x4> Matrices = new List<Matrix4x4>();

			public Bounds Bounds;

			public static Stack<Group> Pool = new Stack<Group>();
		}

		private class Batch
		{
			public MaterialPropertyBlock Properties = new MaterialPropertyBlock();

			public int Count;

			public Bounds Bounds;

			public static Stack<Batch> Pool = new Stack<Batch>();
		}

		/// <summary>This allows you to define the spawn area.
		/// NOTE: This texture should be <b>Single Channel</b> using either the <b>R8</b> or <b>Alpha8</b> formats.
		/// NOTE: This texture should have <b>read/write</b> enabled.</summary>
		public Texture2D MaskTex { set { maskTex = value; } get { return maskTex; } } [SerializeField] private Texture2D maskTex;

		/// <summary>Invert the mask, so 0 values become 255 values, and 255 values become 0 values?</summary>
		public bool InvertMask { set { invertMask = value; } get { return invertMask; } } [SerializeField] private bool invertMask;

		/// <summary>Prefabs will spawn at the LOD level where triangles are approximately this size.</summary>
		public float TriangleSize { set { triangleSize = value; } get { return triangleSize; } } [SerializeField] private float triangleSize = 10.0f;

		/// <summary>The amount of prefabs that will be spawned per LOD chunk.</summary>
		public int Count { set { count = value; } get { return count; } } [SerializeField] private int count = 10;

		/// <summary>The random seed when procedurally spawning the prefabs.</summary>
		public int Seed { set { seed = value; } get { return seed; } } [SerializeField] [CW.Common.CwSeed] private int seed;

		/// <summary>The spawned prefabs will have their localScale multiplied by at least this number.</summary>
		public float ScaleMin { set { scaleMin = value; } get { return scaleMin; } } [SerializeField] private float scaleMin = 0.75f;

		/// <summary>The spawned prefabs will have their localScale multiplied by at most this number.</summary>
		public float ScaleMax { set { scaleMax = value; } get { return scaleMax; } } [SerializeField] private float scaleMax = 1.25f;

		/// <summary>The spawned prefabs will have their position offset by this local space distance.</summary>
		public float Offset { set { offset = value; } get { return offset; } } [SerializeField] private float offset;

		/// <summary>How should the spawned prefabs be rotated?</summary>
		public RotateType Rotate { set { rotate = value; } get { return rotate; } } [SerializeField] private RotateType rotate;

		/// <summary>The mesh that will be rendered.</summary>
		public Mesh Mesh { set { mesh = value; } get { return mesh; } } [SerializeField] private Mesh mesh;

		/// <summary>The material that will be rendered.
		/// NOTE: This must use the <b>SGT / Landscape Static</b> shader.</summary>
		public Material Material { set { material = value; } get { return material; } } [SerializeField] private Material material;

		[System.NonSerialized]
		private SgtLandscape parent;

		[System.NonSerialized]
		private int depth;

		[System.NonSerialized]
		private Dictionary<SgtLandscape.TriangleHash, Group> triangleGroups = new Dictionary<SgtLandscape.TriangleHash, Group>();

		[System.NonSerialized]
		private List<Batch> batches = new List<Batch>();

		private static readonly int _SGT_Transform = Shader.PropertyToID("_SGT_Transform");
		private static readonly int _SGT_Transforms = Shader.PropertyToID("_SGT_Transforms");

		private static Matrix4x4[] tempTransforms = new Matrix4x4[128];

		private MaterialPropertyBlock properties;

		private bool dirty;

		public void MarkForRebuild()
		{
			var t = GetComponentInParent<SgtLandscape>();

			if (t != null)
			{
				t.MarkForRebuild();
			}
		}

		protected virtual void OnEnable()
		{
			parent = GetComponentInParent<SgtLandscape>();

			parent.OnAddVisual    += HandleAddVisual;
			parent.OnRemoveVisual += HandleRemoveVisual;

			depth = parent.CalculateLodDepth(triangleSize);

			properties = new MaterialPropertyBlock();
		}

		protected virtual void Update()
		{
			if (dirty == true)
			{
				RebuildBatches();
			}

			var matrix = transform.localToWorldMatrix;

			foreach (var batch in batches)
			{
				if (batch.Count > 0)
				{
					batch.Properties.SetMatrix(_SGT_Transform, matrix);

					var bounds = TransformBounds(transform, batch.Bounds);

					Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, batch.Count, batch.Properties);
				}
			}
		}

		private static Bounds TransformBounds(Transform t, Bounds b)
		{
			var center = t.TransformPoint(b.center);

			// transform the local extents' axes
			var extents = b.extents;
			var axisX = t.TransformVector(extents.x, 0, 0);
			var axisY = t.TransformVector(0, extents.y, 0);
			var axisZ = t.TransformVector(0, 0, extents.z);

			// sum their absolute value to get the world extents
			extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
			extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
			extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

			return new Bounds { center = center, extents = extents };
		}

		private void RebuildBatches()
		{
			ClearBatches();

			var currentBatch = default(Batch);

			foreach (var pair in triangleGroups)
			{
				var count = pair.Value.Matrices.Count;

				if (count > 0)
				{
					if (currentBatch == null)
					{
						currentBatch = AddBatch();

						currentBatch.Bounds = pair.Value.Bounds;
					}
					else
					{
						currentBatch.Bounds.Encapsulate(pair.Value.Bounds);
					}

					for (var i = 0; i < count; i++)
					{
						if (currentBatch.Count >= tempTransforms.Length)
						{
							currentBatch.Properties.SetMatrixArray(_SGT_Transforms, tempTransforms);

							currentBatch = AddBatch();
							
							currentBatch.Bounds = pair.Value.Bounds;
						}

						tempTransforms[currentBatch.Count] = pair.Value.Matrices[i];

						currentBatch.Count += 1;
					}
				}
			}

			if (currentBatch != null)
			{
				currentBatch.Properties.SetMatrixArray(_SGT_Transforms, tempTransforms);
			}

			dirty = false;
		}

		private void ClearBatches()
		{
			foreach (var batch in batches)
			{
				batch.Count = 0;

				Batch.Pool.Push(batch);
			}

			batches.Clear();
		}

		private Batch AddBatch()
		{
			var batch = Batch.Pool.Count > 0 ? Batch.Pool.Pop() : new Batch();

			batches.Add(batch);

			return batch;
		}

		protected virtual void OnDisable()
		{
			parent.OnAddVisual    -= HandleAddVisual;
			parent.OnRemoveVisual -= HandleRemoveVisual;
		}

		private float SampleMask(double2 uv)
		{
			if (maskTex != null)
			{
				var x = math.clamp((int)(uv.x * maskTex.width ), 0, maskTex.width  - 1);
				var y = math.clamp((int)(uv.y * maskTex.height), 0, maskTex.height - 1);
				var d = maskTex.GetPixelData<byte>(0);
				var m = d[x + y * maskTex.width] / 255.0f;

				if (invertMask == true)
				{
					m = 1.0f - m;
				}

				return m;
			}

			return 1.0f;
		}

		private float3 GetRandomBary()
		{
			var u = UnityEngine.Random.value;
			var v = UnityEngine.Random.value;

			if (u + v > 1)
			{
				u = 1 - u;
				v = 1 - v;
			}

			return new Vector3(u, v, 1.0f - u - v);
		}

		private void HandleAddVisual(SgtLandscape.Visual visual, SgtLandscape.PendingTriangle pendingTriangle)
		{
			if (pendingTriangle.Triangle.Depth == depth && count > 0)
			{
				var group  = Group.Pool.Count > 0 ? Group.Pool.Pop() : new Group();
				var center = (float3)transform.position;

				CW.Common.CwHelper.BeginSeed(visual.Hash.GetHashCode() * 17 + seed * 13);

				for (var i = 0; i < count; i++)
				{
					var vert   = UnityEngine.Random.Range(0, SgtLandscape.TRIANGLE_COUNT) * 3;
					var indexA = SgtLandscape.VERTEX_INDICES[vert + 0];
					var indexB = SgtLandscape.VERTEX_INDICES[vert + 1];
					var indexC = SgtLandscape.VERTEX_INDICES[vert + 2];
					var bary   = GetRandomBary();
					var coord  = pendingTriangle.DataA[indexA].xy * bary.x + pendingTriangle.DataA[indexB].xy * bary.y + pendingTriangle.DataA[indexC].xy * bary.z;

					if (SampleMask(coord) >= UnityEngine.Random.value)
					{
						var localPosition = (Vector3)(float3)(pendingTriangle.GetPosition(indexA) * bary.x + pendingTriangle.GetPosition(indexB) * bary.y + pendingTriangle.GetPosition(indexC) * bary.z);
						var localRotation = Quaternion.identity;
						var localScale    = UnityEngine.Random.Range(scaleMin, scaleMax);

						switch (rotate)
						{
							case RotateType.Randomly:
							{
								localRotation = UnityEngine.Random.rotationUniform;
							}
							break;

							case RotateType.ToLandscapeCenter:
							{
								var point = pendingTriangle.Points[indexA] * bary.x + pendingTriangle.Points[indexB] * bary.y + pendingTriangle.Points[indexC] * bary.z;

								localRotation = Quaternion.FromToRotation(Vector3.up, (float3)point - center) * Quaternion.Euler(0.0f, UnityEngine.Random.Range(-180.0f, 180.0f), 0.0f);
							}
							break;

							case RotateType.ToSurfaceNormal:
							{
								var direction = pendingTriangle.Directions[indexA] * bary.x + pendingTriangle.Directions[indexB] * bary.y + pendingTriangle.Directions[indexC] * bary.z;

								localRotation = Quaternion.FromToRotation(Vector3.up, (float3)direction) * Quaternion.Euler(0.0f, UnityEngine.Random.Range(-180.0f, 180.0f), 0.0f);
							}
							break;
						}

						localPosition += localRotation * new Vector3(0.0f, offset, 0.0f);

						if (group.Matrices.Count == 0)
						{
							group.Bounds = new Bounds(localPosition, Vector3.zero);
						}
						else
						{
							group.Bounds.Encapsulate(localPosition);
						}

						group.Matrices.Add(Matrix4x4.TRS(localPosition, localRotation, Vector3.one * localScale));
					}
				}

				CW.Common.CwHelper.EndSeed();

				triangleGroups.Add(visual.Hash, group);

				dirty = true;
			}
		}

		private void HandleRemoveVisual(SgtLandscape.Visual visual)
		{
			var group = default(Group);

			if (triangleGroups.Remove(visual.Hash, out group) == true)
			{
				group.Matrices.Clear();

				Group.Pool.Push(group);

				dirty = true;
			}
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeStaticSpawner))]
	public class SgtLandscapeStaticSpawner_Editor : CW.Common.CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscapeStaticSpawner tgt; SgtLandscapeStaticSpawner[] tgts; GetTargets(out tgt, out tgts);

			var markAsDirty = false;

			Draw("maskTex", ref markAsDirty, "This allows you to define the spawn area.\n\t\t/// NOTE: This texture should be <b>Single Channel</b> using either the <b>R8</b> or <b>Alpha8</b> formats.\n\t\t/// NOTE: This texture should have <b>read/write</b> enabled.");
			Draw("invertMask", ref markAsDirty, "Invert the mask, so 0 values become 255 values, and 255 values become 0 values?");
			BeginError(Any(tgts, t => t.TriangleSize <= 0.0f));
				Draw("triangleSize", ref markAsDirty, "Prefabs will spawn at the LOD level where triangles are approximately this size.");
			EndError();
			BeginError(Any(tgts, t => t.Count <= 0));
				Draw("count", ref markAsDirty, "The amount of prefabs that will be spawned per LOD chunk.");
			EndError();
			Draw("seed", ref markAsDirty, "The random seed when procedurally spawning the prefabs.");

			Separator();

			Draw("scaleMin", ref markAsDirty, "The spawned prefabs will have their localScale multiplied by at least this number.");
			Draw("scaleMax", ref markAsDirty, "The spawned prefabs will have their localScale multiplied by at most this number.");
			Draw("rotate", ref markAsDirty, "How should the spawned prefabs be rotated?");
			Draw("offset", ref markAsDirty, "The spawned prefabs will have their position offset by this local space distance.");

			Separator();

			BeginError(Any(tgts, t => t.Mesh == null));
				Draw("mesh", ref markAsDirty, "The mesh that will be rendered.");
			EndError();
			BeginError(Any(tgts, t => t.Material == null));
				Draw("material", ref markAsDirty, "The material that will be rendered.");
			EndError();

			if (markAsDirty == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}
	}
}
#endif