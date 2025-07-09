using Unity.Mathematics;
using Unity.Collections;

namespace SpaceGraphicsToolkit.Landscape
{
	public partial class SgtLandscape
	{
		public static float Sample_Cubic_Equirectangular(NativeArray<ushort> data, int2 size, double3 direction)
		{
			var s  = size / new double2(math.PI * 2.0, math.PI);
			var u  = (math.PI * 1.5 - math.atan2(direction.x, direction.z)) * s.x;
			var v  = (math.asin(direction.y) + math.PI * 0.5) * s.y;
			var uv = new double2(u, v);

			var fracX = (float)((uv.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((uv.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(uv.x % size.x);
			var y     = (long)math.floor(uv.y % size.y);

			var aa = Sample_WrapX(data, size, x - 1, y - 1); var ba = Sample_WrapX(data, size, x, y - 1); var ca = Sample_WrapX(data, size, x + 1, y - 1); var da = Sample_WrapX(data, size, x + 2, y - 1);
			var ab = Sample_WrapX(data, size, x - 1, y    ); var bb = Sample_WrapX(data, size, x, y    ); var cb = Sample_WrapX(data, size, x + 1, y    ); var db = Sample_WrapX(data, size, x + 2, y    );
			var ac = Sample_WrapX(data, size, x - 1, y + 1); var bc = Sample_WrapX(data, size, x, y + 1); var cc = Sample_WrapX(data, size, x + 1, y + 1); var dc = Sample_WrapX(data, size, x + 2, y + 1);
			var ad = Sample_WrapX(data, size, x - 1, y + 2); var bd = Sample_WrapX(data, size, x, y + 2); var cd = Sample_WrapX(data, size, x + 1, y + 2); var dd = Sample_WrapX(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Cubic_Equirectangular(NativeArray<byte> data, int2 size, double3 direction)
		{
			var s  = size / new double2(math.PI * 2.0, math.PI);
			var u  = (math.PI * 1.5 - math.atan2(direction.x, direction.z)) * s.x;
			var v  = (math.asin(direction.y) + math.PI * 0.5) * s.y;
			var uv = new double2(u, v);

			var fracX = (float)((uv.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((uv.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(uv.x % size.x);
			var y     = (long)math.floor(uv.y % size.y);

			var aa = Sample_WrapX(data, size, x - 1, y - 1); var ba = Sample_WrapX(data, size, x, y - 1); var ca = Sample_WrapX(data, size, x + 1, y - 1); var da = Sample_WrapX(data, size, x + 2, y - 1);
			var ab = Sample_WrapX(data, size, x - 1, y    ); var bb = Sample_WrapX(data, size, x, y    ); var cb = Sample_WrapX(data, size, x + 1, y    ); var db = Sample_WrapX(data, size, x + 2, y    );
			var ac = Sample_WrapX(data, size, x - 1, y + 1); var bc = Sample_WrapX(data, size, x, y + 1); var cc = Sample_WrapX(data, size, x + 1, y + 1); var dc = Sample_WrapX(data, size, x + 2, y + 1);
			var ad = Sample_WrapX(data, size, x - 1, y + 2); var bd = Sample_WrapX(data, size, x, y + 2); var cd = Sample_WrapX(data, size, x + 1, y + 2); var dd = Sample_WrapX(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Point(NativeArray<ushort> data, int2 size, int x, int y)
		{
			return data[x + y * size.x] / 65535.0f;
		}

		public static float Sample_Cubic(NativeArray<ushort> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var aa = Sample_Wrap(data, size, x - 1, y - 1); var ba = Sample_Wrap(data, size, x, y - 1); var ca = Sample_Wrap(data, size, x + 1, y - 1); var da = Sample_Wrap(data, size, x + 2, y - 1);
			var ab = Sample_Wrap(data, size, x - 1, y    ); var bb = Sample_Wrap(data, size, x, y    ); var cb = Sample_Wrap(data, size, x + 1, y    ); var db = Sample_Wrap(data, size, x + 2, y    );
			var ac = Sample_Wrap(data, size, x - 1, y + 1); var bc = Sample_Wrap(data, size, x, y + 1); var cc = Sample_Wrap(data, size, x + 1, y + 1); var dc = Sample_Wrap(data, size, x + 2, y + 1);
			var ad = Sample_Wrap(data, size, x - 1, y + 2); var bd = Sample_Wrap(data, size, x, y + 2); var cd = Sample_Wrap(data, size, x + 1, y + 2); var dd = Sample_Wrap(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Cubic(NativeArray<byte> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var aa = Sample_Wrap(data, size, x - 1, y - 1); var ba = Sample_Wrap(data, size, x, y - 1); var ca = Sample_Wrap(data, size, x + 1, y - 1); var da = Sample_Wrap(data, size, x + 2, y - 1);
			var ab = Sample_Wrap(data, size, x - 1, y    ); var bb = Sample_Wrap(data, size, x, y    ); var cb = Sample_Wrap(data, size, x + 1, y    ); var db = Sample_Wrap(data, size, x + 2, y    );
			var ac = Sample_Wrap(data, size, x - 1, y + 1); var bc = Sample_Wrap(data, size, x, y + 1); var cc = Sample_Wrap(data, size, x + 1, y + 1); var dc = Sample_Wrap(data, size, x + 2, y + 1);
			var ad = Sample_Wrap(data, size, x - 1, y + 2); var bd = Sample_Wrap(data, size, x, y + 2); var cd = Sample_Wrap(data, size, x + 1, y + 2); var dd = Sample_Wrap(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Cubic_WrapX(NativeArray<ushort> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var aa = Sample_WrapX(data, size, x - 1, y - 1); var ba = Sample_WrapX(data, size, x, y - 1); var ca = Sample_WrapX(data, size, x + 1, y - 1); var da = Sample_WrapX(data, size, x + 2, y - 1);
			var ab = Sample_WrapX(data, size, x - 1, y    ); var bb = Sample_WrapX(data, size, x, y    ); var cb = Sample_WrapX(data, size, x + 1, y    ); var db = Sample_WrapX(data, size, x + 2, y    );
			var ac = Sample_WrapX(data, size, x - 1, y + 1); var bc = Sample_WrapX(data, size, x, y + 1); var cc = Sample_WrapX(data, size, x + 1, y + 1); var dc = Sample_WrapX(data, size, x + 2, y + 1);
			var ad = Sample_WrapX(data, size, x - 1, y + 2); var bd = Sample_WrapX(data, size, x, y + 2); var cd = Sample_WrapX(data, size, x + 1, y + 2); var dd = Sample_WrapX(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Cubic_WrapX(NativeArray<byte> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var aa = Sample_WrapX(data, size, x - 1, y - 1); var ba = Sample_WrapX(data, size, x, y - 1); var ca = Sample_WrapX(data, size, x + 1, y - 1); var da = Sample_WrapX(data, size, x + 2, y - 1);
			var ab = Sample_WrapX(data, size, x - 1, y    ); var bb = Sample_WrapX(data, size, x, y    ); var cb = Sample_WrapX(data, size, x + 1, y    ); var db = Sample_WrapX(data, size, x + 2, y    );
			var ac = Sample_WrapX(data, size, x - 1, y + 1); var bc = Sample_WrapX(data, size, x, y + 1); var cc = Sample_WrapX(data, size, x + 1, y + 1); var dc = Sample_WrapX(data, size, x + 2, y + 1);
			var ad = Sample_WrapX(data, size, x - 1, y + 2); var bd = Sample_WrapX(data, size, x, y + 2); var cd = Sample_WrapX(data, size, x + 1, y + 2); var dd = Sample_WrapX(data, size, x + 2, y + 2);

			var a = Hermite(aa, ba, ca, da, fracX);
			var b = Hermite(ab, bb, cb, db, fracX);
			var c = Hermite(ac, bc, cc, dc, fracX);
			var d = Hermite(ad, bd, cd, dd, fracX);

			return Hermite(a, b, c, d, fracY);
		}

		public static float Sample_Linear_Clamp(NativeArray<ushort> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var bl0 = Sample_Clamp(data, size, x    , y    );
			var br0 = Sample_Clamp(data, size, x + 1, y    );
			var tl0 = Sample_Clamp(data, size, x    , y + 1);
			var tr0 = Sample_Clamp(data, size, x + 1, y + 1);

			var b0 = math.lerp(bl0, br0, fracX);
			var t0 = math.lerp(tl0, tr0, fracX);

			return math.lerp(b0, t0, fracY);
		}

		public static float Sample_Linear_Clamp(NativeArray<byte> data, int2 size, double2 pixel)
		{
			var fracX = (float)((pixel.x % 1.0 + 1.0) % 1.0);
			var fracY = (float)((pixel.y % 1.0 + 1.0) % 1.0);
			var x     = (long)math.floor(pixel.x % size.x);
			var y     = (long)math.floor(pixel.y % size.y);

			var bl0 = Sample_Clamp(data, size, x    , y    );
			var br0 = Sample_Clamp(data, size, x + 1, y    );
			var tl0 = Sample_Clamp(data, size, x    , y + 1);
			var tr0 = Sample_Clamp(data, size, x + 1, y + 1);

			var b0 = math.lerp(bl0, br0, fracX);
			var t0 = math.lerp(tl0, tr0, fracX);

			return math.lerp(b0, t0, fracY);
		}

		public static float Sample_WrapX(NativeArray<ushort> data, int2 size, long x, long y)
		{
			x = (x % size.x + size.x) % size.x;
			y = math.clamp(y, 0, size.y - 1);

			return data[(int)x + (int)y * size.x] / 65535.0f;
		}

		public static float Sample_WrapX(NativeArray<byte> data, int2 size, long x, long y)
		{
			x = (x % size.x + size.x) % size.x;
			y = math.clamp(y, 0, size.y - 1);

			return data[(int)x + (int)y * size.x] / 255.0f;
		}

		public static float Sample_Clamp(NativeArray<ushort> data, int2 size, long x, long y)
		{
			x = math.clamp(x, 0, size.x - 1);
			y = math.clamp(y, 0, size.y - 1);

			return data[(int)x + (int)y * size.x] / 65535.0f;
		}

		public static float Sample_Clamp(NativeArray<byte> data, int2 size, long x, long y)
		{
			x = math.clamp(x, 0, size.x - 1);
			y = math.clamp(y, 0, size.y - 1);

			return data[(int)x + (int)y * size.x] / 255.0f;
		}

		public static float Sample_Wrap(NativeArray<ushort> data, int2 size, long x, long y)
		{
			x = (x % size.x + size.x) % size.x;
			y = (y % size.y + size.y) % size.y;

			return data[(int)x + (int)y * size.x] / 65535.0f;
		}

		public static float Sample_Wrap(NativeArray<byte> data, int2 size, long x, long y)
		{
			x = (x % size.x + size.x) % size.x;
			y = (y % size.y + size.y) % size.y;

			return data[(int)x + (int)y * size.x] / 255.0f;
		}

		public static float Hermite(float a, float b, float c, float d, float t)
		{
			var tt   = t * t;
			var tt3  = tt * 3.0f;
			var ttt  = t * tt;
			var ttt2 = ttt * 2.0f;
			var m0   = (c - a) * 0.5f;
			var m1   = (d - b) * 0.5f;
			var a0   =  ttt2 - tt3 + 1.0f;
			var a1   =  ttt  - tt * 2.0f + t;
			var a2   =  ttt  - tt;
			var a3   = -ttt2 + tt3;

			return a0 * b + a1 * m0 + a2 * m1 + a3 * c;
		}

		public static double4 GetEquirectangularCoord(double3 direction)
		{
			var u = math.atan2(direction.z, direction.x) / (math.PI_DBL * 2.0) + 0.5;
			var v = math.asin(direction.y) / math.PI_DBL + 0.5;
			var w = direction.y * 0.3 + 0.5;

			return new double4(u, v, u, w);
		}
	}
}