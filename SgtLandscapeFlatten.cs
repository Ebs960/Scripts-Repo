using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This feature can modify the height of a terrain in a specific area.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Flatten")]
	public class SgtLandscapeFlatten : SgtLandscapeFeature
	{
		public enum SpaceType
		{
			Global,
			Local
		}

		/// <summary>Where should the flattening be applied?
		/// Global = The whole landscape.
		/// Local = A small section of the landscape defined by the <b>Transform</b> component's position, rotation, and scale.</summary>
		public SpaceType Space { set { space = value; } get { return space; } } [SerializeField] private SpaceType space;

		/// <summary>This allows you to set the height value the landscape will be flattened to.</summary>
		public float TargetHeight { set { targetHeight = value; } get { return targetHeight; } } [SerializeField] private float targetHeight;

		/// <summary>This allows you to set the strata value the landscape will be flattened to.</summary>
		public float TargetStrata { set { targetStrata = value; } get { return targetStrata; } } [SerializeField] private float targetStrata;

		/// <summary>This allows you to set the height value the landscape will be flattened to.</summary>
		public float FlattenHeight { set { flattenHeight = value; } get { return flattenHeight; } } [SerializeField] [Range(0.0f, 1.0f)] private float flattenHeight = 1.0f;

		/// <summary>This allows you to set the strata value the landscape will be flattened to.</summary>
		public float FlattenStrata { set { flattenStrata = value; } get { return flattenStrata; } } [SerializeField] [Range(0.0f, 1.0f)] private float flattenStrata = 1.0f;

		/// <summary>The mask texture used by this component.</summary>
		public int MaskIndex { set { maskIndex = value; } get { return maskIndex; } } [SerializeField] protected int maskIndex;

		/// <summary>This allows you to invert the mask.</summary>
		public bool MaskInvert { set { maskInvert = value; } get { return maskInvert; } } [SerializeField] private bool maskInvert;

		[System.NonSerialized]
		private double4x4 matrix;

		protected static readonly int _CwTargetStrata  = Shader.PropertyToID("_CwTargetStrata");
		protected static readonly int _CwTargetHeight  = Shader.PropertyToID("_CwTargetHeight");
		protected static readonly int _CwFlattenStrata = Shader.PropertyToID("_CwFlattenStrata");
		protected static readonly int _CwFlattenHeight = Shader.PropertyToID("_CwFlattenHeight");

		[BurstCompile]
		public struct GlobalJob : IJob
		{
			[ReadOnly] public NativeArray<double4> DataA;
			[ReadOnly] public NativeArray<double4> DataB;
			           public NativeArray<double>  Heights;

			[ReadOnly] public double TargetHeight;
			[ReadOnly] public double FlattenHeight;

			[ReadOnly] public NativeArray<byte  > MaskData08;
			[ReadOnly] public NativeArray<ushort> MaskData16;
			[ReadOnly] public int2                MaskSize;
			[ReadOnly] public float               MaskOffset;
			[ReadOnly] public bool                MaskInvert;

			public void Execute()
			{
				if (MaskData16.Length > 1)
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						var coord = DataA[i].xy + DataB[i].xy * MaskOffset;
						var mask  = SgtLandscape.Sample_Linear_Clamp(MaskData16, MaskSize, coord * MaskSize);

						if (MaskInvert == true) mask = 1.0f - mask;

						Heights[i] = math.lerp(Heights[i], TargetHeight, FlattenHeight * mask);
					}
				}
				else
				{
					for (var i = 0; i < Heights.Length; i++)
					{
						var coord = DataA[i].xy + DataB[i].xy * MaskOffset;
						var mask  = SgtLandscape.Sample_Linear_Clamp(MaskData08, MaskSize, coord * MaskSize);

						if (MaskInvert == true) mask = 1.0f - mask;

						Heights[i] = math.lerp(Heights[i], TargetHeight, FlattenHeight * mask);
					}
				}
			}
		}

		[BurstCompile]
		public struct LocalJob : IJob
		{
			[ReadOnly] public NativeArray<double3> Points;
			           public NativeArray<double>  Heights;

			[ReadOnly] public NativeArray<byte  > MaskData08;
			[ReadOnly] public NativeArray<ushort> MaskData16;
			[ReadOnly] public int2                MaskSize;
			[ReadOnly] public bool                MaskInvert;
			
			[ReadOnly] public double4x4 Matrix;
			[ReadOnly] public double    TargetHeight;
			[ReadOnly] public double    FlattenHeight;

			public void Execute()
			{
				if (MaskData16.Length > 1)
				{
					for (var i = 0; i < Points.Length; i++)
					{
						var point = math.mul(Matrix, new double4(Points[i], 1.0)).xyz;

						if (point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1 && point.z >= 0 && point.z <= 1)
						{
							var mask = SgtLandscape.Sample_Linear_Clamp(MaskData16, MaskSize, point.xy * MaskSize);

							if (MaskInvert == true) mask = 1.0f - mask;

							Heights[i] = math.lerp(Heights[i], TargetHeight, FlattenHeight * mask);
						}
					}
				}
				else
				{
					for (var i = 0; i < Points.Length; i++)
					{
						var point = math.mul(Matrix, new double4(Points[i], 1.0)).xyz;

						if (point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1 && point.z >= 0 && point.z <= 1)
						{
							var mask = SgtLandscape.Sample_Linear_Clamp(MaskData08, MaskSize, point.xy * MaskSize);

							if (MaskInvert == true) mask = 1.0f - mask;

							Heights[i] = math.lerp(Heights[i], TargetHeight, FlattenHeight * mask);
						}
					}
				}
			}
		}

		public static SgtLandscapeFlatten Create(int layer = 0, Transform parent = null)
		{
			return Create(layer, parent, Vector3.zero, Quaternion.identity, Vector3.one);
		}

		public static SgtLandscapeFlatten Create(int layer, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
		{
			return CW.Common.CwHelper.CreateGameObject("SgtLandscapeFlatten", layer, parent, localPosition, localRotation, localScale).AddComponent<SgtLandscapeFlatten>();
		}

		public override void Prepare()
		{
			matrix = CalculateMatrix(transform.localPosition, transform.localRotation, transform.localScale);
		}

		public override void Dispose()
		{
		}

		public override void ScheduleCpu(SgtLandscape.PendingPoints pending)
		{
			var maskData = cachedLandscape.Bundle.GetMaskData(maskIndex);

			if (space == SpaceType.Global)
			{
				var job = new GlobalJob();

				job.DataA   = pending.DataA;
				job.DataB   = pending.DataB;
				job.Heights = pending.Heights;

				job.MaskData08 = maskData.Data08;
				job.MaskData16 = maskData.Data16;
				job.MaskSize   = maskData.Size;
				job.MaskInvert = maskInvert;

				job.TargetHeight  = targetHeight;
				job.FlattenHeight = flattenHeight;

				pending.Handle = job.Schedule(pending.Handle);
			}
			else
			{
				var job = new LocalJob();

				job.Points  = pending.Points;
				job.Heights = pending.Heights;

				job.MaskData08 = maskData.Data08;
				job.MaskData16 = maskData.Data16;
				job.MaskSize   = maskData.Size;
				job.MaskInvert = maskInvert;

				job.Matrix        = matrix;
				job.TargetHeight  = targetHeight;
				job.FlattenHeight = flattenHeight;

				pending.Handle = job.Schedule(pending.Handle);
			}
		}

#if UNITY_EDITOR
		protected virtual void OnDrawGizmos()
		{
			if (space == SpaceType.Local)
			{
				var m = transform.localToWorldMatrix;

				Gizmos.matrix = m;

				Gizmos.DrawWireCube(Vector3.zero, new Vector3(1.0f, 1.0f, 0.0f));
				Gizmos.DrawWireCube(Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f));

				var maskTex = cachedLandscape.GetMaskTexture(maskIndex);

				if (maskTex != null)
				{
					for (var i = 0; i < 16; i++)
					{
						var subMatrix = m * Matrix4x4.Translate(new Vector3(0.0f, 0.0f, Mathf.Lerp(-0.5f, 0.5f, i / 15.0f)));

						CW.Common.CwHelper.DrawShapeOutline(maskTex, 0, subMatrix);
					}
				}
			}
		}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeFlatten))]
	public class SgtLandscapeFlatten_Editor : SgtLandscapeFeature_Editor
	{
		protected override void OnInspector()
		{
			SgtLandscapeFlatten tgt; SgtLandscapeFlatten[] tgts; GetTargets(out tgt, out tgts);

			var markForRebuild = false;

			Draw("space", ref markForRebuild, "Where should the flattening be applied?\n\nGlobal = The whole landscape.\n\nLocal = A small section of the landscape defined by the <b>Transform</b> component's position, rotation, and scale.");
			BeginDisabled();
				UnityEditor.EditorGUILayout.Toggle("Mask", true);
			EndDisabled();
			BeginIndent();
				Draw("maskIndex", ref markForRebuild, "The mask texture used by this component.");
			EndIndent();
			Draw("maskInvert", ref markForRebuild, "This allows you to invert the mask.");

			Separator();

			Draw("targetHeight", ref markForRebuild, "This allows you to set the height value the landscape will be flattened to.");
			Draw("targetStrata", ref markForRebuild, "This allows you to set the strata value the landscape will be flattened to.");
			Draw("flattenHeight", ref markForRebuild, "This allows you to set the height value the landscape will be flattened to.");
			Draw("flattenStrata", ref markForRebuild, "This allows you to set the strata value the landscape will be flattened to.");

			Separator();

			if (Button("Randomize MaskIndex") == true)
			{
				Each(tgts, t => t.MaskIndex = UnityEngine.Random.Range(0, int.MaxValue), true); markForRebuild = true;
			}

			if (markForRebuild == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}

		protected override void OnTransformChanged()
		{
			var tgt = (SgtLandscapeFlatten)target;

			if (tgt.Space == SgtLandscapeFlatten.SpaceType.Local)
			{
				tgt.MarkForRebuild();
			}
		}
	}
}
#endif