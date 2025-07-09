using Unity.Mathematics;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component can be added to a child GameObject of your landscape. It will color the landscape based on its height and slope data using a gradient texture.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Color")]
	public class SgtLandscapeColor : SgtLandscapeFeature
	{
		public enum SpaceType
		{
			Global,
			Local
		}

		/// <summary>Where should the color be applied?
		/// Global = The whole landscape.
		/// Local = A small section of the landscape defined by the <b>Transform</b> component's position, rotation, and scale.</summary>
		public SpaceType Space { set { space = value; } get { return space; } } [SerializeField] private SpaceType space;

		/// <summary>Should this layer of detail be masked?</summary>
		public bool Mask { set { mask = value; } get { return mask; } } [SerializeField] protected bool mask;

		/// <summary>The mask texture used by this component.</summary>
		public int MaskIndex { set { maskIndex = value; } get { return maskIndex; } } [SerializeField] protected int maskIndex;

		/// <summary>This allows you to invert the mask.</summary>
		public bool MaskInvert { set { maskInvert = value; } get { return maskInvert; } } [SerializeField] private bool maskInvert;

		/// <summary>This allows you to set how sharp or smooth the mask transition is.</summary>
		public float MaskSharpness { set { maskSharpness = value; } get { return maskSharpness; } } [SerializeField] [Range(1.0f, 100.0f)] private float maskSharpness = 1.0f;

		/// <summary>This allows you to offset the mask based on the topology.</summary>
		public float MaskGlobalShift { set { maskGlobalShift = value; } get { return maskGlobalShift; } } [SerializeField] [Range(-1.0f, 1.0f)] private float maskGlobalShift;

		/// <summary>This allows you to enhance the detail around the edges of the mask.</summary>
		public bool MaskDetail { set { maskDetail = value; } get { return maskDetail; } } [SerializeField] private bool maskDetail;

		/// <summary>This allows you to enhance the detail around the edges of the mask.</summary>
		public int MaskDetailIndex { set { maskDetailIndex = value; } get { return maskDetailIndex; } } [SerializeField] private int maskDetailIndex;

		/// <summary>This allows you to adjust the mask detail texture tiling.</summary>
		public Vector2 MaskDetailTiling { set { maskDetailTiling = value; } get { return maskDetailTiling; } } [SerializeField] private Vector2 maskDetailTiling = new Vector2(10.0f, 10.0f);

		/// <summary>This allows you to adjust the mask detail texture tiling.</summary>
		public float MaskDetailOffset { set { maskDetailOffset = value; } get { return maskDetailOffset; } } [SerializeField] [Range(0.0001f, 0.1f)] private float maskDetailOffset = 0.1f;

		/// <summary>This allows you to specify which gradient texture is used.</summary>
		public int GradientIndex { set { gradientIndex = value; } get { return gradientIndex; } } [SerializeField] private int gradientIndex;

		/// <summary>This allows you to adjust which part of the <b>GradientTex</b> is used.</summary>
		public float Variation { set { variation = value; } get { return variation; } } [SerializeField] [Range(0.0f, 1.0f)] private float variation = 0.0f;

		/// <summary>This allows you to adjust which part of the <b>GradientTex</b> is used.</summary>
		public float Offset { set { offset = value; } get { return offset; } } [SerializeField] [Range(0.0f, 1.0f)] private float offset = 0.0f;

		/// <summary>This allows you to adjust the amount of ambient occlusion darkening applied to the slopes of terrain features.</summary>
		public float Occlusion { set { occlusion = value; } get { return occlusion; } } [SerializeField] [Range(0.0f, 10.0f)] private float occlusion = 1.0f;

		/// <summary>This allows you to adjust how many layers of strata appear in the terrain depending on its height.</summary>
		public float Strata { set { strata = value; } get { return strata; } } [SerializeField] private float strata = 1.0f;

		/// <summary>This allows you to smooth out the strata colors.</summary>
		public float Blur { set { blur = value; } get { return blur; } } [SerializeField] [Range(0.0f, 10.0f)] [UnityEngine.Serialization.FormerlySerializedAs("smooth")] private float blur;

		/// <summary>The smoothness will reach its maximum value when the gradient color is at this luminosity.</summary>
		public float SmoothnessMidpoint { set { smoothnessMidpoint = value; } get { return smoothnessMidpoint; } } [SerializeField] [Range(0.0f, 1.0f)] private float smoothnessMidpoint = 0.5f;

		/// <summary>This allows you to set the maximum smoothness value.</summary>
		public float SmoothnessStrength { set { smoothnessStrength = value; } get { return smoothnessStrength; } } [SerializeField] [Range(0.0f, 1.0f)] private float smoothnessStrength;

		/// <summary>This allows you to set the sharpness of the smoothness relative to how close the gradient color is to the <b>SmoothnessMidpoint</b>.</summary>
		public float SmoothnessPower { set { smoothnessPower = value; } get { return smoothnessPower; } } [SerializeField] [Range(0.01f, 100.0f)] private float smoothnessPower = 2.0f;

		/// <summary>The emission will reach its maximum value when the gradient color is at this luminosity.</summary>
		public float EmissionMidpoint { set { emissionMidpoint = value; } get { return emissionMidpoint; } } [SerializeField] [Range(0.0f, 1.0f)] private float emissionMidpoint = 0.5f;

		/// <summary>This allows you to set the maximum emission value.</summary>
		public float EmissionStrength { set { emissionStrength = value; } get { return emissionStrength; } } [SerializeField] [Range(0.0f, 1.0f)] private float emissionStrength;

		/// <summary>This allows you to set the sharpness of the emission relative to how close the gradient color is to the <b>EmissionMidpoint</b>.</summary>
		public float EmissionPower { set { emissionPower = value; } get { return emissionPower; } } [SerializeField] [Range(0.01f, 100.0f)] private float emissionPower = 2.0f;

		public static SgtLandscapeColor Create(int layer = 0, Transform parent = null)
		{
			return Create(layer, parent, Vector3.zero, Quaternion.identity, Vector3.one);
		}

		public static SgtLandscapeColor Create(int layer, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
		{
			return CW.Common.CwHelper.CreateGameObject("SgtLandscapeColor", layer, parent, localPosition, localRotation, localScale).AddComponent<SgtLandscapeColor>();
		}

		public override void Prepare()
		{
		}

		public override void Dispose()
		{
		}

		public override void ScheduleCpu(SgtLandscape.PendingPoints pending)
		{
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

				if (mask == true)
				{
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
		}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeColor))]
	public class SgtLandscapeColor_Editor : SgtLandscapeFeature_Editor
	{
		protected override void OnInspector()
		{
			SgtLandscapeColor tgt; SgtLandscapeColor[] tgts; GetTargets(out tgt, out tgts);

			var markForRebuild = false;

			Draw("space", ref markForRebuild, "Where should the color be applied?\n\nGlobal = The whole landscape.\n\nLocal = A small section of the landscape defined by the <b>Transform</b> component's position, rotation, and scale.");
			Draw("mask", ref markForRebuild, "Should this layer of detail be masked?");

			if (Any(tgts, t => t.Mask == true))
			{
				BeginIndent();
					Draw("maskIndex", ref markForRebuild, "The mask texture used by this component.");
					Draw("maskInvert", ref markForRebuild, "This allows you to invert the mask.");
					Draw("maskSharpness", ref markForRebuild, "This allows you to set how sharp or smooth the mask transition is.");
					if (Any(tgts, t => t.Space == SgtLandscapeColor.SpaceType.Global))
					{
						Draw("maskGlobalShift", ref markForRebuild, "This allows you to offset the mask based on the topology.");
					}
					Draw("maskDetail", ref markForRebuild, "This allows you to enhance the detail around the edges of the mask.");

					if (Any(tgts, t => t.MaskDetail == true))
					{
						BeginIndent();
							Draw("maskDetailIndex", ref markForRebuild, "This allows you to enhance the detail around the edges of the mask.");
							Draw("maskDetailTiling", ref markForRebuild, "This allows you to adjust the mask detail texture tiling.");
							Draw("maskDetailOffset", ref markForRebuild, "This allows you to maximum amount the mask can be shifted based on the <b>MaskDetail</b> texture in UV space.");
						EndIndent();
					}
				EndIndent();
			}

			Separator();

			Draw("gradientIndex", ref markForRebuild, "This allows you to specify which gradient texture is used.\n\nNOTE: The actual texture is in the <b>SgtLandscapeBundle</b> component set in the parent landscape component's <b>Bundle</b> setting.");
			Draw("variation", ref markForRebuild, "This allows you to adjust which part of the <b>GradientTex</b> is used.");
			Draw("offset", ref markForRebuild, "This allows you to adjust which part of the <b>GradientTex</b> is used.");
			Draw("occlusion", ref markForRebuild, "This allows you to adjust the amount of ambient occlusion darkening applied to the slopes of terrain features.");
			Draw("strata", ref markForRebuild, "This allows you to adjust how many layers of strata appear in the terrain depending on its height.");
			Draw("blur", ref markForRebuild, "This allows you to smooth out the strata colors.");
			Draw("smoothnessMidpoint", ref markForRebuild, "The smoothness will reach its maximum value when the gradient color is at this luminosity.");
			Draw("smoothnessStrength", ref markForRebuild, "This allows you to set the maximum smoothness value.");
			Draw("smoothnessPower", ref markForRebuild, "This allows you to set the sharpness of the smoothness relative to how close the gradient color is to the <b>SmoothnessMidpoint</b>.");
			Draw("emissionMidpoint", ref markForRebuild, "The emission will reach its maximum value when the gradient color is at this luminosity.");
			Draw("emissionStrength", ref markForRebuild, "This allows you to set the maximum emission value.");
			Draw("emissionPower", ref markForRebuild, "This allows you to set the sharpness of the emission relative to how close the gradient color is to the <b>EmissionMidpoint</b>.");

			Separator();

			if (Any(tgts, t => t.Mask == true) && Button("Randomize MaskIndex") == true)
			{
				Each(tgts, t => t.MaskIndex = UnityEngine.Random.Range(0, int.MaxValue), true); markForRebuild = true;
			}

			if (Button("Randomize GradientIndex") == true)
			{
				Each(tgts, t => t.GradientIndex = UnityEngine.Random.Range(0, int.MaxValue), true); markForRebuild = true;
			}

			if (Button("Randomize Variation") == true)
			{
				Each(tgts, t => t.Variation = SgtLandscape_Editor.Randomize01(t.Variation, ref markForRebuild), true);
			}

			if (markForRebuild == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}

		protected override void OnTransformChanged()
		{
			var tgt = (SgtLandscapeColor)target;

			if (tgt.Space == SgtLandscapeColor.SpaceType.Local)
			{
				tgt.MarkForRebuild();
			}
		}
	}
}
#endif