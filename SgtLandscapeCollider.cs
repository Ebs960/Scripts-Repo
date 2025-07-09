using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component can be added alongside a terrain to give it <b>MeshColliders</b> that match the visual mesh up to the specified LOD depth.</summary>
	[RequireComponent(typeof(SgtLandscape))]
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Collider")]
	public class SgtLandscapeCollider : MonoBehaviour
	{
		/// <summary>Approximately the smallest triangle we want to be generated when you go to the landscape surface.</summary>
		public float MinimumTriangleSize { set { minimumTriangleSize = value; } get { return minimumTriangleSize; } } [SerializeField] private float minimumTriangleSize = 0.5f;

		[System.NonSerialized]
		private int maxDepth;

		[System.NonSerialized]
		private SgtLandscape parent;

		[System.NonSerialized]
		private GameObject root;

		[System.NonSerialized]
		private Dictionary<SgtLandscape.TriangleHash, MeshCollider> colliders = new Dictionary<SgtLandscape.TriangleHash, MeshCollider>();

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
			parent.OnShowVisual   += HandleShowVisual;
			parent.OnHideVisual   += HandleHideVisual;

			root = new GameObject("Colliders");

			root.transform.SetParent(transform, false);

			maxDepth = parent.CalculateLodDepth(minimumTriangleSize);
		}

		protected virtual void OnDisable()
		{
			parent.OnAddVisual    -= HandleAddVisual;
			parent.OnRemoveVisual -= HandleRemoveVisual;
			parent.OnShowVisual   -= HandleShowVisual;
			parent.OnHideVisual   -= HandleHideVisual;
		}

		private void HandleAddVisual(SgtLandscape.Visual visual, SgtLandscape.PendingTriangle pendingTriangle)
		{
			if (pendingTriangle.Triangle.Depth <= maxDepth)
			{
				var child        = new GameObject("Child"); child.transform.SetParent(root.transform, false);
				var meshCollider = child.AddComponent<MeshCollider>();
				var mesh         = new Mesh();
				var corner       = math.floor(pendingTriangle.GetPosition(0));

				child.transform.localPosition = (float3)corner;

				for (var i = 0; i < SgtLandscape.VERTEX_COUNT; i++)
				{
					SgtLandscape.VERTEX_POSITIONS[i] = new float3(pendingTriangle.GetPosition(i) - corner);
				}

				mesh.SetVertices(SgtLandscape.VERTEX_POSITIONS);

				mesh.SetIndices(SgtLandscape.VERTEX_INDICES, MeshTopology.Triangles, 0);

				meshCollider.sharedMesh = mesh;
				meshCollider.enabled    = pendingTriangle.Triangle.Depth == maxDepth;

				colliders.Add(visual.Hash, meshCollider);
			}
		}

		private void HandleRemoveVisual(SgtLandscape.Visual visual)
		{
			var meshCollider = default(MeshCollider);

			if (colliders.Remove(visual.Hash, out meshCollider) == true)
			{
				DestroyImmediate(meshCollider.sharedMesh);
				DestroyImmediate(meshCollider.gameObject);
			}
		}

		private void HandleShowVisual(SgtLandscape.Visual visual)
		{
			var meshCollider = default(MeshCollider);

			if (colliders.TryGetValue(visual.Hash, out meshCollider) == true)
			{
				meshCollider.enabled = true;
			}
		}

		private void HandleHideVisual(SgtLandscape.Visual visual)
		{
			var meshCollider = default(MeshCollider);

			if (colliders.TryGetValue(visual.Hash, out meshCollider) == true)
			{
				if (visual.Depth == maxDepth)
				{
					meshCollider.enabled = true;
				}
				else
				{
					meshCollider.enabled = false;
				}
			}
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeCollider))]
	public class SgtLandscapeCollider_Editor : CW.Common.CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscapeCollider tgt; SgtLandscapeCollider[] tgts; GetTargets(out tgt, out tgts);

			var markAsDirty = false;

			BeginError(Any(tgts, t => t.MinimumTriangleSize <= 0.0f));
				Draw("minimumTriangleSize", ref markAsDirty, "Approximately the smallest triangle we want to be generated when you go to the landscape surface.");
			EndError();

			if (markAsDirty == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}
	}
}
#endif