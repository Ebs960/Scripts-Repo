using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SpaceGraphicsToolkit.Landscape
{
	public partial class SgtLandscape
	{
		public class Atlas
		{
			public RenderTexture DataP;
			public RenderTexture DataA;
			public RenderTexture DataN;

			public Atlas()
			{
				var descP = new RenderTextureDescriptor(               VERTEX_COUNT,            BATCH_CAPACITY, RenderTextureFormat.ARGBFloat, 0); descP.sRGB = false;
				var descA = new RenderTextureDescriptor(ATLAS_COLUMNS * PIXEL_WIDTH, ATLAS_ROWS * PIXEL_HEIGHT, RenderTextureFormat.ARGB32   , 0); descA.sRGB = false;
				var descN = new RenderTextureDescriptor(ATLAS_COLUMNS * PIXEL_WIDTH, ATLAS_ROWS * PIXEL_HEIGHT, RenderTextureFormat.ARGB32   , 0); descN.sRGB = false;

				DataP = new RenderTexture(descP);
				DataP.filterMode = FilterMode.Point;
				DataP.wrapMode = TextureWrapMode.Clamp;

				DataA = new RenderTexture(descA);
				DataA.filterMode = FilterMode.Trilinear;
				//DataA.filterMode = FilterMode.Point;
				DataA.wrapMode   = TextureWrapMode.Clamp;

				DataN = new RenderTexture(descN);
				DataN.filterMode = FilterMode.Trilinear;
				DataN.wrapMode   = TextureWrapMode.Clamp;
			}

			public void Write(int slice, Texture bufferP, Texture bufferA, Texture bufferN)
			{
				var col = slice % ATLAS_COLUMNS;
				var row = slice / ATLAS_COLUMNS;

				Graphics.CopyTexture(bufferP, 0, 0, 0, 0, VERTEX_COUNT, 1, DataP, 0, 0, 0, slice);
				GL.sRGBWrite = false;
				Graphics.CopyTexture(bufferA, 0, 0, 0, 0, PIXEL_WIDTH, PIXEL_HEIGHT, DataA, 0, 0, col * PIXEL_WIDTH, row * PIXEL_HEIGHT);
				Graphics.CopyTexture(bufferN, 0, 0, 0, 0, PIXEL_WIDTH, PIXEL_HEIGHT, DataN, 0, 0, col * PIXEL_WIDTH, row * PIXEL_HEIGHT);
			}

			public void Write(int slice, Atlas fromAtlas, int fromSlice)
			{
				var col     = slice % ATLAS_COLUMNS;
				var row     = slice / ATLAS_COLUMNS;
				var fromCol = fromSlice % ATLAS_COLUMNS;
				var fromRow = fromSlice / ATLAS_COLUMNS;

				Graphics.CopyTexture(fromAtlas.DataP, 0, 0, 0, fromSlice, VERTEX_COUNT, 1, DataP, 0, 0, 0, slice);
				GL.sRGBWrite = false;
				Graphics.CopyTexture(fromAtlas.DataA, 0, 0, fromCol * PIXEL_WIDTH, fromRow * PIXEL_HEIGHT, PIXEL_WIDTH, PIXEL_HEIGHT, DataA, 0, 0, col * PIXEL_WIDTH, row * PIXEL_HEIGHT);
				Graphics.CopyTexture(fromAtlas.DataN, 0, 0, fromCol * PIXEL_WIDTH, fromRow * PIXEL_HEIGHT, PIXEL_WIDTH, PIXEL_HEIGHT, DataN, 0, 0, col * PIXEL_WIDTH, row * PIXEL_HEIGHT);
			}

			public void Dispose()
			{
				DestroyImmediate(DataP);
				DestroyImmediate(DataA);
				DestroyImmediate(DataN);
			}
		}

		public class Storage
		{
			public Stack<int> FreeSlices = new Stack<int>();

			public Atlas Atlas = new Atlas();

			public Storage()
			{
				for (var i = BATCH_CAPACITY - 1; i >= 0; i--)
				{
					FreeSlices.Push(i);
				}
			}

			public void Dispose()
			{
				Atlas.Dispose();
			}
		}

		public class Batch
		{
			public int       Count;
			public Visual[]  Visuals    = new Visual[BATCH_CAPACITY];
			public Vector4[] Origins    = new Vector4[BATCH_CAPACITY];
			public Vector4[] PositionsA = new Vector4[BATCH_CAPACITY];
			public Vector4[] PositionsB = new Vector4[BATCH_CAPACITY];
			public Vector4[] PositionsC = new Vector4[BATCH_CAPACITY];

			public bool         IsDirty;

			/*
			public Transform    TT;
			public GameObject   GO;
			public MeshFilter   MF;
			public MeshRenderer MR;
			public Mesh         Mesh;
			*/

			public Atlas Atlas = new Atlas();

			public MaterialPropertyBlock Properties;

			public int[] DataI = new int[BATCH_CAPACITY];

			public Batch(SgtLandscape landscape)
			{
				/*
				GO = new GameObject("Batch");
				TT = GO.transform;
				MF = GO.AddComponent<MeshFilter>();
				MR = GO.AddComponent<MeshRenderer>();

				Mesh = new Mesh();
				Mesh.hideFlags   = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
				Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				Mesh.bounds      = new Bounds(Vector3.zero, Vector3.one * 10000000.0f);

				MF.sharedMesh = Mesh;

				MR.sharedMaterial = landscape.material;

				GO.hideFlags = HideFlags.DontSave;

				TT.SetParent(landscape.transform, false);
				*/

				Properties = new MaterialPropertyBlock();

				Properties.SetTexture("DataP", Atlas.DataP);
				Properties.SetTexture("DataA", Atlas.DataA);
				Properties.SetTexture("DataN", Atlas.DataN);
				Properties.SetVector("_CwSize", new Vector4(1.0f / (VERTEX_COUNT - 1), 1.0f / (BATCH_CAPACITY - 1.0f)));
				Properties.SetVector("_CwAtlas", new Vector4(PIXEL_WIDTH, PIXEL_HEIGHT, ATLAS_COLUMNS, ATLAS_ROWS));
				Properties.SetVectorArray("_CwWeights", VERTEX_BARY);
				Properties.SetVectorArray("_CwCoords", VERTEX_COORDS);
			}

			public void AddVisual(SgtLandscape planet, Visual visual)
			{
				if (visual.Batch != null)
				{
					Debug.LogError("Wrong Batch.");
				}

				visual.Batch = this;
				visual.Slice = Count;

				Visuals[Count] = visual;
				Origins[Count] = new float4((float3)visual.Origin, visual.Slice);
				PositionsA[Count] = (Vector3)visual.PositionA;
				PositionsB[Count] = (Vector3)visual.PositionB;
				PositionsC[Count] = (Vector3)visual.PositionC;

				Atlas.Write(Count, visual.Storage.Atlas, visual.StorageSlice);

				Count += 1;

				IsDirty = true;
			}

			public void RemoveVisual(Visual visual)
			{
				if (visual.Batch != this)
				{
					Debug.LogError("Wrong Batch.");
				}

				var lastIndex = Count - 1;

				if (visual.Slice < lastIndex)
				{
					Origins   [visual.Slice] = Origins   [lastIndex];
					Visuals   [visual.Slice] = Visuals   [lastIndex];
					PositionsA[visual.Slice] = PositionsA[lastIndex];
					PositionsB[visual.Slice] = PositionsB[lastIndex];
					PositionsC[visual.Slice] = PositionsC[lastIndex];

					Atlas.Write(visual.Slice, Visuals[lastIndex].Storage.Atlas, Visuals[lastIndex].StorageSlice);

					Visuals[lastIndex].Slice = visual.Slice;
				}

				visual.Batch = null;
				visual.Slice = -1;

				Count -= 1;

				IsDirty = true;
			}

			public void Dispose()
			{
				//DestroyImmediate(GO);

				Atlas.Dispose();
			}
		}

		public class Visual
		{
			public int          Slice = -1;
			public TriangleHash Hash;
			public int          Depth;
			public Batch        Batch;
			public int3         Origin;
			public float3       PositionA;
			public float3       PositionB;
			public float3       PositionC;

			public Storage Storage;
			public int     StorageSlice = -1;

			public static Stack<Visual> Pool = new Stack<Visual>();
		}

		[System.NonSerialized]
		public List<Batch> batches = new List<Batch>();

		[System.NonSerialized]
		public List<Storage> storages = new List<Storage>();

		[System.NonSerialized]
		public Dictionary<TriangleHash, Visual> visuals = new Dictionary<TriangleHash, Visual>();

		public Storage GetStorage()
		{
			foreach (var storage in storages)
			{
				if (storage.FreeSlices.Count > 0)
				{
					return storage;
				}
			}

			var newStorage = new Storage();

			storages.Add(newStorage);

			return newStorage;
		}

		public Batch GetBatch()
		{
			foreach (var batch in batches)
			{
				if (batch.Count < BATCH_CAPACITY)
				{
					return batch;
				}
			}

			var newBatch = new Batch(this);

			batches.Add(newBatch);

			return newBatch;
		}

		[System.NonSerialized] private bool buffersCreated;
		[System.NonSerialized] private Texture2D tempBufferP;

		[System.NonSerialized] private RenderTexture tempBufferA;
		[System.NonSerialized] private RenderTexture tempBufferN;

		[System.NonSerialized]
		private static RenderBuffer[] tempBuffers = new RenderBuffer[2];

		public Visual AddVisual(PendingTriangle pending)
		{
			var visual = Visual.Pool.Count > 0 ? Visual.Pool.Pop() : new Visual();

			visual.Hash         = pending.Triangle.Hash;
			visual.Depth        = pending.Triangle.Depth;
			visual.Origin       = (int3)math.floor(pending.GetPosition(0));
			visual.PositionA    = (float3)pending.Triangle.PositionA;
			visual.PositionB    = (float3)pending.Triangle.PositionB;
			visual.PositionC    = (float3)pending.Triangle.PositionC;
			visual.Storage      = GetStorage();
			visual.StorageSlice = visual.Storage.FreeSlices.Pop();

			RenderVisual(pending, visual);

			visuals.Add(pending.Triangle.Hash, visual);

			if (OnAddVisual != null)
			{
				OnAddVisual.Invoke(visual, pending);
			}

			return visual;
		}

		protected abstract Material GetVisualBlitMaterial(PendingTriangle pending);

		private void RenderVisual(PendingTriangle pending, Visual visual)
		{
			var oldActive = RenderTexture.active;
			var oldWrite  = GL.sRGBWrite;

			if (buffersCreated == false)
			{
				buffersCreated = true;

				var descA = new RenderTextureDescriptor(PIXEL_WIDTH, PIXEL_HEIGHT, RenderTextureFormat.ARGB32, 0); descA.sRGB = false;
				var descN = new RenderTextureDescriptor(PIXEL_WIDTH, PIXEL_HEIGHT, RenderTextureFormat.ARGB32, 0); descN.sRGB = false;

				tempBufferP = new Texture2D(VERTEX_COUNT, 1, TextureFormat.RGBAFloat, false, true);

				tempBufferA = new RenderTexture(descA);
				tempBufferN = new RenderTexture(descN);
			}

			tempBufferP.SetPixelData(pending.PixelP, 0); tempBufferP.Apply();// Graphics.CopyTexture(tempBufferP, 0, 0, 0, 0, VERTEX_COUNT, 1, visual.BufferP, 0, 0, 0, 0);

			var blitMaterial = GetVisualBlitMaterial(pending);

			tempBuffers[0] = tempBufferA.colorBuffer;
			tempBuffers[1] = tempBufferN.colorBuffer;

			Graphics.SetRenderTarget(tempBuffers, tempBufferA.depthBuffer);

			blitMaterial.SetPass(0); GL.sRGBWrite = false; Graphics.DrawMeshNow(visualBlitMesh, Matrix4x4.identity);

			visual.Storage.Atlas.Write(visual.StorageSlice, tempBufferP, tempBufferA, tempBufferN);

			RenderTexture.active = oldActive;
			GL.sRGBWrite         = oldWrite;
		}

		protected void ApplyBlitVariables(Material blitMaterial)
		{
			if (PIXEL_WEIGHTS == null) GenerateTex();

			blitMaterial.SetTexture(_SGT_WeightTex, PIXEL_WEIGHTS);
			blitMaterial.SetVectorArray(_CwWeights, VERTEX_BARY);
			blitMaterial.SetVectorArray(_CwCoords, VERTEX_COORDS);
			blitMaterial.SetVector(_CwPixelSize, new Vector2(PIXEL_WIDTH, PIXEL_HEIGHT));
			blitMaterial.SetFloat(_CwVertexResolution, VERTEX_RESOLUTION);
			blitMaterial.SetFloat(_CwSize, 1.0f / (VERTEX_COUNT - 1));
			blitMaterial.SetTexture(_CwBufferP, tempBufferP);

			blitMaterial.SetInt(_CwGlobalDetailCount, globalDetailCount);
			blitMaterial.SetVectorArray(_CwGlobalDetailDataA, globalDetailDataA);
			blitMaterial.SetVectorArray(_CwGlobalDetailDataB, globalDetailDataB);
			blitMaterial.SetVectorArray(_CwGlobalDetailDataC, globalDetailDataC);
			blitMaterial.SetVectorArray(_CwGlobalDetailDataD, globalDetailDataD);
			blitMaterial.SetVectorArray(_CwGlobalDetailLayer, globalDetailLayer);

			blitMaterial.SetInt(_CwLocalDetailCount, localDetailCount);
			blitMaterial.SetVectorArray(_CwLocalDetailDataA, localDetailDataA);
			blitMaterial.SetVectorArray(_CwLocalDetailDataB, localDetailDataB);
			blitMaterial.SetVectorArray(_CwLocalDetailDataC, localDetailDataC);
			blitMaterial.SetVectorArray(_CwLocalDetailDataD, localDetailDataD);
			blitMaterial.SetMatrixArray(_CwLocalDetailMatrix, localDetailMatrix);

			blitMaterial.SetInt(_CwGlobalFlattenCount, globalFlattenCount);
			blitMaterial.SetVectorArray(_CwGlobalFlattenDataA, globalFlattenDataA);
			blitMaterial.SetVectorArray(_CwGlobalFlattenDataC, globalFlattenDataC);

			blitMaterial.SetInt(_CwLocalFlattenCount, localFlattenCount);
			blitMaterial.SetVectorArray(_CwLocalFlattenDataA, localFlattenDataA);
			blitMaterial.SetVectorArray(_CwLocalFlattenDataC, localFlattenDataC);
			blitMaterial.SetMatrixArray(_CwLocalFlattenMatrix, localFlattenMatrix);

			blitMaterial.SetInt(_CwGlobalColorCount, globalColorCount);
			blitMaterial.SetVectorArray(_CwGlobalColorDataA, globalColorDataA);
			blitMaterial.SetVectorArray(_CwGlobalColorDataB, globalColorDataB);
			blitMaterial.SetVectorArray(_CwGlobalColorDataC, globalColorDataC);
			blitMaterial.SetVectorArray(_CwGlobalColorDataD, globalColorDataD);
			blitMaterial.SetVectorArray(_CwGlobalColorDataE, globalColorDataE);

			blitMaterial.SetInt(_CwLocalColorCount, localColorCount);
			blitMaterial.SetVectorArray(_CwLocalColorDataA, localColorDataA);
			blitMaterial.SetVectorArray(_CwLocalColorDataB, localColorDataB);
			blitMaterial.SetVectorArray(_CwLocalColorDataC, localColorDataC);
			blitMaterial.SetVectorArray(_CwLocalColorDataD, localColorDataD);
			blitMaterial.SetVectorArray(_CwLocalColorDataE, localColorDataE);
			blitMaterial.SetMatrixArray(_CwLocalColorMatrix, localColorMatrix);
		}

		public void RemoveVisual(TriangleHash hash)
		{
			var visual = default(Visual);

			if (visuals.Remove(hash, out visual) == true)
			{
				HideVisual(hash);

				visual.Storage.FreeSlices.Push(visual.StorageSlice);

				visual.Storage      = null;
				visual.StorageSlice = -1;

				if (OnRemoveVisual != null)
				{
					OnRemoveVisual.Invoke(visual);
				}

				Visual.Pool.Push(visual);
			}
		}

		public void ShowVisual(TriangleHash hash)
		{
			var visual = default(Visual);

			if (visuals.TryGetValue(hash, out visual) == true)
			{
				if (visual.Batch == null)
				{
					GetBatch().AddVisual(this, visual);

					if (OnShowVisual != null)
					{
						OnShowVisual.Invoke(visual);
					}
				}
			}
		}

		public void HideVisual(TriangleHash hash)
		{
			var visual = default(Visual);

			if (visuals.TryGetValue(hash, out visual) == true)
			{
				if (visual.Batch != null)
				{
					visual.Batch.RemoveVisual(visual);

					if (OnHideVisual != null)
					{
						OnHideVisual.Invoke(visual);
					}
				}
			}
		}
	}
}