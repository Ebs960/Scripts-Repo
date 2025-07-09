using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This component can be added alongside a terrain to benchmark how long it takes to generate.</summary>
	[RequireComponent(typeof(SgtLandscape))]
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Benchmark")]
	public class SgtLandscapeBenchmark : MonoBehaviour
	{
		[System.NonSerialized]
		private System.Diagnostics.Stopwatch benchmark;

		[System.NonSerialized]
		private SgtLandscape parent;

		protected virtual void OnEnable()
		{
			parent = GetComponent<SgtLandscape>();
		}

		protected virtual void LateUpdate()
		{
			if (benchmark == null && parent.IsActivated == true)
			{
				benchmark = System.Diagnostics.Stopwatch.StartNew();

				parent.ForceUpdateLOD();

				benchmark.Stop();

				Debug.Log("BENCHMARK: " + name + " took " + benchmark.ElapsedMilliseconds + "ms to generate.");
			}
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeBenchmark))]
	public class SgtLandscapeBenchmark_Editor : CW.Common.CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscapeBenchmark tgt; SgtLandscapeBenchmark[] tgts; GetTargets(out tgt, out tgts);

			Info("This component will time how long it took to generate the landscape. For this to work best, the landscape's Detail setting should be set to a high value.");
		}
	}
}
#endif