using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component can be added alongside a terrain to procedurally spawn prefabs on its surface.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Prefab Spawner")]
	public class SgtLandscapePrefabSpawner : MonoBehaviour
	{
		public enum RotateType
		{
			Randomly,
			ToLandscapeCenter,
			ToSurfaceNormal,
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

		/// <summary>The prefabs that will be picked from.</summary>
		public List<Transform> Prefabs { get { if (prefabs == null) prefabs = new List<Transform>(); return prefabs; } } [SerializeField] private List<Transform> prefabs;

		[System.NonSerialized]
		private SgtLandscape parent;

		[System.NonSerialized]
		private int depth;

		[System.NonSerialized]
		private Dictionary<SgtLandscape.TriangleHash, List<Transform>> triangleClones = new Dictionary<SgtLandscape.TriangleHash, List<Transform>>();

		private static Stack<List<Transform>> clonesPool = new Stack<List<Transform>>();

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

		private void HandleAddVisual(SgtLandscape.Visual visual, SgtLandscape.PendingTriangle pendingTriangle)
		{
			if (pendingTriangle.Triangle.Depth == depth && prefabs != null && prefabs.Count > 0 && count > 0)
			{
				var clones = clonesPool.Count > 0 ? clonesPool.Pop() : new List<Transform>();
				var center = (float3)transform.position;

				CW.Common.CwHelper.BeginSeed(visual.Hash.GetHashCode() * 17 + seed * 13);

				for (var i = 0; i < count; i++)
				{
					var index  = UnityEngine.Random.Range(0, prefabs.Count);
					var prefab = prefabs[index];

					if (prefab != null)
					{
						var vert      = UnityEngine.Random.Range(0, SgtLandscape.VERTEX_COUNT);
						var point     = pendingTriangle.Points[vert];
						var direction = pendingTriangle.Directions[vert];
						var height    = pendingTriangle.Heights[vert];
						var dataA     = pendingTriangle.DataA[vert];

						if (SampleMask(dataA.xy) >= UnityEngine.Random.value)
						{
							var clone = Instantiate(prefab, transform, false);

							clone.localPosition = (float3)(point + direction * height);
							clone.localScale    = clone.localScale * UnityEngine.Random.Range(scaleMin, scaleMax);

							switch (rotate)
							{
								case RotateType.Randomly:
								{
									clone.localRotation = UnityEngine.Random.rotationUniform;
								}
								break;

								case RotateType.ToLandscapeCenter:
								{
									clone.up = (float3)point - center;
									clone.Rotate(0.0f, UnityEngine.Random.Range(-180.0f, 180.0f), 0.0f, Space.Self);
								}
								break;

								case RotateType.ToSurfaceNormal:
								{
									clone.up = (float3)direction;
									clone.Rotate(0.0f, UnityEngine.Random.Range(-180.0f, 180.0f), 0.0f, Space.Self);
								}
								break;
							}

							clone.localPosition += clone.up * offset;

							clones.Add(clone);
						}
					}
				}

				CW.Common.CwHelper.EndSeed();

				triangleClones.Add(visual.Hash, clones);
			}
		}

		private void HandleRemoveVisual(SgtLandscape.Visual visual)
		{
			var clones = default(List<Transform>);

			if (triangleClones.Remove(visual.Hash, out clones) == true)
			{
				foreach (var clone in clones)
				{
					if (clone != null)
					{
						DestroyImmediate(clone.gameObject);
					}
				}

				clones.Clear();

				clonesPool.Push(clones);
			}
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapePrefabSpawner))]
	public class SgtLandscapePrefabSpawner_Editor : CW.Common.CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscapePrefabSpawner tgt; SgtLandscapePrefabSpawner[] tgts; GetTargets(out tgt, out tgts);

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

			BeginError(Any(tgts, t => t.Prefabs.Count == 0));
				Draw("prefabs", ref markAsDirty, "The prefabs that will be picked from.");
			EndError();

			if (markAsDirty == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}
	}
}
#endif