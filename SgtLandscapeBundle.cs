using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;

namespace SpaceGraphicsToolkit.Landscape
{
	/// <summary>For efficient generation, all textures used by a landscape are combined into a texture bundle.
	/// This component allows you to specify which textures go into the bundle, and handles any conversions.
	/// NOTE: If your scene has multiple bundles that share the same textures, you should combine the bundles together to save memory.</summary>
	[AddComponentMenu("Space Graphics Toolkit/SGT Landscape Bundle")]
	public class SgtLandscapeBundle : MonoBehaviour
	{
		/// <summary>This list stores all heightmaps used to deform the terrain. These should be square textures that are seamless.
		/// NOTE: Requires <b>TextureType = SingleChannel</b>.
		/// NOTE: Requires <b>Advanced / Read/Write</b> to be enabled.
		/// NOTE: Requires <b>Channel = Red</b> and <b>Format = R8 or R16 bit</b>.
		/// NOTE: Requires <b>Compression = None</b>.</summary>
		public List<Texture2D> HeightTextures { get { if (heightTextures == null) heightTextures = new List<Texture2D>(); return heightTextures; } } [SerializeField] private List<Texture2D> heightTextures;

		/// <summary>This list stores all masks used to restrict where detail or colors can appear.
		/// NOTE: Requires <b>TextureType = SingleChannel</b>.
		/// NOTE: Requires <b>Advanced / Read/Write</b> to be enabled.
		/// NOTE: Requires <b>Channel = Red</b> and <b>Format = R8 or R16 bit</b>.
		/// NOTE: Requires <b>Compression = None</b>.</summary>
		public List<Texture2D> MaskTextures { get { if (maskTextures == null) maskTextures = new List<Texture2D>(); return maskTextures; } } [SerializeField] private List<Texture2D> maskTextures;

		/// <summary>This list stores all gradients used to color the landscape. These can be a pictures of landscapes.
		/// NOTE: Requires <b>TextureType = SingleChannel</b>.
		/// NOTE: Requires <b>Advanced / Read/Write</b> to be enabled.
		/// NOTE: Requires <b>Channel = Red</b> and <b>Format = R8 or R16 bit</b>.
		/// NOTE: Requires <b>Compression = None</b>.</summary>
		public List<Texture2D> GradientTextures { get { if (gradientTextures == null) gradientTextures = new List<Texture2D>(); return gradientTextures; } } [SerializeField] private List<Texture2D> gradientTextures;

		/// <summary>This list stores all detail textures used to modify masks. These should be square textures that are seamless of some type of terrain (e.g. mountains).
		/// NOTE: Requires <b>TextureType = SingleChannel</b>.
		/// NOTE: Requires <b>Advanced / Read/Write</b> to be enabled.
		/// NOTE: Requires <b>Channel = Red</b> and <b>Format = R8 or R16 bit</b>.
		/// NOTE: Requires <b>Compression = None</b>.</summary>
		public List<Texture2D> DetailTextures { get { if (detailTextures == null) detailTextures = new List<Texture2D>(); return detailTextures; } } [SerializeField] private List<Texture2D> detailTextures;

		[System.NonSerialized]
		private RenderTexture heightTopologyAtlas;

		[System.NonSerialized]
		private RenderTexture maskTopologyAtlas;

		[System.NonSerialized]
		private RenderTexture gradientAtlas;

		[System.NonSerialized]
		private RenderTexture detailAtlas;

		[System.NonSerialized]
		private SgtLandscape.HeightData[] heightDatas;

		[System.NonSerialized]
		private SgtLandscape.HeightData[] maskDatas;

		//[System.NonSerialized]
		//private SgtLandscape.HeightData[] detailDatas;

		[System.NonSerialized]
		private SgtLandscape.HeightData defaultHeight0;

		[System.NonSerialized]
		private SgtLandscape.HeightData defaultHeight1;

		[System.NonSerialized]
		private int refCount;

		[System.NonSerialized]
		private bool dirty = true;

		public RenderTexture HeightTopologyAtlas
		{
			get
			{
				return heightTopologyAtlas;
			}
		}

		public RenderTexture MaskTopologyAtlas
		{
			get
			{
				return maskTopologyAtlas;
			}
		}

		public RenderTexture GradientAtlas
		{
			get
			{
				return gradientAtlas;
			}
		}

		public RenderTexture DetailAtlas
		{
			get
			{
				return detailAtlas;
			}
		}

		public SgtLandscape.HeightData[] HeightDatas
		{
			get
			{
				return heightDatas;
			}
		}

		public SgtLandscape.HeightData[] MaskDatas
		{
			get
			{
				return maskDatas;
			}
		}

		//public SgtLandscape.HeightData[] DetailDatas
		//{
		//	get
		//	{
		//		return detailDatas;
		//	}
		//}

		[ContextMenu("Mark As Dirty")]
		public void MarkAsDirty()
		{
			dirty = true;

			var landscape = GetComponent<SgtLandscape>();

			if (landscape != null)
			{
				landscape.MarkForRebuild();
			}
		}

		public void AddRef()
		{
			if (refCount == 0)
			{
				Regenerate();
			}

			refCount += 1;
		}

		public void RemoveRef()
		{
			if (refCount > 0)
			{
				refCount -= 1;

				if (refCount == 0)
				{
					Dispose();
				}
			}
		}

		private void Dispose()
		{
			DestroyImmediate(heightTopologyAtlas);
			DestroyImmediate(  maskTopologyAtlas);
			DestroyImmediate(      gradientAtlas);
			DestroyImmediate(        detailAtlas);

			if (heightDatas != null)
			{
				for (var i = 0; i < heightDatas.Length; i++)
				{
					heightDatas[i].Dispose();
				}
			}

			if (maskDatas != null)
			{
				for (var i = 0; i < maskDatas.Length; i++)
				{
					maskDatas[i].Dispose();
				}
			}

			//for (var i = 0; i < detailDatas.Length; i++) detailDatas[i].Dispose();

			heightDatas = null;
			maskDatas   = null;
			//detailDatas = null;

			defaultHeight0.Dispose();
			defaultHeight1.Dispose();
		}

		public void Regenerate()
		{
			dirty = false;

			if (heightTopologyAtlas != null)
			{
				Dispose();
			}

			var oldActive = RenderTexture.active;
			var oldWrite  = GL.sRGBWrite;

			GL.sRGBWrite = false;

			GenerateHeight();
			GenerateMask();
			GenerateGradient();
			GenerateDetail();

			defaultHeight0.Data08    = new NativeArray<byte  >(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			defaultHeight0.Data16    = new NativeArray<ushort>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			defaultHeight0.Size      = new int2(1, 1);
			defaultHeight0.Created08 = true;
			defaultHeight0.Created16 = true;
			defaultHeight0.Data08[0] = 0;
			defaultHeight0.Data16[0] = 0;

			defaultHeight1.Data08    = new NativeArray<byte  >(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			defaultHeight1.Data16    = new NativeArray<ushort>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			defaultHeight1.Size      = new int2(1, 1);
			defaultHeight1.Created08 = true;
			defaultHeight1.Created16 = true;
			defaultHeight1.Data08[0] = 255;
			defaultHeight1.Data16[0] = 65535;

			RenderTexture.active = oldActive;
			GL.sRGBWrite         = oldWrite;
		}

		public SgtLandscape.HeightData GetHeightData(int index)
		{
			if (index >= 0 && heightDatas != null && heightDatas.Length > 0)
			{
				return heightDatas[index % heightDatas.Length];
			}

			return defaultHeight0;
		}

		public SgtLandscape.HeightData GetMaskData(int index)
		{
			if (index >= 0 && maskDatas != null && maskDatas.Length > 0)
			{
				return maskDatas[index % maskDatas.Length];
			}

			return defaultHeight1;
		}

		//public SgtLandscape.HeightData GetDetailData(int index)
		//{
		//	if (index >= 0 && detailDatas != null && detailDatas.Length > 0)
		//	{
		//		return detailDatas[index % detailDatas.Length];
		//	}

		//	return defaultHeight1;
		//}

		private void GenerateHeight()
		{
			var atlasSize   = default(int2);
			var atlasFormat = default(TextureFormat);
			var atlasDepth  = 0;

			if (TryGetTextureData(heightTextures, ref atlasSize, ref atlasFormat, ref atlasDepth) == true)
			{
				var index = 0;
				var mips  = Mathf.FloorToInt(Mathf.Log(Mathf.Max(atlasSize.x, atlasSize.y), 2)) + 1;

				heightTopologyAtlas = new RenderTexture(atlasSize.x, atlasSize.y, 0, RenderTextureFormat.ARGBFloat, mips);
				heightTopologyAtlas.hideFlags         = HideFlags.DontSave;
				heightTopologyAtlas.dimension         = UnityEngine.Rendering.TextureDimension.Tex2DArray;
				heightTopologyAtlas.volumeDepth       = atlasDepth;
				heightTopologyAtlas.enableRandomWrite = true;
				heightTopologyAtlas.wrapMode          = TextureWrapMode.Repeat;
				heightTopologyAtlas.useMipMap         = true;
				heightTopologyAtlas.autoGenerateMips  = false;
				heightTopologyAtlas.Create();

				heightDatas = new SgtLandscape.HeightData[atlasDepth];

				foreach (var heightTexture in heightTextures)
				{
					if (heightTexture != null)
					{
						var data = default(SgtLandscape.TopologyData);

						data.Create(heightTexture, 1, 1, 1, 1);

						Graphics.Blit(data.Texture, heightTopologyAtlas, 0, index);

						data.Dispose();

						heightDatas[index].Create(heightTexture, 1, 1);

						index += 1;
					}
				}

				heightTopologyAtlas.GenerateMips();
			}
		}

		private void GenerateMask()
		{
			var atlasSize   = default(int2);
			var atlasFormat = default(TextureFormat);
			var atlasDepth  = 0;

			if (TryGetTextureData(maskTextures, ref atlasSize, ref atlasFormat, ref atlasDepth) == true)
			{
				var index = 0;
				var mips  = 0;
				var desc  = new RenderTextureDescriptor(atlasSize.x, atlasSize.y, RenderTextureFormat.ARGBFloat, 0, mips);

				maskTopologyAtlas = new RenderTexture(desc);
				maskTopologyAtlas.hideFlags         = HideFlags.DontSave;
				maskTopologyAtlas.dimension         = UnityEngine.Rendering.TextureDimension.Tex2DArray;
				maskTopologyAtlas.volumeDepth       = atlasDepth;
				maskTopologyAtlas.enableRandomWrite = true;
				maskTopologyAtlas.wrapMode          = TextureWrapMode.Repeat;
				maskTopologyAtlas.useMipMap         = true;
				maskTopologyAtlas.autoGenerateMips  = false;
				maskTopologyAtlas.Create();

				maskDatas = new SgtLandscape.HeightData[atlasDepth];

				foreach (var maskTexture in maskTextures)
				{
					if (maskTexture != null)
					{
						var data = default(SgtLandscape.TopologyData);

						data.Create(maskTexture, 1, 1, 1, 1);

						Graphics.Blit(data.Texture, maskTopologyAtlas, 0, index);

						data.Dispose();

						maskDatas[index].Create(maskTexture, 1, 1);

						index += 1;
					}
				}

				maskTopologyAtlas.GenerateMips();
			}
		}

		private void GenerateGradient()
		{
			var atlasSize   = default(int2);
			var atlasFormat = default(TextureFormat);
			var atlasDepth  = 0;

			if (TryGetTextureData(gradientTextures, ref atlasSize, ref atlasFormat, ref atlasDepth) == true)
			{
				var index = 0;
				var mips  = Mathf.FloorToInt(Mathf.Log(Mathf.Max(atlasSize.x, atlasSize.y), 2)) + 1;
				var desc  = new RenderTextureDescriptor(atlasSize.x, atlasSize.y, RenderTextureFormat.ARGB32, 0, mips);

				desc.sRGB = false;

				gradientAtlas = new RenderTexture(desc);
				gradientAtlas.hideFlags         = HideFlags.DontSave;
				gradientAtlas.dimension         = UnityEngine.Rendering.TextureDimension.Tex2DArray;
				gradientAtlas.volumeDepth       = atlasDepth;
				gradientAtlas.enableRandomWrite = true;
				gradientAtlas.wrapMode          = TextureWrapMode.Repeat;
				gradientAtlas.useMipMap         = true;
				gradientAtlas.autoGenerateMips  = false;
				gradientAtlas.Create();

				foreach (var gradientTexture in gradientTextures)
				{
					if (gradientTexture != null)
					{
						Graphics.Blit(gradientTexture, gradientAtlas, 0, index);

						index += 1;
					}
				}

				gradientAtlas.GenerateMips();
			}
		}

		private void GenerateDetail()
		{
			var atlasSize   = default(int2);
			var atlasFormat = default(TextureFormat);
			var atlasDepth  = 0;

			if (TryGetTextureData(detailTextures, ref atlasSize, ref atlasFormat, ref atlasDepth) == true)
			{
				var index = 0;
				var mips  = Mathf.FloorToInt(Mathf.Log(Mathf.Max(atlasSize.x, atlasSize.y), 2)) + 1;
				var desc  = new RenderTextureDescriptor(atlasSize.x, atlasSize.y, RenderTextureFormat.R8, 0, mips);

				detailAtlas = new RenderTexture(desc);
				detailAtlas.hideFlags         = HideFlags.DontSave;
				detailAtlas.dimension         = UnityEngine.Rendering.TextureDimension.Tex2DArray;
				detailAtlas.volumeDepth       = atlasDepth;
				detailAtlas.enableRandomWrite = true;
				detailAtlas.wrapMode          = TextureWrapMode.Repeat;
				detailAtlas.useMipMap         = true;
				detailAtlas.autoGenerateMips  = false;
				detailAtlas.Create();

				foreach (var detailTexture in detailTextures)
				{
					if (detailTexture != null)
					{
						Graphics.Blit(detailTexture, detailAtlas, 0, index);

						index += 1;
					}
				}

				detailAtlas.GenerateMips();
			}
		}

		private bool TryGetTextureData(List<Texture2D> textures, ref int2 size, ref TextureFormat format, ref int count)
		{
			if (dirty == true)
			{
				Regenerate();
			}

			count = 0;

			if (textures != null)
			{
				foreach (var texture in textures)
				{
					if (texture != null)
					{
						size.x = texture.width;
						size.y = texture.height;
						format = texture.format;

						count += 1;
					}
				}
			}

			return count > 0;
		}
	}
}

#if UNITY_EDITOR
namespace SpaceGraphicsToolkit.Landscape
{
	[UnityEditor.CanEditMultipleObjects]
	[UnityEditor.CustomEditor(typeof(SgtLandscapeBundle))]
	public class SgtLandscapeAtlas_Editor : CW.Common.CwEditor
	{
		private static string ErrorText;

		protected override void OnInspector()
		{
			SgtLandscapeBundle tgt; SgtLandscapeBundle[] tgts; GetTargets(out tgt, out tgts);

			var markAsDirty = false;

			Draw("heightTextures", ref markAsDirty);

			if (Any(tgts, t => AnyNull(t.HeightTextures))) Warning("Element " + ErrorText + " of this list is Null.");
			if (Any(tgts, t => AnySizeMismatch(t.HeightTextures))) Warning("Element " + ErrorText + " of this list is a different size to previous textures.");
			if (Any(tgts, t => AnyFormatMismatch(t.HeightTextures))) Warning("Element " + ErrorText + " of this list is a different format to previous textures.");
			if (Any(tgts, t => AnyNotReadable(t.HeightTextures))) Warning("Element " + ErrorText + " of this list is not readable. Please enable Advanced / Read/Write.");
			if (Any(tgts, t => AnyNotRed(t.HeightTextures))) Warning("Element " + ErrorText + " of this list is not red. Please set Texture Type = Single Channel, Channel = Red, Format = R 8 or R 16 bit.");

			Separator();

			Draw("maskTextures", ref markAsDirty);

			if (Any(tgts, t => AnyNull(t.MaskTextures))) Warning("Element " + ErrorText + " of this list is Null.");
			if (Any(tgts, t => AnySizeMismatch(t.MaskTextures))) Warning("Element " + ErrorText + " of this list is a different size to previous textures.");
			if (Any(tgts, t => AnyFormatMismatch(t.MaskTextures))) Warning("Element " + ErrorText + " of this list is a different format to previous textures.");
			if (Any(tgts, t => AnyNotReadable(t.MaskTextures))) Warning("Element " + ErrorText + " of this list is not readable. Please enable Advanced / Read/Write.");
			if (Any(tgts, t => AnyNotRed(t.MaskTextures))) Warning("Element " + ErrorText + " of this list is not red. Please set Texture Type = Single Channel, Channel = Red, Format = R 8 or R 16 bit.");

			Separator();

			Draw("gradientTextures", ref markAsDirty);

			if (Any(tgts, t => AnyNull(t.GradientTextures))) Warning("Element " + ErrorText + " of this list is Null.");
			if (Any(tgts, t => AnySizeMismatch(t.GradientTextures))) Warning("Element " + ErrorText + " of this list is a different size to previous textures.");
			if (Any(tgts, t => AnyFormatMismatch(t.GradientTextures))) Warning("Element " + ErrorText + " of this list is a different format to previous textures.");
			if (Any(tgts, t => AnyNotReadable(t.GradientTextures))) Warning("Element " + ErrorText + " of this list is not readable. Please enable Advanced / Read/Write.");

			Separator();

			Draw("detailTextures", ref markAsDirty);

			if (Any(tgts, t => AnyNull(t.DetailTextures))) Warning("Element " + ErrorText + " of this list is Null.");
			if (Any(tgts, t => AnySizeMismatch(t.DetailTextures))) Warning("Element " + ErrorText + " of this list is a different size to previous textures.");
			if (Any(tgts, t => AnyFormatMismatch(t.DetailTextures))) Warning("Element " + ErrorText + " of this list is a different format to previous textures.");
			if (Any(tgts, t => AnyNotReadable(t.DetailTextures))) Warning("Element " + ErrorText + " of this list is not readable. Please enable Advanced / Read/Write.");
			if (Any(tgts, t => AnyNotRed(t.DetailTextures))) Warning("Element " + ErrorText + " of this list is not red. Please set Texture Type = Single Channel, Channel = Red, Format = R 8 or R 16 bit.");

			Separator();

			UnityEditor.EditorGUILayout.LabelField("RANDOMIZE", UnityEditor.EditorStyles.boldLabel);

			for (var i = 0; i < 5; i++)
			{
				if (Any(tgts, t => t.HeightTextures.Count > i) && Button("Randomize Height Texture " + i) == true)
				{
					Each(tgts, t => { if (t.HeightTextures.Count > i) t.HeightTextures[i] = SgtLandscape_Editor.Randomize(t.HeightTextures[i], ref markAsDirty); }, true);
				}
			}

			Separator();

			for (var i = 0; i < 5; i++)
			{
				if (Any(tgts, t => t.MaskTextures.Count > i) && Button("Randomize Mask Texture " + i) == true)
				{
					Each(tgts, t => { if (t.MaskTextures.Count > i) t.MaskTextures[i] = SgtLandscape_Editor.Randomize(t.MaskTextures[i], ref markAsDirty); }, true);
				}
			}

			Separator();

			for (var i = 0; i < 5; i++)
			{
				if (Any(tgts, t => t.GradientTextures.Count > i) && Button("Randomize Gradient Texture " + i) == true)
				{
					Each(tgts, t => { if (t.GradientTextures.Count > i) t.GradientTextures[i] = SgtLandscape_Editor.Randomize(t.GradientTextures[i], ref markAsDirty); }, true);
				}
			}

			Separator();

			for (var i = 0; i < 5; i++)
			{
				if (Any(tgts, t => t.DetailTextures.Count > i) && Button("Randomize Detail Texture " + i) == true)
				{
					Each(tgts, t => { if (t.DetailTextures.Count > i) t.DetailTextures[i] = SgtLandscape_Editor.Randomize(t.DetailTextures[i], ref markAsDirty); }, true);
				}
			}

			Separator();

			UnityEditor.EditorGUILayout.LabelField("SIZES", UnityEditor.EditorStyles.boldLabel);

			var heightSize   = default(Vector2Int);
			var heightFormat = default(TextureFormat);

			if (TryGetData(tgt.HeightTextures, ref heightSize, ref heightFormat) == true)
			{
				var megs = CalculateMegabytes(tgt.HeightTextures, heightSize, 4 * 4);

				Info("Height Texture Size = " + heightSize.x + "x" + heightSize.y + ", Format = " + heightFormat + ", Megabytes = " + megs);

				if (Button("Change Height Texture Max Size") == true)
				{
					TrySetMaxSize(tgt.HeightTextures);
				}
			}

			var maskSize   = default(Vector2Int);
			var maskFormat = default(TextureFormat);

			if (TryGetData(tgt.MaskTextures, ref maskSize, ref maskFormat) == true)
			{
				var megs = CalculateMegabytes(tgt.MaskTextures, maskSize, 4 * 4);

				Info("Mask Texture Size = " + maskSize.x + "x" + maskSize.y + ", Format = " + maskFormat + ", Megabytes = " + megs);

				if (Button("Change Mask Texture Max Size") == true)
				{
					TrySetMaxSize(tgt.MaskTextures);
				}
			}

			var gradientSize   = default(Vector2Int);
			var gradientFormat = default(TextureFormat);

			if (TryGetData(tgt.GradientTextures, ref gradientSize, ref gradientFormat) == true)
			{
				var megs = CalculateMegabytes(tgt.GradientTextures, gradientSize, 1 * 4);

				Info("Gradient Texture Size = " + gradientSize.x + "x" + gradientSize.y + ", Format = " + gradientFormat + ", Megabytes = " + megs);

				if (Button("Change Gradient Texture Max Size") == true)
				{
					TrySetMaxSize(tgt.GradientTextures);
				}
			}

			var detailSize   = default(Vector2Int);
			var detailFormat = default(TextureFormat);

			if (TryGetData(tgt.DetailTextures, ref detailSize, ref detailFormat) == true)
			{
				var megs = CalculateMegabytes(tgt.DetailTextures, detailSize, 1 * 1);

				Info("Detail Texture Size = " + detailSize.x + "x" + detailSize.y + ", Format = " + detailFormat + ", Megabytes = " + megs);

				if (Button("Change Detail Texture Max Size") == true)
				{
					TrySetMaxSize(tgt.DetailTextures);
				}
			}

			if (markAsDirty == true)
			{
				Each(tgts, t => t.MarkAsDirty());
			}
			//UnityEditor.EditorGUILayout.ObjectField(tgt.TopologyAtlas, typeof(RenderTexture), false);
		}

		private void TrySetMaxSize(List<Texture2D> textures)
		{
			var menu        = new UnityEditor.GenericMenu();
			var currentSize = 0;

			if (textures != null)
			{
				for (var i = 0; i < textures.Count; i++)
				{
					var t = textures[i];

					if (t != null)
					{
						var p = UnityEditor.AssetDatabase.GetAssetPath(t);

						if (string.IsNullOrEmpty(p) == false)
						{
							var ti = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.TextureImporter;

							if (ti != null)
							{
								currentSize = ti.maxTextureSize; break;
							}
						}
					}
				}
			}
			
			menu.AddItem(new GUIContent("Change to 256" ), currentSize ==  256, () => TrySetMaxSize(textures,  256));
			menu.AddItem(new GUIContent("Change to 512" ), currentSize ==  512, () => TrySetMaxSize(textures,  512));
			menu.AddItem(new GUIContent("Change to 1024"), currentSize == 1024, () => TrySetMaxSize(textures, 1024));
			menu.AddItem(new GUIContent("Change to 2048"), currentSize == 2048, () => TrySetMaxSize(textures, 2048));
			menu.AddItem(new GUIContent("Change to 4096"), currentSize == 4096, () => TrySetMaxSize(textures, 4096));
			menu.AddItem(new GUIContent("Change to 8192"), currentSize == 8192, () => TrySetMaxSize(textures, 8192));

			menu.ShowAsContext();
		}

		private void TrySetMaxSize(List<Texture2D> textures, int size)
		{
			if (textures != null)
			{
				for (var i = 0; i < textures.Count; i++)
				{
					var t = textures[i];

					if (t != null)
					{
						var p = UnityEditor.AssetDatabase.GetAssetPath(t);

						if (string.IsNullOrEmpty(p) == false)
						{
							var ti = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.TextureImporter;

							if (ti != null && ti.maxTextureSize != size)
							{
								ti.maxTextureSize = size;

								UnityEditor.EditorUtility.SetDirty(ti);

								ti.SaveAndReimport();
							}
						}
					}
				}
			}
		}

		private bool TryGetData(List<Texture2D> textures, ref Vector2Int size, ref TextureFormat format)
		{
			if (textures != null)
			{
				for (var i = 0; i < textures.Count; i++)
				{
					var t = textures[i];

					if (t != null)
					{
						size.x = t.width;
						size.y = t.height;
						format = t.format;

						return true;
					}
				}
			}

			return false;
		}

		private int CalculateMegabytes(List<Texture2D> textures, Vector2Int size, int bytesPerPixel)
		{
			var total = 0;

			if (textures != null)
			{
				for (var i = 0; i < textures.Count; i++)
				{
					var t = textures[i];

					if (t != null)
					{
						total += size.x * size.y * bytesPerPixel;
					}
				}
			}

			return (int)math.ceil(total / 1024.0 / 1024.0);
		}

		private static bool AnyNull(List<Texture2D> textures)
		{
			if (textures == null) return true;

			for (var i = 0; i < textures.Count; i++)
			{
				if (textures[i] == null)
				{
					ErrorText = i.ToString(); return true;
				}
			}

			return false;
		}

		private static bool AnyNotReadable(List<Texture2D> textures)
		{
			if (textures == null) return false;

			for (var i = 0; i < textures.Count; i++)
			{
				var t = textures[i];

				if (t != null && t.isReadable == false)
				{
					ErrorText = i.ToString() + " (" + t.name + ")"; return true;
				}
			}

			return false;
		}

		private static bool AnyNotRed(List<Texture2D> textures)
		{
			if (textures == null) return false;

			for (var i = 0; i < textures.Count; i++)
			{
				var t = textures[i];

				if (t != null)
				{
					if (t.format != TextureFormat.R8 && t.format != TextureFormat.R16)
					{
						ErrorText = i.ToString() + " (" + t.name + ")"; return true;
					}
				}
			}

			return false;
		}

		private static bool AnySizeMismatch(List<Texture2D> textures)
		{
			if (textures == null) return false;

			var size = int2.zero;

			for (var i = 0; i < textures.Count; i++)
			{
				var t = textures[i];

				if (t != null)
				{
					if (size.Equals(int2.zero) == true)
					{
						size.x = t.width; size.y = t.height;
					}
					else if (size.x != t.width || size.y != t.height)
					{
						ErrorText = i.ToString() + " (" + t.name + ")"; return true;
					}
				}
			}

			return false;
		}

		private static bool AnyFormatMismatch(List<Texture2D> textures)
		{
			if (textures == null) return false;

			var format = (TextureFormat)(-1);

			for (var i = 0; i < textures.Count; i++)
			{
				var t = textures[i];

				if (t != null)
				{
					if (format.Equals((TextureFormat)(-1)) == true)
					{
						format = t.format;
					}
					else if (format != t.format)
					{
						ErrorText = i.ToString() + " (" + t.name + ")"; return true;
					}
				}
			}

			return false;
		}
	}
}
#endif