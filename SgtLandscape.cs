using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>This is the base class for all landscape types.</summary>
	[ExecuteInEditMode]
	[DefaultExecutionOrder(1000)]
	public abstract partial class SgtLandscape : MonoBehaviour
	{
		public enum DeformType
		{
			Default,
			Sphere
		}

		public struct TriangleHash : System.IEquatable<TriangleHash>
		{
			private double3x3 data;

			public TriangleHash(double3 a, double3 b, double3 c)
			{
				data.c0 = a;
				data.c1 = b;
				data.c2 = c;

				if (CompareVectors(data.c0, data.c1) > 0) Swap(ref data.c0, ref data.c1);
				if (CompareVectors(data.c0, data.c2) > 0) Swap(ref data.c0, ref data.c2);
				if (CompareVectors(data.c1, data.c2) > 0) Swap(ref data.c1, ref data.c2);
			}

			private static int CompareVectors(double3 a, double3 b)
			{
				if (a.x != b.x) return a.x < b.x ? -1 : 1;
				if (a.y != b.y) return a.y < b.y ? -1 : 1;
				if (a.z != b.z) return a.z < b.z ? -1 : 1;
				return 0;
			}

			private static void Swap(ref double3 a, ref double3 b)
			{
				var temp = a;
				a = b;
				b = temp;
			}

			public bool Equals(TriangleHash other)
			{
				return data.Equals(other.data);
			}

			public override bool Equals(object obj)
			{
				return obj is TriangleHash other && Equals(other);
			}

			public override int GetHashCode()
			{
				return data.GetHashCode();
			}
		}

		public struct Triangle
		{
			public double3 PositionA;
			public double3 PositionB;
			public double3 PositionC;

			public int          Depth;
			public bool         Split;
			public TriangleHash Hash;

			private static List<double3> Three = new List<double3>(3);

			public Triangle(double3 a, double3 b, double3 c, int depth, bool split)
			{
				PositionA  = a;
				PositionB  = b;
				PositionC  = c;
				Depth      = depth;
				Split      = split;
				Hash       = new TriangleHash(a, b, c);
			}

			public double3 Pivot1
			{
				get
				{
					var a = math.min(PositionA, PositionB);
					var b = math.max(PositionA, PositionB);
					return (a + b) * 0.5f;
				}
			}

			public double3 Pivot2
			{
				get
				{
					var a = math.min(PositionB, PositionC);
					var b = math.max(PositionB, PositionC);
					return (a + b) * 0.5f;
				}
			}

			public double3 Pivot3
			{
				get
				{
					var a = math.min(PositionC, PositionA);
					var b = math.max(PositionC, PositionA);
					return (a + b) * 0.5f;
				}
			}

			public double Size1
			{
				get
				{
					var a = math.min(PositionA, PositionB);
					var b = math.max(PositionA, PositionB);
					return math.length(a.xyz - b.xyz);
				}
			}

			public double Size2
			{
				get
				{
					var a = math.min(PositionB, PositionC);
					var b = math.max(PositionB, PositionC);
					return math.length(a.xyz - b.xyz);
				}
			}

			public double Size3
			{
				get
				{
					var a = math.min(PositionC, PositionA);
					var b = math.max(PositionC, PositionA);
					return math.length(a.xyz - b.xyz);
				}
			}
		}

		/// <summary>This allows you to control the LOD detail while in edit mode.</summary>
		public float EditorDetail { set { editorDetail = value; } get { return editorDetail; } } [SerializeField] [Range(1.0f, 20.0f)] private float editorDetail = 1.0f;

		/// <summary>When entering play mode or running the actual game, the landscape will initialize with this level of detail.
		/// NOTE: 0 means it will initialize with the minimum amount of triangles, and will generate very quickly.
		/// Higher values will make the planet immediately look better, but may cause the scene to freeze until it's generated.</summary>
		public float InitDetail { set { initDetail = value; } get { return initDetail; } } [SerializeField] [Range(0.0f, 20.0f)] private float initDetail;

		/// <summary>The overall detail of the landscape relative to the camera distance. The higher you set this, the more triangles it will have.</summary>
		public float Detail { set { detail = value; } get { return detail; } } [SerializeField] [Range(1.0f, 20.0f)] private float detail = 5.0f;

		/// <summary>The maximum LOD depth this landscape can have.</summary>
		public float MinimumTriangleSize { set { minimumTriangleSize = value; } get { return minimumTriangleSize; } } [SerializeField] private float minimumTriangleSize = 0.1f;

		/// <summary>The maximum amount of seconds we can budget for LOD.</summary>
		public float LodBudget { set { lodBudget = value; } get { return lodBudget; } } [SerializeField] private float lodBudget = 0.001f;

		/// <summary>The maximum LOD chunks that can be generated between each LOD change. For example, if your camera suddenly travels to the planet surface then many LOD chunks will need to be generated. If you set LOD Steps to a high value, then it will take a while for any changes to appear, whereas a low value means the landscape will constantly be updating to the final LOD state.</summary>
		public int LodSteps { set { lodSteps = value; } get { return lodSteps; } } [SerializeField] protected int lodSteps = 30;

		/// <summary>The landscape will be rendered using this Material.
		/// NOTE: This material must use a shader based on the SGT/Landscape shader.</summary>
		public Material Material { set { material = value; } get { return material; } } [SerializeField] private Material material;

		/// <summary>The landscape will use textures from this texture bundle.</summary>
		public SgtLandscapeBundle Bundle { set { bundle = value; } get { return bundle; } } [SerializeField] protected SgtLandscapeBundle bundle;

		/// <summary>To support massive planets, the global detail/biome UV sizes must be baked into the mesh. This allows you to set up to 4 different tile sizes.</summary>
		public Vector4 GlobalSizes { set { globalSizes = value; } get { return globalSizes; } } [SerializeField] protected Vector4 globalSizes = new Vector4(1.0f, 100.0f, 10000.0f, 1000000.0f);

		public Vector4 GlobalTiling { get { return globalTiling; } } [SerializeField] protected Vector4 globalTiling;

		public Vector4 GlobalTilingNormalized { get { return globalTilingNormalized; } } [SerializeField] protected Vector4 globalTilingNormalized;

		/// <summary>The landscape LOD will be based on these transform positions.
		/// None/null = The GameObject with the <b>MainCamera</b> tag will be used.</summary>
		public List<Transform> Observers { get { if (observers == null) observers = new List<Transform>(); return observers; } } [SerializeField] private List<Transform> observers;

		/// <summary>Should the landscape cast Unity built-in shadows?</summary>
		public bool CastShadows { set { castShadows = value; } get { return castShadows; } } [SerializeField] private bool castShadows = true;

		/// <summary>Should the landscape receive Unity's built-in shadows?</summary>
		public bool ReceiveShadows { set { receiveShadows = value; } get { return receiveShadows; } } [SerializeField] private bool receiveShadows = true;

		//public bool DetachMesh { set { detachMesh = value; } get { return detachMesh; } } [SerializeField] private bool detachMesh;

		[System.NonSerialized]
		private static readonly int PIXEL_WIDTH = 136 * 3; //private static readonly int PIXEL_WIDTH = 136 * 3;

		[System.NonSerialized]
		private static readonly int PIXEL_HEIGHT = 136; //private static readonly int PIXEL_HEIGHT = 136;

		private static readonly int ATLAS_COLUMNS = 5;

		private static readonly int ATLAS_ROWS = 7; // 15

		private static readonly int GLOBAL_DETAIL_CAPACITY = 8;

		private static readonly int LOCAL_DETAIL_CAPACITY = 32;

		private static readonly int GLOBAL_FLATTEN_CAPACITY = 8;

		private static readonly int LOCAL_FLATTEN_CAPACITY = 32;

		private static readonly int GLOBAL_COLOR_CAPACITY = 8;

		private static readonly int LOCAL_COLOR_CAPACITY = 32;

		[System.NonSerialized]
		protected NativeList<Triangle> topology;

		[System.NonSerialized]
		protected NativeList<double3> cameraPositions;

		[System.NonSerialized]
		protected NativeList<Triangle> triangles;

		[System.NonSerialized] protected NativeList<Triangle> createDiffs;
		[System.NonSerialized] protected NativeList<Triangle> deleteDiffs;
		[System.NonSerialized] protected NativeList<Triangle> statusDiffs;

		[System.NonSerialized]
		private float lodWeight = 1.0f;

		[System.NonSerialized]
		protected SgtLandscapeBundle registeredBundle;

		public static LinkedList<SgtLandscape> AllLandscapes = new LinkedList<SgtLandscape>();

		private LinkedListNode<SgtLandscape> node;

		public event System.Action<Visual, PendingTriangle> OnAddVisual;

		public event System.Action<Visual> OnRemoveVisual;

		public event System.Action<Visual> OnShowVisual;

		public event System.Action<Visual> OnHideVisual;

		[System.NonSerialized]
		private bool markForRebuild;

		[System.NonSerialized]
		private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

		[System.NonSerialized]
		protected PendingUpdate pendingUpdate = new PendingUpdate();

		[System.NonSerialized]
		private List<PendingTriangle> pendingTriangles = new List<PendingTriangle>();

		[System.NonSerialized]
		protected PendingPoints cameraPoints;

		[System.NonSerialized]
		protected List<SgtLandscapeFeature> features = new List<SgtLandscapeFeature>();

		[System.NonSerialized] private int       globalDetailCount;
		[System.NonSerialized] private Vector4[] globalDetailDataA = new Vector4[GLOBAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[] globalDetailDataB = new Vector4[GLOBAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[] globalDetailDataC = new Vector4[GLOBAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[] globalDetailDataD = new Vector4[GLOBAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[] globalDetailLayer = new Vector4[GLOBAL_DETAIL_CAPACITY];

		[System.NonSerialized] private int         localDetailCount;
		[System.NonSerialized] private Vector4[]   localDetailDataA  = new Vector4[LOCAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[]   localDetailDataB  = new Vector4[LOCAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[]   localDetailDataC  = new Vector4[LOCAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Vector4[]   localDetailDataD  = new Vector4[LOCAL_DETAIL_CAPACITY];
		[System.NonSerialized] private Matrix4x4[] localDetailMatrix = new Matrix4x4[LOCAL_DETAIL_CAPACITY];

		[System.NonSerialized] private int       globalFlattenCount;
		[System.NonSerialized] private Vector4[] globalFlattenDataA = new Vector4[GLOBAL_FLATTEN_CAPACITY];
		[System.NonSerialized] private Vector4[] globalFlattenDataC = new Vector4[GLOBAL_FLATTEN_CAPACITY];

		[System.NonSerialized] private int         localFlattenCount;
		[System.NonSerialized] private Vector4[]   localFlattenDataA  = new Vector4[LOCAL_FLATTEN_CAPACITY];
		[System.NonSerialized] private Vector4[]   localFlattenDataC  = new Vector4[LOCAL_FLATTEN_CAPACITY];
		[System.NonSerialized] private Matrix4x4[] localFlattenMatrix = new Matrix4x4[LOCAL_FLATTEN_CAPACITY];

		[System.NonSerialized] private int       globalColorCount;
		[System.NonSerialized] private Vector4[] globalColorDataA = new Vector4[GLOBAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[] globalColorDataB = new Vector4[GLOBAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[] globalColorDataC = new Vector4[GLOBAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[] globalColorDataD = new Vector4[GLOBAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[] globalColorDataE = new Vector4[GLOBAL_COLOR_CAPACITY];

		[System.NonSerialized] private int         localColorCount;
		[System.NonSerialized] private Vector4[]   localColorDataA  = new Vector4[LOCAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[]   localColorDataB  = new Vector4[LOCAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[]   localColorDataC  = new Vector4[LOCAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[]   localColorDataD  = new Vector4[LOCAL_COLOR_CAPACITY];
		[System.NonSerialized] private Vector4[]   localColorDataE  = new Vector4[LOCAL_COLOR_CAPACITY];
		[System.NonSerialized] private Matrix4x4[] localColorMatrix = new Matrix4x4[LOCAL_COLOR_CAPACITY];

		protected static readonly int _CwMatrix        = Shader.PropertyToID("_CwMatrix");
		protected static readonly int _CwSize          = Shader.PropertyToID("_CwSize");
		protected static readonly int _CwPositionA     = Shader.PropertyToID("_CwPositionA");
		protected static readonly int _CwPositionB     = Shader.PropertyToID("_CwPositionB");
		protected static readonly int _CwPositionC     = Shader.PropertyToID("_CwPositionC");
		protected static readonly int _CwCoordX        = Shader.PropertyToID("_CwCoordX");
		protected static readonly int _CwCoordY        = Shader.PropertyToID("_CwCoordY");
		protected static readonly int _CwCoordZ        = Shader.PropertyToID("_CwCoordZ");
		protected static readonly int _CwCoordW        = Shader.PropertyToID("_CwCoordW");
		protected static readonly int _CwGlobalTiling  = Shader.PropertyToID("_CwGlobalTiling");
		protected static readonly int _CwDepth         = Shader.PropertyToID("_CwDepth");
		protected static readonly int _CwRadius        = Shader.PropertyToID("_CwRadius");
		protected static readonly int _CwSquareSize    = Shader.PropertyToID("_CwSquareSize");
		protected static readonly int _CwAlbedo        = Shader.PropertyToID("_CwAlbedo");
		protected static readonly int _CwAlbedoSize    = Shader.PropertyToID("_CwAlbedoSize");
		protected static readonly int _CwTopology      = Shader.PropertyToID("_CwTopology");
		protected static readonly int _CwTopologySize  = Shader.PropertyToID("_CwTopologySize");
		protected static readonly int _CwTopologyData  = Shader.PropertyToID("_CwTopologyData");
		protected static readonly int _SGT_CloudTex     = Shader.PropertyToID("_SGT_CloudTex");
		protected static readonly int _SGT_CloudMatrix  = Shader.PropertyToID("_SGT_CloudMatrix");
		protected static readonly int _SGT_CloudOpacity = Shader.PropertyToID("_SGT_CloudOpacity");
		protected static readonly int _SGT_CloudWarp    = Shader.PropertyToID("_SGT_CloudWarp");

		protected static readonly int _CwBufferP          = Shader.PropertyToID("_CwBufferP");
		protected static readonly int _CwWeights          = Shader.PropertyToID("_CwWeights");
		protected static readonly int _SGT_WeightTex      = Shader.PropertyToID("_SGT_WeightTex");
		protected static readonly int _CwCoords           = Shader.PropertyToID("_CwCoords");
		protected static readonly int _CwPixelSize        = Shader.PropertyToID("_CwPixelSize");
		protected static readonly int _CwVertexResolution = Shader.PropertyToID("_CwVertexResolution");

		protected static readonly int _CwLocalDetailCount  = Shader.PropertyToID("_CwLocalDetailCount");
		protected static readonly int _CwLocalDetailDataA  = Shader.PropertyToID("_CwLocalDetailDataA");
		protected static readonly int _CwLocalDetailDataB  = Shader.PropertyToID("_CwLocalDetailDataB");
		protected static readonly int _CwLocalDetailDataC  = Shader.PropertyToID("_CwLocalDetailDataC");
		protected static readonly int _CwLocalDetailDataD  = Shader.PropertyToID("_CwLocalDetailDataD");
		protected static readonly int _CwLocalDetailMatrix = Shader.PropertyToID("_CwLocalDetailMatrix");

		protected static readonly int _CwGlobalDetailCount = Shader.PropertyToID("_CwGlobalDetailCount");
		protected static readonly int _CwGlobalDetailDataA = Shader.PropertyToID("_CwGlobalDetailDataA");
		protected static readonly int _CwGlobalDetailDataB = Shader.PropertyToID("_CwGlobalDetailDataB");
		protected static readonly int _CwGlobalDetailDataC = Shader.PropertyToID("_CwGlobalDetailDataC");
		protected static readonly int _CwGlobalDetailDataD = Shader.PropertyToID("_CwGlobalDetailDataD");
		protected static readonly int _CwGlobalDetailLayer = Shader.PropertyToID("_CwGlobalDetailLayer");

		protected static readonly int _CwLocalFlattenCount  = Shader.PropertyToID("_CwLocalFlattenCount");
		protected static readonly int _CwLocalFlattenDataA  = Shader.PropertyToID("_CwLocalFlattenDataA");
		protected static readonly int _CwLocalFlattenDataC  = Shader.PropertyToID("_CwLocalFlattenDataC");
		protected static readonly int _CwLocalFlattenMatrix = Shader.PropertyToID("_CwLocalFlattenMatrix");

		protected static readonly int _CwGlobalFlattenCount = Shader.PropertyToID("_CwGlobalFlattenCount");
		protected static readonly int _CwGlobalFlattenDataA = Shader.PropertyToID("_CwGlobalFlattenDataA");
		protected static readonly int _CwGlobalFlattenDataC = Shader.PropertyToID("_CwGlobalFlattenDataC");
		protected static readonly int _CwGlobalFlattenLayer = Shader.PropertyToID("_CwGlobalFlattenLayer");
		
		protected static readonly int _CwLocalColorCount  = Shader.PropertyToID("_CwLocalColorCount");
		protected static readonly int _CwLocalColorDataA  = Shader.PropertyToID("_CwLocalColorDataA");
		protected static readonly int _CwLocalColorDataB  = Shader.PropertyToID("_CwLocalColorDataB");
		protected static readonly int _CwLocalColorDataC  = Shader.PropertyToID("_CwLocalColorDataC");
		protected static readonly int _CwLocalColorDataD  = Shader.PropertyToID("_CwLocalColorDataD");
		protected static readonly int _CwLocalColorDataE  = Shader.PropertyToID("_CwLocalColorDataE");
		protected static readonly int _CwLocalColorMatrix = Shader.PropertyToID("_CwLocalColorMatrix");

		protected static readonly int _CwGlobalColorCount = Shader.PropertyToID("_CwGlobalColorCount");
		protected static readonly int _CwGlobalColorDataA = Shader.PropertyToID("_CwGlobalColorDataA");
		protected static readonly int _CwGlobalColorDataB = Shader.PropertyToID("_CwGlobalColorDataB");
		protected static readonly int _CwGlobalColorDataC = Shader.PropertyToID("_CwGlobalColorDataC");
		protected static readonly int _CwGlobalColorDataD = Shader.PropertyToID("_CwGlobalColorDataD");
		protected static readonly int _CwGlobalColorDataE = Shader.PropertyToID("_CwGlobalColorDataE");

		protected static readonly int _CwOrigins       = Shader.PropertyToID("_CwOrigins");
		protected static readonly int _CwPositionsA    = Shader.PropertyToID("_CwPositionsA");
		protected static readonly int _CwPositionsB    = Shader.PropertyToID("_CwPositionsB");
		protected static readonly int _CwPositionsC    = Shader.PropertyToID("_CwPositionsC");
		protected static readonly int _CwOffset        = Shader.PropertyToID("_CwOffset");
		protected static readonly int _CwObjectToWorld = Shader.PropertyToID("_CwObjectToWorld");
		protected static readonly int _CwWorldToObject = Shader.PropertyToID("_CwWorldToObject");

		protected static readonly int _CwHeightTopologyAtlas = Shader.PropertyToID("_CwHeightTopologyAtlas");
		protected static readonly int _CwMaskTopologyAtlas   = Shader.PropertyToID("_CwMaskTopologyAtlas");
		protected static readonly int _CwGradientAtlas       = Shader.PropertyToID("_CwGradientAtlas");
		protected static readonly int _CwDetailAtlas         = Shader.PropertyToID("_CwDetailAtlas");

		public bool GetTilingLayer(float size, ref int index, ref int tiling)
		{
			if (size > 0.0f)
			{
				if (size <= globalSizes.x)
				{
					index  = 0;
					tiling = (int)math.round(globalSizes.x / size);

					return true;
				}

				if (size <= globalSizes.y)
				{
					index  = 1;
					tiling = (int)math.round(globalSizes.y / size);

					return true;
				}

				if (size <= globalSizes.z)
				{
					index  = 2;
					tiling = (int)math.round(globalSizes.z / size);

					return true;
				}

				if (size <= globalSizes.w)
				{
					index  = 3;
					tiling = (int)math.round(globalSizes.w / size);

					return true;
				}
			}

			return false;
		}

		public int GetTriangleDepth(int triangleIndex)
		{
			return triangles[triangleIndex].Depth;
		}

		/// <summary>This will call <b>Rebuild</b> the next time this landscape updates.</summary>
		public void MarkForRebuild()
		{
			markForRebuild = true;
		}

		/// <summary>This method will completely reset the landscape and rebuild it from scratch. You can call this if you've modified a setting or feature that causes the landscape to change.</summary>
		[ContextMenu("Rebuild")]
		public void Rebuild()
		{
			if (triangles.IsCreated == true)
			{
				Dispose();
			}

			TryGenerateMeshData();

			Prepare();

#if UNITY_EDITOR
			if (Application.isPlaying == true)
			{
				UpdateLod(initDetail, int.MaxValue);
			}
			else
			{
				UpdateLod(editorDetail, int.MaxValue);
			}
#else
			UpdateLod(initDetail, int.MaxValue);
#endif
		}

		[System.NonSerialized]
		private static PendingTriangle rootPending;

		public abstract int CalculateLodDepth(float triangleSize);

		public abstract double3 GetLocalPoint(double3 localPoint);

		public bool IsActivated
		{
			get
			{
				return triangles.IsCreated == true;
			}
		}

		public int GetMaskIndex(int index)
		{
			if (index >= 0 && registeredBundle != null && registeredBundle.MaskTextures.Count > 0)
			{
				return index % registeredBundle.MaskTextures.Count;
			}

			return 0;
		}

		public int GetGradientIndex(int index)
		{
			if (index >= 0 && registeredBundle != null && registeredBundle.GradientTextures.Count > 0)
			{
				return index % registeredBundle.GradientTextures.Count;
			}

			return 0;
		}

		public int GetDetailIndex(int index)
		{
			if (index >= 0 && registeredBundle != null && registeredBundle.DetailTextures.Count > 0)
			{
				return index % registeredBundle.DetailTextures.Count;
			}

			return 0;
		}

		public int GetHeightIndex(int index)
		{
			if (index >= 0 && registeredBundle != null && registeredBundle.HeightTextures.Count > 0)
			{
				return index % registeredBundle.HeightTextures.Count;
			}

			return 0;
		}

		public Texture GetMaskTexture(int index)
		{
			if (index >= 0 && registeredBundle != null && registeredBundle.MaskTextures.Count > 0)
			{
				return registeredBundle.MaskTextures[index % registeredBundle.MaskTextures.Count];
			}

			return null;
		}

		public double3 GetWorldPoint(double3 worldPoint) // TODO: Implement this with doubles
		{
			var localPoint = transform.InverseTransformPoint((float3)worldPoint);

			if (triangles.IsCreated == true)
			{
				localPoint = (float3)GetLocalPoint((double3)(float3)localPoint);
			}

			return (float3)transform.TransformPoint(localPoint);
		}

		public abstract double3 GetWorldPivot(double3 worldPoint);

		private static double3 GetNormal(double3 vectorA, double3 vectorB, double length)
		{
			var smallsq = length * 0.1; smallsq *= smallsq;

			if (math.lengthsq(vectorA) < smallsq)
			{
				return vectorB;
			}

			if (math.lengthsq(vectorB) < smallsq)
			{
				return vectorA;
			}

			return -math.cross(vectorA, vectorB);
		}

		public double3 GetWorldNormal(double3 worldPoint, double3 worldRight, double3 worldForward) // TODO: Implement this with doubles
		{
			var sampledPointL = GetWorldPoint(worldPoint - worldRight  );
			var sampledPointR = GetWorldPoint(worldPoint + worldRight  );
			var sampledPointB = GetWorldPoint(worldPoint - worldForward);
			var sampledPointF = GetWorldPoint(worldPoint + worldForward);

			var vectorA = sampledPointR - sampledPointL;
			var vectorB = sampledPointF - sampledPointB;

			var sampledNormal = math.normalize(GetNormal(vectorA, vectorB, math.length(worldRight)));

			if (math.dot(sampledNormal, sampledPointL - (float3)transform.position) < 0.0f)
			{
				sampledNormal = -sampledNormal;
			}

			return sampledNormal;
		}

		public double3 GetLocalNormal(double3 localPoint, double3 localRight, double3 localForward)
		{
			var sampledPointL = GetLocalPoint(localPoint - localRight  );
			var sampledPointR = GetLocalPoint(localPoint + localRight  );
			var sampledPointB = GetLocalPoint(localPoint - localForward);
			var sampledPointF = GetLocalPoint(localPoint + localForward);

			var vectorA = sampledPointR - sampledPointL;
			var vectorB = sampledPointF - sampledPointB;

			var sampledNormal = math.normalize(GetNormal(vectorA, vectorB, math.length(localRight)));

			if (math.dot(sampledNormal, sampledPointL - (float3)transform.position) < 0.0f)
			{
				sampledNormal = -sampledNormal;
			}

			return sampledNormal;
		}

		/// <summary>This method returns the nearest <b>SgtLandscape</b> to the specified world point.</summary>
		public static SgtLandscape FindNearest(Vector3 worldPosition)
		{
			var bestDistance  = float.PositiveInfinity;
			var bestLandscape = default(SgtLandscape);

			foreach (var landscape in AllLandscapes)
			{
				var distance = landscape.GetApproximateWorldDistance(worldPosition);

				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestLandscape  = landscape;
				}
			}

			return bestLandscape;
		}

		public abstract float GetApproximateWorldDistance(Vector3 worldPoint);

		protected virtual void OnEnable()
		{
			node = AllLandscapes.AddLast(this);
		}

		protected virtual void OnDisable()
		{
			AllLandscapes.Remove(node); node = null;

			if (triangles.IsCreated == true)
			{
				Dispose();
			}

			if (AllLandscapes.Count == 0)
			{
				TryDisposeMeshData();
			}
		}

		protected virtual void Prepare()
		{
			markForRebuild = false;

			topology        = new NativeList<Triangle>(Allocator.Persistent);
			cameraPositions = new NativeList<double3>(Allocator.Persistent);
			triangles       = new NativeList<Triangle>(4096, Allocator.Persistent);
			createDiffs     = new NativeList<Triangle>(4096, Allocator.Persistent);
			deleteDiffs     = new NativeList<Triangle>(4096, Allocator.Persistent);
			statusDiffs     = new NativeList<Triangle>(4096, Allocator.Persistent);
			cameraPoints    = new PendingPoints(1);

			if (bundle != null)
			{
				registeredBundle = bundle;

				registeredBundle.AddRef();
			}

			globalDetailCount = 0;
			localDetailCount = 0;
			globalFlattenCount = 0;
			localFlattenCount = 0;
			globalColorCount = 0;
			localColorCount = 0;

			features.Clear();

			GetComponentsInChildren(features);

			features.RemoveAll(f => f.transform.parent != transform);

			foreach (var feature in features)
			{
				feature.Prepare();

				if (feature is SgtLandscapeBiome)
				{
					var f = (SgtLandscapeBiome)feature;

					if (f.Space == SgtLandscapeBiome.SpaceType.Local)
					{
						foreach (var l in f.Layers)
						{
							if (l.Enabled == true)
							{
								AddLocalDetailData(l.LocalTiling, l.LocalScale, l.HeightIndex, l.HeightRange, l.HeightMidpoint, f.Strata, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset, f.CalculateMatrix());
							}
						}

						if (f.Color == true)
						{
							AddLocalColorData(f.Variation, f.Occlusion, f.Strata, f.GradientIndex, f.Blur, f.SmoothnessMidpoint, f.SmoothnessStrength, f.SmoothnessPower, f.EmissionMidpoint, f.EmissionStrength, f.EmissionPower, f.Offset, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset, f.CalculateMatrix());
						}
					}
					else if (f.Space == SgtLandscapeBiome.SpaceType.Global)
					{
						foreach (var l in f.Layers)
						{
							if (l.Enabled == true)
							{
								AddGlobalDetailData(l.GlobalIndex, l.GlobalTile, l.HeightIndex, l.HeightRange, l.HeightMidpoint, l.Strata, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset);
							}
						}

						if (f.Color == true)
						{
							AddGlobalColorData(f.Variation, f.Occlusion, f.Strata, f.GradientIndex, f.Blur, f.SmoothnessMidpoint, f.SmoothnessStrength, f.SmoothnessPower, f.EmissionMidpoint, f.EmissionStrength, f.EmissionPower, f.Offset, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset);
						}
					}
				}
				else if (feature is SgtLandscapeDetail)
				{
					var f = (SgtLandscapeDetail)feature;

					if (f.Space == SgtLandscapeDetail.SpaceType.Local)
					{
						AddLocalDetailData(f.LocalTiling, f.LocalScale, f.HeightIndex, f.HeightRange, f.HeightMidpoint, f.Strata, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset, f.CalculateMatrix());
					}
					else if (f.Space == SgtLandscapeDetail.SpaceType.Global)
					{
						AddGlobalDetailData(f.GlobalIndex, f.GlobalTile, f.HeightIndex, f.HeightRange, f.HeightMidpoint, f.Strata, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset);
					}
				}
				else if (feature is SgtLandscapeFlatten)
				{
					var f = (SgtLandscapeFlatten)feature;

					if (f.Space == SgtLandscapeFlatten.SpaceType.Local)
					{
						AddLocalFlattenData(f.TargetHeight, f.TargetStrata, f.FlattenHeight, f.FlattenStrata, f.MaskIndex, f.MaskInvert, f.CalculateMatrix());
					}
					else if (f.Space == SgtLandscapeFlatten.SpaceType.Global)
					{
						AddGlobalFlattenData(f.TargetHeight, f.TargetStrata, f.FlattenHeight, f.FlattenStrata, f.MaskIndex, f.MaskInvert);
					}
				}
				else if (feature is SgtLandscapeColor)
				{
					var f = (SgtLandscapeColor)feature;

					if (f.Space == SgtLandscapeColor.SpaceType.Local)
					{
						AddLocalColorData(f.Variation, f.Occlusion, f.Strata, f.GradientIndex, f.Blur, f.SmoothnessMidpoint, f.SmoothnessStrength, f.SmoothnessPower, f.EmissionMidpoint, f.EmissionStrength, f.EmissionPower, f.Offset, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset, f.CalculateMatrix());
					}
					else if (f.Space == SgtLandscapeColor.SpaceType.Global)
					{
						AddGlobalColorData(f.Variation, f.Occlusion, f.Strata, f.GradientIndex, f.Blur, f.SmoothnessMidpoint, f.SmoothnessStrength, f.SmoothnessPower, f.EmissionMidpoint, f.EmissionStrength, f.EmissionPower, f.Offset, f.Mask, f.MaskIndex, f.MaskInvert, f.MaskSharpness, f.MaskGlobalShift, f.MaskDetail, f.MaskDetailIndex, f.MaskDetailTiling, f.MaskDetailOffset);
					}
				}
			}
		}

		private void AddLocalDetailData(Vector2 localTiling, Vector2 localScale, int heightIndex, float heightRange, float heightMidpoint, float strata, bool mask, int maskIndex, bool maskInvert, float maskSharpness, float maskGlobalShift, bool maskDetail, int maskDetailIndex, Vector2 maskDetailTiling, float maskDetailOffset, Matrix4x4 matrix)
		{
			localDetailDataA[localDetailCount] = new float4(localTiling, localScale);
			localDetailDataB[localDetailCount] = new float4(strata, GetHeightIndex(heightIndex), -heightRange * heightMidpoint, heightRange);

			localDetailDataC[localDetailCount] = new float4(mask == true ? GetMaskIndex(maskIndex) : -1, maskSharpness, 0, maskInvert == true ? 1 : 0);
			localDetailDataD[localDetailCount] = new float4(maskDetail == true ? GetDetailIndex(maskDetailIndex) : -1, maskDetailTiling, maskDetailOffset);
			localDetailMatrix[localDetailCount] = matrix;

			localDetailCount += 1;
		}

		private void AddGlobalDetailData(int globalIndex, int globalTile, int heightIndex, float heightRange, float heightMidpoint, float strata, bool mask, int maskIndex, bool maskInvert, float maskSharpness, float maskGlobalShift, bool maskDetail, int maskDetailIndex, Vector2 maskDetailTiling, float maskDetailOffset)
		{
			if (globalTile > 0)
			{
				var vector = Vector4.zero;

				vector[globalIndex] = 1.0f;

				globalDetailDataA[globalDetailCount] = new float4(1.0f / globalTilingNormalized[globalIndex] / heightRange / globalTile, globalTile, 0.0f, 0.0f);
				globalDetailDataB[globalDetailCount] = new float4(strata, GetHeightIndex(heightIndex), -heightRange * heightMidpoint, heightRange);

				globalDetailDataC[globalDetailCount] = new float4(mask == true ? GetMaskIndex(maskIndex) : -1, maskSharpness, maskGlobalShift, maskInvert == true ? 1 : 0);
				globalDetailDataD[globalDetailCount] = new float4(maskDetail == true ? GetDetailIndex(maskDetailIndex) : -1, maskDetailTiling, maskDetailOffset);
				globalDetailLayer[globalDetailCount] = vector;

				globalDetailCount += 1;
			}
		}

		private void AddLocalFlattenData(float targetHeight, float targetStrata, float flattenHeight, float flattenStrata, int maskIndex, bool maskInvert, Matrix4x4 matrix)
		{
			localFlattenDataA[localFlattenCount] = new float4(targetHeight, targetStrata, flattenHeight, flattenStrata);
			localFlattenDataC[localFlattenCount] = new float4(GetMaskIndex(maskIndex), 0, 0, maskInvert == true ? 1 : 0);
			localFlattenMatrix[localFlattenCount] = matrix;

			localFlattenCount += 1;
		}

		private void AddGlobalFlattenData(float targetHeight, float targetStrata, float flattenHeight, float flattenStrata, int maskIndex, bool maskInvert)
		{
			globalFlattenDataA[globalFlattenCount] = new float4(targetHeight, targetStrata, flattenHeight, flattenStrata);
			globalFlattenDataC[globalFlattenCount] = new float4(GetMaskIndex(maskIndex), 0, 0, maskInvert == true ? 1 : 0);

			globalFlattenCount += 1;
		}

		private void AddLocalColorData(float variation, float occlusion, float strata, int gradientIndex, float blur, float smoothnessMidpoint, float smoothnessStrength, float smoothnessPower, float emissionMidpoint, float emissionStrength, float emissionPower, float offset, bool mask, int maskIndex, bool maskInvert, float maskSharpness, float maskGlobalShift, bool maskDetail, int maskDetailIndex, Vector2 maskDetailTiling, float maskDetailOffset, Matrix4x4 matrix)
		{
			localColorDataA[localColorCount] = new float4(variation, occlusion, strata, GetGradientIndex(gradientIndex));
			localColorDataB[localColorCount] = new float4(blur, offset, smoothnessStrength, emissionStrength);
			localColorDataC[localColorCount] = new float4(mask == true ? GetMaskIndex(maskIndex) : -1, maskSharpness, 0, maskInvert == true ? 1 : 0);
			localColorDataD[localColorCount] = new float4(maskDetail == true ? GetDetailIndex(maskDetailIndex) : -1, maskDetailTiling, maskDetailOffset);
			localColorDataE[localColorCount] = new float4(smoothnessMidpoint, smoothnessPower, emissionMidpoint, emissionPower);
			localColorMatrix[localColorCount] = matrix;

			localColorCount += 1;
		}

		private void AddGlobalColorData(float variation, float occlusion, float strata, int gradientIndex, float blur, float smoothnessMidpoint, float smoothnessStrength, float smoothnessPower, float emissionMidpoint, float emissionStrength, float emissionPower, float offset, bool mask, int maskIndex, bool maskInvert, float maskSharpness, float maskGlobalShift, bool maskDetail, int maskDetailIndex, Vector2 maskDetailTiling, float maskDetailOffset)
		{
			globalColorDataA[globalColorCount] = new float4(variation, occlusion, strata, GetGradientIndex(gradientIndex));
			globalColorDataB[globalColorCount] = new float4(blur, offset, smoothnessStrength, emissionStrength);
			globalColorDataC[globalColorCount] = new float4(mask == true ? GetMaskIndex(maskIndex) : -1, maskSharpness, maskGlobalShift, maskInvert == true ? 1 : 0);
			globalColorDataD[globalColorCount] = new float4(maskDetail == true ? GetDetailIndex(maskDetailIndex) : -1, maskDetailTiling, maskDetailOffset);
			globalColorDataE[globalColorCount] = new float4(smoothnessMidpoint, smoothnessPower, emissionMidpoint, emissionPower);

			globalColorCount += 1;
		}

		protected virtual void Dispose()
		{
			foreach (var pending in PendingTriangle.Pool) { pending.Dispose(); } PendingTriangle.Pool.Clear();

			foreach (var batch in batches) { batch.Dispose(); } batches.Clear();

			foreach (var storage in storages) { storage.Dispose(); } storages.Clear();

			foreach (var pending in pendingTriangles) { pending.Handle.Complete(); pending.Dispose(); } pendingTriangles.Clear();

			if (OnRemoveVisual != null)
			{
				foreach (var pair in visuals)
				{
					OnRemoveVisual.Invoke(pair.Value);
				}
			}

			visuals.Clear();

			Visual.Pool.Clear();

			if (rootPending != null) rootPending.Dispose(); rootPending = null;

			foreach (var feature in features)
			{
				feature.Dispose();
			}

			pendingUpdate.Complete();

			topology.Dispose();

			cameraPositions.Dispose();

			cameraPoints.Dispose();

			triangles.Dispose();

			createDiffs.Dispose();
			deleteDiffs.Dispose();
			statusDiffs.Dispose();

			if (registeredBundle != null)
			{
				registeredBundle.RemoveRef();

				registeredBundle = null;
			}
		}

		private void UpdateCameraPositions()
		{
			cameraPositions.Clear();

			if (observers != null)
			{
				foreach (var observer in observers)
				{
					if (observer != null)
					{
						cameraPositions.Add((float3)transform.InverseTransformPoint(observer.position));
					}
				}
			}

			if (cameraPositions.Length == 0 && Camera.main != null)
			{
				cameraPositions.Add((float3)transform.InverseTransformPoint(Camera.main.transform.position));
			}

			if (cameraPoints.Count != cameraPositions.Length)
			{
				cameraPoints.Dispose();

				cameraPoints = new PendingPoints(cameraPositions.Length);
			}

			cameraPoints.Points.CopyFrom(cameraPositions.AsArray());
		}

		private bool UpdateDiffs(float budget)
		{
			var maxTicks = long.MaxValue;

			if (budget > 0.0f)
			{
				maxTicks = (long)(System.Diagnostics.Stopwatch.Frequency * budget);
			}

			stopwatch.Restart();

			for (var i = createDiffs.Length - 1; i >= 0; i--)
			{
				var triangle = createDiffs[i]; createDiffs.RemoveAt(i);

				var pendingTriangle = Schedule(triangle);

				pendingTriangles.Add(pendingTriangle);

				if (stopwatch.ElapsedTicks >= maxTicks)
				{
					return false;
				}
			}

			for (var i = pendingTriangles.Count - 1; i >= 0; i--)
			{
				var pendingTriangle = pendingTriangles[i];
				
				if (pendingTriangle.Handle.IsCompleted == true || budget < 0.0f)
				{
					pendingTriangles.RemoveAt(i);

					CompleteAndAddVisual(pendingTriangle);

					if (pendingTriangle.Triangle.Split == false)
					{
						statusDiffs.Add(pendingTriangle.Triangle);
					}

					if (stopwatch.ElapsedTicks >= maxTicks)
					{
						return false;
					}
				}
			}

			// Finished? Swap!
			if (createDiffs.Length == 0 && pendingTriangles.Count == 0)
			{
				foreach (var triangle in deleteDiffs)
				{
					HideVisual(triangle.Hash);

					RemoveVisual(triangle.Hash);
				}

				foreach (var triangle in statusDiffs)
				{
					if (triangle.Split == true)
					{
						HideVisual(triangle.Hash);
					}
					else
					{
						ShowVisual(triangle.Hash);
					}
				}

				deleteDiffs.Clear();
				statusDiffs.Clear();

				return true;
			}

			return false;
		}

		/// <summary>This method will force the LOD to update now, rather than slowly over time.</summary>
		[ContextMenu("Force Update LOD")]
		public void ForceUpdateLOD()
		{
#if UNITY_EDITOR
			if (Application.isPlaying == true)
			{
				UpdateLod(detail, lodSteps);
			}
			else
			{
				UpdateLod(editorDetail, int.MaxValue);
			}
#else
			UpdateLod(detail, int.MaxValue);
#endif
		}

		private void UpdateLod(float detail, int maxSteps, float budget = -1.0f)
		{
			if (IsActivated == true)
			{
				if (pendingUpdate.Running == false)
				{
					if (UpdateDiffs(budget) == true)
					{
						UpdateCameraPositions();

						pendingUpdate.Schedule(ScheduleUpdateTriangles(detail, maxSteps));
					}
				}

				if (pendingUpdate.Running == true)
				{
					if (pendingUpdate.Handle.IsCompleted == true || budget < 0.0f)
					{
						pendingUpdate.Complete();

						UpdateDiffs(budget);
					}
				}
			}
		}

#if UNITY_EDITOR
		private int childrenHash;

		private void UpdateChildrenHash()
		{
			var oldHash = childrenHash;

			childrenHash = 0;

			UpdateChildrenHash(transform);

			if (childrenHash != oldHash)
			{
				MarkForRebuild();
			}
		}

		private void UpdateChildrenHash(Transform t)
		{
			for (var i = 0; i < t.childCount; i++)
			{
				var child   = t.GetChild(i);
				var feature = child.GetComponent<SgtLandscapeFeature>();

				if (feature != null)
				{
					childrenHash = childrenHash * 31 + child.localPosition.GetHashCode();
					childrenHash = childrenHash * 31 + child.localRotation.GetHashCode();
					childrenHash = childrenHash * 31 + child.localScale   .GetHashCode();
				}

				if (child.name != "Colliders")
				{
					UpdateChildrenHash(child);
				}
			}
		}
#endif

		protected virtual void Start()
		{
			if (triangles.IsCreated == true)
			{
				Dispose();
			}

			TryGenerateMeshData();
		}

		protected virtual void LateUpdate()
		{
#if UNITY_EDITOR
			UpdateChildrenHash();
#endif
			if (markForRebuild == true || triangles.IsCreated == false)
			{
				Rebuild();
			}

			/*
			if (instances.First == node)
			{
				var lodTotal = 0.0f;

				foreach (var instance in instances)
				{
					var lodSum = instance.pendingTriangles.Count + 1;

					lodTotal += lodSum;
				}

				foreach (var instance in instances)
				{
					var lodSum = instance.pendingTriangles.Count + 1;

					instance.lodWeight = lodSum / lodTotal;
				}
			}
			*/

#if UNITY_EDITOR
			if (Application.isPlaying == true)
			{
				UpdateLod(detail, lodSteps, lodBudget * lodWeight);
			}
#else
			UpdateLod(detail, lodSteps, lodBudget * lodWeight);
#endif
			
			DrawTriangles();
		}

		private void DrawTriangles()
		{
			var o2w   = RemoveTranslation(transform.localToWorldMatrix);
			var w2o   = RemoveTranslation(transform.worldToLocalMatrix);
			var rot   = Quaternion.Inverse(transform.rotation) * transform.position;

			var settings = new RenderParams(material);

			settings.worldBounds       = new Bounds(transform.position, Vector3.one * 10000000.0f);
			settings.layer             = gameObject.layer;
			settings.camera            = null;
			settings.shadowCastingMode = castShadows == true ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
			settings.receiveShadows    = receiveShadows;

			for (var i = batches.Count - 1; i >= 0; i--)
			{
				var batch = batches[i];

				if (batch.IsDirty == true)
				{
					if (batch.Count > 0)
					{
						batch.Properties.SetVectorArray(_CwOrigins, batch.Origins);
						batch.Properties.SetVectorArray(_CwPositionsA, batch.PositionsA);
						batch.Properties.SetVectorArray(_CwPositionsB, batch.PositionsB);
						batch.Properties.SetVectorArray(_CwPositionsC, batch.PositionsC);

						//batch.MR.SetPropertyBlock(batch.Properties);
						//batch.MF.sharedMesh = batchMeshes[batch.Count - 1];

						batch.IsDirty = false;
					}
					else
					{
						batches.RemoveAt(i);

						batch.Dispose();

						continue;
					}

					batch.IsDirty = false;
				}

				batch.Properties.SetMatrix(_CwObjectToWorld, o2w);
				batch.Properties.SetMatrix(_CwWorldToObject, w2o);
				batch.Properties.SetVector(_CwOffset, rot);

				UpdateBatchBeforeRender(batch);

				settings.matProps = batch.Properties;

				Graphics.RenderMeshPrimitives(settings, batchMeshes[0], 0, batch.Count);
			}
		}

		protected abstract void UpdateBatchBeforeRender(Batch batch);

		private Matrix4x4 RemoveTranslation(Matrix4x4 matrix)
		{
			matrix.m03 = 0;
			matrix.m13 = 0;
			matrix.m23 = 0;

			return matrix;
		}

		/*
		protected virtual void OnGUI()
		{
			if (batches.Count > 0)
			{
				var w = batches[0].Atlas.DataA.width;
				var h = batches[0].Atlas.DataA.height;

				GUI.DrawTexture(new Rect(10,10,w,h), batches[0].Atlas.DataN, ScaleMode.StretchToFill, false);
			}
		}
		*/
#if UNITY_EDITOR
	[System.NonSerialized]
	protected List<float3x3> gizmoTriangles = new List<float3x3>();

	[System.NonSerialized]
	protected List<float3> gizmoObservers = new List<float3>();

	protected virtual void OnDrawGizmosSelected()
	{
		Gizmos.matrix = transform.localToWorldMatrix;

		if (IsActivated == true)
		{
			if (pendingUpdate.Running == false)
			{
				gizmoTriangles.Clear();
				gizmoObservers.Clear();

				foreach (var triangle in triangles)
				{
					if (triangle.Split == false)
					{
						var gizmoTriangle = default(float3x3);

						gizmoTriangle.c0 = (float3)triangle.PositionA;
						gizmoTriangle.c1 = (float3)triangle.PositionB;
						gizmoTriangle.c2 = (float3)triangle.PositionC;

						gizmoTriangles.Add(gizmoTriangle);
					}
				}

				foreach (var cameraPosition in cameraPositions)
				{
					gizmoObservers.Add((float3)cameraPosition);
				}

				UpdateGizmoTriangles();
			}

			foreach (var gizmoTriangle in gizmoTriangles)
			{
				Gizmos.DrawLine(gizmoTriangle.c0, gizmoTriangle.c1);
				Gizmos.DrawLine(gizmoTriangle.c1, gizmoTriangle.c2);
				Gizmos.DrawLine(gizmoTriangle.c2, gizmoTriangle.c0);
			}

			foreach (var cameraPosition in gizmoObservers)
			{
				Gizmos.DrawWireSphere(cameraPosition, 0.1f);
			}
		}
	}

	protected virtual void UpdateGizmoTriangles()
	{
	}
#endif
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	using CW.Common;

	class SgtAssetPostprocessor : UnityEditor.AssetPostprocessor
	{
		static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			foreach (var landscape in SgtLandscape.AllLandscapes)
			{
				landscape.MarkForRebuild();
			}
		}
	}

	public class SgtLandscape_Editor : CwEditor
	{
		protected override void OnInspector()
		{
			SgtLandscape tgt; SgtLandscape[] tgts; GetTargets(out tgt, out tgts);

			var markForRebuild = false;

			if (Any(tgts, t => t.GetComponent<SgtLandscapeCollider>() == null))
			{
				if (Button("Add Collider") == true)
				{
					Each(tgts, t => { if (t.GetComponent<SgtLandscapeCollider>() == null) t.gameObject.AddComponent<SgtLandscapeCollider>(); }, true);
				}
			}

			BeginColor(Color.green);
				if (Any(tgts, t => t.Bundle == null))
				{
					if (Button("Add Bundle") == true)
					{
						Each(tgts, t => { if (t.GetComponent<SgtLandscapeBundle>() == null) t.Bundle = t.gameObject.AddComponent<SgtLandscapeBundle>(); }, true);
					}
				}
			EndColor();

			if (Button("Add Detail") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapeDetail>(t); }, true);
			}

			if (Button("Add Flatten") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapeFlatten>(t); }, true);
			}

			if (Button("Add Color") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapeColor>(t); }, true);
			}

			if (Button("Add Biome") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapeBiome>(t); }, true);
			}

			if (Button("Add Prefab Spawner") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapePrefabSpawner>(t); }, true);
			}

			if (Button("Add Static Spawner") == true)
			{
				Each(tgts, t => { AddChildComponent<SgtLandscapeStaticSpawner>(t); }, true);
			}

			if (Button("Force Update LOD") == true)
			{
				Each(tgts, t => t.ForceUpdateLOD(), true);
			}

			Separator();

			Draw("editorDetail", "This allows you to control the LOD detail while in edit mode.");
			BeginError(Any(tgts, t => t.InitDetail > t.Detail));
				Draw("initDetail", "When entering play mode or running the actual game, the landscape will initialize with this level of detail.\n\nNOTE: 0 means it will initialize with the minimum amount of triangles, and will generate very quickly.\n\nHigher values will make the planet immediately look better, but may cause the scene to freeze until it's generated.");
			EndError();
			BeginError(Any(tgts, t => t.Detail <= 0.0f));
				Draw("detail", "The overall detail of the landscape relative to the camera distance. The higher you set this, the more triangles it will have.");
			EndError();
			BeginError(Any(tgts, t => t.MinimumTriangleSize <= 0.0f));
				Draw("minimumTriangleSize", "The maximum LOD depth this landscape can have.");
			EndError();
			Draw("lodBudget", "The maximum amount of seconds we can budget for LOD.");
			Draw("lodSteps", "The maximum LOD chunks that can be generated between each LOD change. For example, if your camera suddenly travels to the planet surface then many LOD chunks will need to be generated. If you set LOD Steps to a high value, then it will take a while for any changes to appear, whereas a low value means the landscape will constantly be updating to the final LOD state.");
			Draw("observers", "The landscape LOD will be based on these transform positions.\n\nNone/null = The GameObject with the <b>MainCamera</b> tag will be used.");

			Separator();

			BeginError(Any(tgts, t => t.Material == null));
				Draw("material", "The landscape will be rendered using this Material.\n\nNOTE: This material must use a shader based on the SGT/Landscape shader.");
			EndError();
			BeginError(Any(tgts, t => t.Bundle == null));
				Draw("bundle", ref markForRebuild, "The landscape will use textures from this texture bundle.");
			EndError();
			if (DrawVector4("globalSizes", "To support massive planets, the global detail/biome UV sizes must be baked into the mesh. This allows you to set up to 4 different tile sizes.") == true)
			{
				markForRebuild = true;
			}
			Draw("castShadows", "Should the landscape cast Unity built-in shadows?");
			Draw("receiveShadows", "Should the landscape receive Unity built-in shadows?");

			if (markForRebuild == true)
			{
				Each(tgts, t => t.MarkForRebuild());
			}
		}

		public static Texture2D Randomize(Texture2D current, ref bool markForRebuild)
		{
			if (current != null)
			{
				var currentPath = UnityEditor.AssetDatabase.GetAssetPath(current);
				var guids       = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new string[] { System.IO.Path.GetDirectoryName(currentPath) });

				if (guids.Length > 0)
				{
					var guid = guids[UnityEngine.Random.Range(0, guids.Length)];
					var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
					var next = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);

					if (current != next)
					{
						current        = next;
						markForRebuild = true;
					}
				}
			}

			return current;
		}

		private static T AddChildComponent<T>(SgtLandscape tgt)
			where T : Component
		{
			var child = new GameObject(typeof(T).Name);

			child.transform.SetParent(tgt.transform, false);

			var component = child.AddComponent<T>();

			CW.Common.CwHelper.SelectAndPing(component);

			return component;
		}

		public static float Randomize01(float current, ref bool markForRebuild)
		{
			current        = UnityEngine.Random.Range(0.0f, 1.0f);
			markForRebuild = true;

			return current;
		}
	}
}
#endif