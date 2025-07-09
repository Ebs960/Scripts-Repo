using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

namespace SpaceGraphicsToolkit.Landscape
{
	public partial class SgtLandscape
	{
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct Color16
		{
			public byte r;
			public byte g;

			public Color16(byte newR, byte newG)
			{
				r = newR;
				g = newG;
			}
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct Color24
		{
			public byte r;
			public byte g;
			public byte b;

			public Color24(byte newR, byte newG, byte newB)
			{
				r = newR;
				g = newG;
				b = newB;
			}
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		public struct Vertex
		{
			public float3 Position;
			public float4 Texcoord;
		}

		/// <summary>This struct allows you to load a alpha/opacity/mask Texture2D into an easily readable format.</summary>
		public struct MaskData
		{
			public NativeArray<byte> Data;

			public int2 Size;

			public int Stride;

			public bool Created;

			public void Create(Texture2D opacityTex)
			{
				if (opacityTex != null && opacityTex.isReadable == true)
				{
					Data    = opacityTex.GetPixelData<byte>(0);
					Size    = new int2(opacityTex.width, opacityTex.height);
					Created = false;
				}
				else
				{
					Data    = new NativeArray<byte>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
					Size    = new int2(1, 1);
					Created = true;

					Data[0] = 255;
				}

				Stride = Data.Length / (Size.x * Size.y);
			}

			public void Dispose()
			{
				if (Created == true)
				{
					Data.Dispose();
				}
			}
		}

		/// <summary>This struct allows you to load a height Texture2D into an easily readable format.</summary>
		public struct HeightData
		{
			public NativeArray<byte>   Data08;
			public NativeArray<ushort> Data16;

			public int2   Size;
			public float2 Range;

			public bool Created08;
			public bool Created16;

			public void Create(Texture2D heightTex, float heightMidpoint, float heightScale)
			{
				Range  = new float2(-heightScale * heightMidpoint, heightScale);

				if (heightTex != null && heightTex.isReadable == true)
				{
					if (heightTex.format == TextureFormat.R16)
					{
						Data16    = heightTex.GetPixelData<ushort>(0);
						Size      = new int2(heightTex.width, heightTex.height);
						Created16 = false;

						Data08    = new NativeArray<byte>(0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
						Created08 = true;

						return;
					}
					else if (heightTex.format == TextureFormat.R8 || heightTex.format == TextureFormat.Alpha8)
					{
						Data08    = heightTex.GetPixelData<byte>(0);
						Size      = new int2(heightTex.width, heightTex.height);
						Created08 = false;

						Data16    = new NativeArray<ushort>(0, Allocator.Persistent, NativeArrayOptions.ClearMemory);
						Created16 = true;

						return;
					}
				}

				Data08    = new NativeArray<byte  >(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
				Data16    = new NativeArray<ushort>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
				Size      = new int2(1, 1);
				Created08 = true;
				Created16 = true;

				Data08[0] = 0;
				Data16[0] = 0;
			}

			public void Dispose()
			{
				if (Created08 == true)
				{
					Data08.Dispose();

					Created08 = false;
				}

				if (Created16 == true)
				{
					Data16.Dispose();

					Created16 = false;
				}
			}
		}

		public class CachedTopologyData
		{
			public RenderTexture Texture;

			public int2 Size;

			private int referenceCount;

			private static Dictionary<Texture, CachedTopologyData> cache = new Dictionary<Texture, CachedTopologyData>();

			[System.NonSerialized]
			private static Material material;

			private static readonly int _CwHeightTex  = Shader.PropertyToID("_CwHeightTex");
			private static readonly int _CwHeightSize = Shader.PropertyToID("_CwHeightSize");

			public static CachedTopologyData TryCreate(Texture heightTex)
			{
				if (heightTex == null)
				{
					heightTex = Texture2D.blackTexture;
				}

				var instance = default(CachedTopologyData);

				if (cache.TryGetValue(heightTex, out instance) == false || (instance.Texture == null && cache.Remove(heightTex)))
				{
					instance = new CachedTopologyData(heightTex);

					cache.Add(heightTex, instance);
				}

				instance.referenceCount += 1;

				return instance;
			}

			private CachedTopologyData(Texture heightTex)
			{
				if (material == null) { material = new Material(Shader.Find("Hidden/SgtTopology")); material.hideFlags = HideFlags.DontSave; }

				var mips = (int)Mathf.Floor(Mathf.Log(Mathf.Max(heightTex.width, heightTex.height), 2)) + 1;

				Texture = new RenderTexture(heightTex.width, heightTex.height, 0, RenderTextureFormat.ARGBFloat, mips);
				Size    = new int2(heightTex.width, heightTex.height);

				//Texture.enableRandomWrite = true;
				Texture.useMipMap         = true;
				Texture.autoGenerateMips  = false;
				Texture.hideFlags         = HideFlags.DontSave;
				Texture.filterMode        = FilterMode.Bilinear;
				Texture.wrapModeU         = heightTex.wrapModeU;
				Texture.wrapModeV         = heightTex.wrapModeV;
				Texture.Create();

				material.SetTexture(_CwHeightTex, heightTex);
				material.SetVector(_CwHeightSize, new Vector2(heightTex.width, heightTex.height));

				var oldActive = RenderTexture.active;

				Graphics.Blit(default(Texture), Texture, material, 0);

				RenderTexture.active = oldActive;

				Texture.GenerateMips();
			}

			public void TryDispose()
			{
				if (--referenceCount <= 0)
				{
					DestroyImmediate(Texture);

					cache.Remove(Texture);
				}
			}
		}

		/// <summary>This struct allows you to load a height Texture2D into a height/slope RenderTexture for GPU use.</summary>
		public struct TopologyData
		{
			public RenderTexture Texture;

			public float2 Size;

			public float3 Data;

			private CachedTopologyData cachedData;

			public void Create(Texture heightTex, float heightMid, float heightRange, float2 size, float strata)
			{
				CreateShared(heightTex);

				Data = new float3(size / Size / heightRange * new float2(2.0f, 2.0f), strata);
			}

			public void CreateSphere(Texture heightTex, float heightMid, float heightRange, float radius, float strata)
			{
				CreateShared(heightTex);

				var circ = new float2(math.PI * radius);

				Data = new float3(circ / Size / heightRange * new float2(4.0f, 2.0f), strata);
			}

			public void Dispose()
			{
				cachedData.TryDispose();

				cachedData = null;
			}

			private void CreateShared(Texture heightTex)
			{
				cachedData = CachedTopologyData.TryCreate(heightTex);

				Texture = cachedData.Texture;
				Size    = cachedData.Size;
			}
		}
	}
}