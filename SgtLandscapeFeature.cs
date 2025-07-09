using Unity.Mathematics;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This is the base class for features, which can be used to modify a landscape. For example, the height or color.</summary>
	[ExecuteInEditMode]
	public abstract class SgtLandscapeFeature : MonoBehaviour
	{
		public double4x4 CalculateMatrix(Vector3 position, Quaternion rotation, Vector3 scale)
		{
			var m = Matrix4x4.TRS(position, rotation, scale) * Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, -0.5f));

			return (float4x4)m.inverse;
		}

		public Vector2 CalculateScale(Vector2 tiling, float heightRange)
		{
			var localSize = (float3)transform.localScale;

			return 0.1f / localSize.xy / (float2)tiling / heightRange;
		}

		public Matrix4x4 CalculateMatrix()
		{
			var m = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale) * Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, -0.5f));

			return m.inverse;
		}

		public void MarkForRebuild()
		{
			var t = GetComponentInParent<SgtLandscape>();

			if (t != null)
			{
				t.MarkForRebuild();
			}
		}

		public abstract void Prepare();

		public abstract void Dispose();

		public abstract void ScheduleCpu(SgtLandscape.PendingPoints pending);

		[System.NonSerialized]
		protected SgtLandscape cachedLandscape;

		protected static readonly int _CwTiling        = Shader.PropertyToID("_CwTiling");
		protected static readonly int _CwTopologyTex   = Shader.PropertyToID("_CwTopologyTex");
		protected static readonly int _CwTopologySize  = Shader.PropertyToID("_CwTopologySize");
		protected static readonly int _CwTopologyData  = Shader.PropertyToID("_CwTopologyData");
		protected static readonly int _CwCoordA        = Shader.PropertyToID("_CwCoordA");
		protected static readonly int _CwCoordB        = Shader.PropertyToID("_CwCoordB");
		protected static readonly int _CwCoordC        = Shader.PropertyToID("_CwCoordC");
		protected static readonly int _CwAlbedoTex     = Shader.PropertyToID("_CwAlbedoTex");
		protected static readonly int _CwAlbedoSize    = Shader.PropertyToID("_CwAlbedoSize");
		protected static readonly int _CwMaskTex       = Shader.PropertyToID("_CwMaskTex");
		protected static readonly int _CwMatrix        = Shader.PropertyToID("_CwMatrix");
		protected static readonly int _CwMaskSize      = Shader.PropertyToID("_CwMaskSize");
		protected static readonly int _CwMaskData      = Shader.PropertyToID("_CwMaskData");
		protected static readonly int _CwMaskDetailTex = Shader.PropertyToID("_CwMaskDetailTex");
		protected static readonly int _CwPointA        = Shader.PropertyToID("_CwPointA");
		protected static readonly int _CwPointB        = Shader.PropertyToID("_CwPointB");
		protected static readonly int _CwPointC        = Shader.PropertyToID("_CwPointC");
		protected static readonly int _CwNormalA       = Shader.PropertyToID("_CwNormalA");
		protected static readonly int _CwNormalB       = Shader.PropertyToID("_CwNormalB");
		protected static readonly int _CwNormalC       = Shader.PropertyToID("_CwNormalC");

		protected virtual void OnEnable()
		{
			cachedLandscape = GetComponentInParent<SgtLandscape>();

			if (cachedLandscape != null)
			{
				cachedLandscape.MarkForRebuild();
			}
		}

		protected virtual void OnDisable()
		{
			if (cachedLandscape != null)
			{
				cachedLandscape.MarkForRebuild();
			}
		}

#if UNITY_EDITOR
		protected virtual void OnValidate()
		{
			if (cachedLandscape != null)
			{
				cachedLandscape.MarkForRebuild();
			}
		}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeFeature))]
	public abstract class SgtLandscapeFeature_Editor : CW.Common.CwEditor
	{
		private Vector3    expectedPosition;
		private quaternion expectedRotation;
		private Vector3    expectedScale;
		private bool       expectedSet;

		protected virtual void OnSceneGUI()
		{
			var tgt = (SgtLandscapeFeature)target;

			var modified = false;

			if (expectedSet == true)
			{
				if (expectedPosition != tgt.transform.localPosition) modified = true;
				if (expectedRotation != tgt.transform.localRotation) modified = true;
				if (expectedScale    != tgt.transform.localScale   ) modified = true;
			}

			if (expectedSet == false || modified == true)
			{
				expectedPosition = tgt.transform.localPosition;
				expectedRotation = tgt.transform.localRotation;
				expectedScale    = tgt.transform.localScale;
				expectedSet      = true;
			}

			if (modified == true)
			{
				OnTransformChanged();
			}
		}

		protected virtual void OnTransformChanged()
		{
		}
	}
}
#endif