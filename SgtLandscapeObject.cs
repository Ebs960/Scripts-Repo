using Unity.Mathematics;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component snaps the current GameObject to the surface of a planet.</summary>
	[ExecuteInEditMode]
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Object")]
	public class SgtLandscapeObject : MonoBehaviour
	{
		public enum SnapType
		{
			Update,
			LateUpdate,
			FixedUpdate,
			Start
		}

		public enum SnapPositionType
		{
			Always,
			IfTooClose
		}

		public enum SnapRotationType
		{
			Always,
			Never
		}

		/// <summary>The landscape this object will snap to.
		/// None/null = Closest.</summary>
		public SgtLandscape Landscape { set { landscape = value; } get { return landscape; } } [SerializeField] private SgtLandscape landscape;

		/// <summary>This allows you to move the object up based on the surface normal in world space.</summary>
		public float Offset { set { offset = value; } get { return offset; } } [SerializeField] private float offset;

		/// <summary>The surface normal will be calculated using this sample radius in world space. Larger values = Smoother.</summary>
		public float Radius { set { radius = value; } get { return radius; } } [SerializeField] private float radius = 0.1f;

		/// <summary>This allows you to control where in the game loop the object position will be snapped.</summary>
		public SnapType SnapIn { set { snapIn = value; } get { return snapIn; } } [SerializeField] private SnapType snapIn;

		/// <summary>When should the position be snapped?</summary>
		public SnapPositionType SnapPosition { set { snapPosition = value; } get { return snapPosition; } } [SerializeField] private SnapPositionType snapPosition;

		/// <summary>When should the rotation be snapped?</summary>
		public SnapRotationType SnapRotation { set { snapRotation = value; } get { return snapRotation; } } [SerializeField] private SnapRotationType snapRotation;

		[System.NonSerialized]
		private float3 delta;

		[System.NonSerialized]
		private bool deltaSet;

		protected virtual void Start()
		{
			if (snapIn == SnapType.Start)
			{
				SnapNow();
			}
		}

		protected virtual void Update()
		{
			if (snapIn == SnapType.Update)
			{
				SnapNow();
			}
		}

		protected virtual void LateUpdate()
		{
			if (snapIn == SnapType.LateUpdate)
			{
				SnapNow();
			}
		}

		protected virtual void FixedUpdate()
		{
			if (snapIn == SnapType.FixedUpdate)
			{
				SnapNow();
			}
		}

		private SgtLandscape GetLandscape()
		{
			if (landscape == null)
			{
				return SgtLandscape.FindNearest(transform.position);
			}

			return landscape;
		}

#if UNITY_EDITOR
		protected virtual void OnDrawGizmosSelected()
		{
			var finalLandscape = GetLandscape();

			if (finalLandscape != null)
			{
				var worldPoint   = transform.position;
				var worldRight   = transform.right   * radius;
				var worldForward = transform.forward * radius;

				if (deltaSet == true)
				{
					worldPoint -= (Vector3)delta;
				}

				var surfacePoint  = finalLandscape.GetWorldPoint((float3)worldPoint);
				var surfaceNormal = finalLandscape.GetWorldNormal((float3)worldPoint, (float3)worldRight, (float3)worldForward);

				Gizmos.matrix = Matrix4x4.Translate((float3)surfacePoint) * Matrix4x4.Rotate(Quaternion.LookRotation(worldForward, (float3)surfaceNormal));
				Gizmos.DrawWireSphere(Vector3.zero, radius);
			}
		}
#endif

		/// <summary>This method updates the position and rotation of the current <b>Transform</b>.</summary>
		[ContextMenu("Snap Now")]
		private void SnapNow()
		{
			var finalLandscape = GetLandscape();

			if (finalLandscape != null && finalLandscape.IsActivated == true)
			{
				var worldPoint   = transform.position;
				var worldRight   = transform.right   * radius;
				var worldForward = transform.forward * radius;

				if (deltaSet == true && snapPosition == SnapPositionType.Always)
				{
					worldPoint -= (Vector3)delta;
				}

				var surfacePoint  = finalLandscape.GetWorldPoint((float3)worldPoint);
				var surfaceNormal = finalLandscape.GetWorldNormal((float3)worldPoint, (float3)worldRight, (float3)worldForward);

				delta    = (float3)surfaceNormal * offset;
				deltaSet = true;

				switch (snapPosition)
				{
					case SnapPositionType.Always:
					{
						transform.position = (float3)surfacePoint + delta;
					}
					break;

					case SnapPositionType.IfTooClose:
					{
						var pivot        = finalLandscape.GetWorldPivot((float3)worldPoint);
						var delta        = (float3)math.normalize((float3)worldPoint - pivot) * offset;

						surfacePoint += delta;

						var realDistance = Vector3.Distance((float3)pivot, worldPoint);
						var snapDistance = Vector3.Distance((float3)pivot, (float3)surfacePoint);

						if (realDistance < snapDistance)
						{
							transform.position = (float3)surfacePoint;
						}
					}
					break;
				}

				switch (snapRotation)
				{
					case SnapRotationType.Always:
					{
						transform.rotation = Quaternion.FromToRotation(transform.up, (float3)surfaceNormal) * transform.rotation;
					}
					break;
				}
			}
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeObject))]
	public class SgtLandscapeObject_Editor : CW.Common.CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscapeObject tgt; SgtLandscapeObject[] tgts; GetTargets(out tgt, out tgts);

			Draw("landscape", "The landscape this object will snap to.\n\nNone/null = Closest.");
			Draw("offset", "This allows you to move the object up based on the surface normal in world space.");
			Draw("radius", "The surface normal will be calculated using this sample radius in world space. Larger values = Smoother.");
			Draw("snapIn", "This allows you to control where in the game loop the object position will be snapped.");
			Draw("snapPosition", "When should the position be snapped?");
			Draw("snapRotation", "When should the rotation be snapped?");
		}
	}
}
#endif