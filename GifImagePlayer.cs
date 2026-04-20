using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays animated GIFs from StreamingAssets into a RawImage.
/// Uses a Unity-native decoder so it works without System.Drawing.
/// </summary>
public class GifImagePlayer : MonoBehaviour
{
    private const byte ImageSeparator = 0x2C;
    private const byte ExtensionIntroducer = 0x21;
    private const byte Trailer = 0x3B;
    private const byte GraphicControlLabel = 0xF9;

    [Serializable]
    private struct GifFrameData
    {
        public Texture2D texture;
        public float duration;
    }

    private struct GraphicControlState
    {
        public int disposalMethod;
        public int delayCentiseconds;
        public bool transparencyEnabled;
        public byte transparentColorIndex;
    }

    private struct GifFrameDescriptor
    {
        public int left;
        public int top;
        public int width;
        public int height;
        public bool interlaced;
        public Color32[] colorTable;
        public byte[] imageData;
        public GraphicControlState graphicsControl;
    }

    [SerializeField] private RawImage targetImage;
    [SerializeField] private bool loop = true;

    private readonly Dictionary<string, List<GifFrameData>> cache = new Dictionary<string, List<GifFrameData>>();
    private Coroutine playbackCoroutine;
    private string currentRelativePath;

    public bool IsPlayingGif => playbackCoroutine != null;

    public void StopPlayback(bool hideImage = false)
    {
        if (playbackCoroutine != null)
        {
            StopCoroutine(playbackCoroutine);
            playbackCoroutine = null;
        }

        currentRelativePath = null;

        if (targetImage != null)
        {
            targetImage.texture = null;
            if (hideImage)
                targetImage.gameObject.SetActive(false);
        }
    }

    public bool PlayFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || targetImage == null)
        {
            StopPlayback(true);
            return false;
        }

        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.Equals(currentRelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase) && IsPlayingGif)
            return true;

        List<GifFrameData> frames = GetOrLoadFrames(normalizedPath);
        if (frames == null || frames.Count == 0)
        {
            StopPlayback(true);
            return false;
        }

        StopPlayback();
        currentRelativePath = normalizedPath;
        targetImage.gameObject.SetActive(true);
        playbackCoroutine = StartCoroutine(PlayFrames(frames));
        return true;
    }

    private IEnumerator PlayFrames(List<GifFrameData> frames)
    {
        do
        {
            for (int index = 0; index < frames.Count; index++)
            {
                if (targetImage != null)
                    targetImage.texture = frames[index].texture;

                yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, frames[index].duration));
            }
        }
        while (loop);

        playbackCoroutine = null;
    }

    private List<GifFrameData> GetOrLoadFrames(string relativePath)
    {
        if (cache.TryGetValue(relativePath, out List<GifFrameData> cached))
            return cached;

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogWarning($"[GifImagePlayer] GIF file not found: {fullPath}");
            return null;
        }

        List<GifFrameData> loadedFrames = LoadFrames(fullPath);
        if (loadedFrames != null && loadedFrames.Count > 0)
            cache[relativePath] = loadedFrames;

        return loadedFrames;
    }

    private static List<GifFrameData> LoadFrames(string fullPath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            return DecodeGif(bytes, Path.GetFileName(fullPath));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GifImagePlayer] Failed to decode GIF '{fullPath}': {ex.Message}");
            return null;
        }
    }

    private static List<GifFrameData> DecodeGif(byte[] bytes, string sourceName)
    {
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = new BinaryReader(stream))
        {
            string signature = new string(reader.ReadChars(6));
            if (signature != "GIF87a" && signature != "GIF89a")
                throw new InvalidDataException($"'{sourceName}' is not a valid GIF file.");

            int canvasWidth = reader.ReadUInt16();
            int canvasHeight = reader.ReadUInt16();
            byte packedFields = reader.ReadByte();
            bool hasGlobalColorTable = (packedFields & 0x80) != 0;
            int globalColorTableSize = 1 << ((packedFields & 0x07) + 1);
            reader.ReadByte();
            reader.ReadByte();

            Color32[] globalColorTable = hasGlobalColorTable ? ReadColorTable(reader, globalColorTableSize) : null;
            var descriptors = new List<GifFrameDescriptor>();
            GraphicControlState graphicControl = CreateDefaultGraphicControlState();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte blockType = reader.ReadByte();
                if (blockType == Trailer)
                    break;

                if (blockType == ExtensionIntroducer)
                {
                    byte label = reader.ReadByte();
                    if (label == GraphicControlLabel)
                        graphicControl = ReadGraphicControlExtension(reader);
                    else
                        SkipSubBlocks(reader);

                    continue;
                }

                if (blockType != ImageSeparator)
                    throw new InvalidDataException($"'{sourceName}' contains unsupported block type 0x{blockType:X2}.");

                descriptors.Add(ReadImageDescriptor(reader, globalColorTable, graphicControl));
                graphicControl = CreateDefaultGraphicControlState();
            }

            if (descriptors.Count == 0)
                return null;

            return ComposeFrames(canvasWidth, canvasHeight, descriptors);
        }
    }

    private static GraphicControlState CreateDefaultGraphicControlState()
    {
        return new GraphicControlState
        {
            disposalMethod = 0,
            delayCentiseconds = 6,
            transparencyEnabled = false,
            transparentColorIndex = 0
        };
    }

    private static GraphicControlState ReadGraphicControlExtension(BinaryReader reader)
    {
        byte blockSize = reader.ReadByte();
        if (blockSize != 4)
            throw new InvalidDataException("Unsupported GIF graphics control extension block size.");

        byte packed = reader.ReadByte();
        ushort delay = reader.ReadUInt16();
        byte transparentIndex = reader.ReadByte();
        byte terminator = reader.ReadByte();
        if (terminator != 0)
            throw new InvalidDataException("Malformed GIF graphics control extension terminator.");

        return new GraphicControlState
        {
            disposalMethod = (packed >> 2) & 0x07,
            delayCentiseconds = delay <= 1 ? 6 : delay,
            transparencyEnabled = (packed & 0x01) != 0,
            transparentColorIndex = transparentIndex
        };
    }

    private static GifFrameDescriptor ReadImageDescriptor(BinaryReader reader, Color32[] globalColorTable, GraphicControlState graphicControl)
    {
        int left = reader.ReadUInt16();
        int top = reader.ReadUInt16();
        int width = reader.ReadUInt16();
        int height = reader.ReadUInt16();
        byte packed = reader.ReadByte();

        bool hasLocalColorTable = (packed & 0x80) != 0;
        bool interlaced = (packed & 0x40) != 0;
        int localColorTableSize = 1 << ((packed & 0x07) + 1);
        Color32[] colorTable = hasLocalColorTable ? ReadColorTable(reader, localColorTableSize) : globalColorTable;
        if (colorTable == null || colorTable.Length == 0)
            throw new InvalidDataException("GIF image frame is missing a color table.");

        byte minimumCodeSize = reader.ReadByte();
        byte[] imageData = ReadSubBlocks(reader);

        return new GifFrameDescriptor
        {
            left = left,
            top = top,
            width = width,
            height = height,
            interlaced = interlaced,
            colorTable = colorTable,
            imageData = LzwDecode(imageData, minimumCodeSize, width * height),
            graphicsControl = graphicControl
        };
    }

    private static Color32[] ReadColorTable(BinaryReader reader, int size)
    {
        var table = new Color32[size];
        for (int index = 0; index < size; index++)
        {
            byte red = reader.ReadByte();
            byte green = reader.ReadByte();
            byte blue = reader.ReadByte();
            table[index] = new Color32(red, green, blue, 255);
        }

        return table;
    }

    private static byte[] ReadSubBlocks(BinaryReader reader)
    {
        using (var output = new MemoryStream())
        {
            while (true)
            {
                int blockSize = reader.ReadByte();
                if (blockSize == 0)
                    break;

                byte[] block = reader.ReadBytes(blockSize);
                if (block.Length != blockSize)
                    throw new EndOfStreamException("Unexpected end of GIF data while reading sub-blocks.");

                output.Write(block, 0, block.Length);
            }

            return output.ToArray();
        }
    }

    private static void SkipSubBlocks(BinaryReader reader)
    {
        while (true)
        {
            int blockSize = reader.ReadByte();
            if (blockSize == 0)
                break;

            byte[] skipped = reader.ReadBytes(blockSize);
            if (skipped.Length != blockSize)
                throw new EndOfStreamException("Unexpected end of GIF data while skipping sub-blocks.");
        }
    }

    private static byte[] LzwDecode(byte[] compressedData, int minimumCodeSize, int expectedSize)
    {
        if (minimumCodeSize < 2 || minimumCodeSize > 8)
            throw new InvalidDataException($"Unsupported GIF LZW minimum code size: {minimumCodeSize}.");

        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;
        int nextCode = endCode + 1;
        int codeSize = minimumCodeSize + 1;
        int codeMask = (1 << codeSize) - 1;

        var prefixes = new int[4096];
        var suffixes = new byte[4096];
        var pixelStack = new byte[4097];
        var output = new byte[expectedSize];

        for (int code = 0; code < clearCode; code++)
        {
            prefixes[code] = -1;
            suffixes[code] = (byte)code;
        }

        int datum = 0;
        int bits = 0;
        int dataIndex = 0;
        int oldCode = -1;
        int first = 0;
        int outputIndex = 0;
        int stackTop = 0;

        while (outputIndex < expectedSize)
        {
            while (bits < codeSize)
            {
                if (dataIndex >= compressedData.Length)
                    goto Finish;

                datum |= compressedData[dataIndex++] << bits;
                bits += 8;
            }

            int code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code == clearCode)
            {
                codeSize = minimumCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                nextCode = endCode + 1;
                oldCode = -1;
                continue;
            }

            if (code == endCode)
                break;

            if (oldCode == -1)
            {
                if (code >= clearCode)
                    throw new InvalidDataException("GIF LZW stream contains an invalid starter code.");

                output[outputIndex++] = suffixes[code];
                first = suffixes[code];
                oldCode = code;
                continue;
            }

            int inCode = code;
            if (code >= nextCode)
            {
                pixelStack[stackTop++] = (byte)first;
                code = oldCode;
            }

            while (code >= clearCode)
            {
                if (code >= nextCode)
                    throw new InvalidDataException("GIF LZW stream contains an out-of-range code.");

                pixelStack[stackTop++] = suffixes[code];
                code = prefixes[code];
                if (stackTop >= pixelStack.Length)
                    throw new InvalidDataException("GIF LZW stream overflowed the decode stack.");
            }

            first = suffixes[code];
            pixelStack[stackTop++] = (byte)first;

            if (nextCode < 4096)
            {
                prefixes[nextCode] = oldCode;
                suffixes[nextCode] = (byte)first;
                nextCode++;

                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }

            oldCode = inCode;

            while (stackTop > 0 && outputIndex < expectedSize)
                output[outputIndex++] = pixelStack[--stackTop];
        }

Finish:
        if (outputIndex < expectedSize)
            Array.Clear(output, outputIndex, expectedSize - outputIndex);

        return output;
    }

    private static List<GifFrameData> ComposeFrames(int canvasWidth, int canvasHeight, List<GifFrameDescriptor> descriptors)
    {
        int pixelCount = canvasWidth * canvasHeight;
        var canvas = new Color32[pixelCount];
        var transparent = new Color32(0, 0, 0, 0);
        for (int index = 0; index < pixelCount; index++)
            canvas[index] = transparent;

        var frames = new List<GifFrameData>(descriptors.Count);
        foreach (GifFrameDescriptor descriptor in descriptors)
        {
            Color32[] previousCanvas = descriptor.graphicsControl.disposalMethod == 3 ? (Color32[])canvas.Clone() : null;
            DrawFrameOntoCanvas(canvas, canvasWidth, canvasHeight, descriptor);
            frames.Add(CreateFrameData(canvasWidth, canvasHeight, canvas, descriptor.graphicsControl.delayCentiseconds));
            ApplyDisposal(canvas, canvasWidth, canvasHeight, descriptor, previousCanvas, transparent);
        }

        return frames;
    }

    private static void DrawFrameOntoCanvas(Color32[] canvas, int canvasWidth, int canvasHeight, GifFrameDescriptor descriptor)
    {
        int pixelIndex = 0;
        for (int row = 0; row < descriptor.height; row++)
        {
            int targetRow = descriptor.interlaced ? GetInterlacedRow(row, descriptor.height) : row;
            int canvasY = descriptor.top + targetRow;
            if (canvasY < 0 || canvasY >= canvasHeight)
            {
                pixelIndex += descriptor.width;
                continue;
            }

            for (int column = 0; column < descriptor.width; column++, pixelIndex++)
            {
                int canvasX = descriptor.left + column;
                if (canvasX < 0 || canvasX >= canvasWidth)
                    continue;

                byte colorIndex = descriptor.imageData[pixelIndex];
                if (descriptor.graphicsControl.transparencyEnabled && colorIndex == descriptor.graphicsControl.transparentColorIndex)
                    continue;

                if (colorIndex >= descriptor.colorTable.Length)
                    continue;

                canvas[(canvasY * canvasWidth) + canvasX] = descriptor.colorTable[colorIndex];
            }
        }
    }

    private static int GetInterlacedRow(int rowIndex, int frameHeight)
    {
        int[] starts = { 0, 4, 2, 1 };
        int[] steps = { 8, 8, 4, 2 };
        int currentRow = 0;

        for (int pass = 0; pass < starts.Length; pass++)
        {
            for (int y = starts[pass]; y < frameHeight; y += steps[pass])
            {
                if (currentRow == rowIndex)
                    return y;

                currentRow++;
            }
        }

        return rowIndex;
    }

    private static GifFrameData CreateFrameData(int width, int height, Color32[] canvas, int delayCentiseconds)
    {
        var pixels = new Color32[canvas.Length];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * width;
            int targetRow = (height - 1 - y) * width;
            Array.Copy(canvas, sourceRow, pixels, targetRow, width);
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        return new GifFrameData
        {
            texture = texture,
            duration = Mathf.Max(0.02f, delayCentiseconds / 100f)
        };
    }

    private static void ApplyDisposal(Color32[] canvas, int canvasWidth, int canvasHeight, GifFrameDescriptor descriptor, Color32[] previousCanvas, Color32 transparent)
    {
        switch (descriptor.graphicsControl.disposalMethod)
        {
            case 2:
                ClearRegion(canvas, canvasWidth, canvasHeight, descriptor.left, descriptor.top, descriptor.width, descriptor.height, transparent);
                break;
            case 3:
                if (previousCanvas != null)
                    Array.Copy(previousCanvas, canvas, canvas.Length);
                break;
        }
    }

    private static void ClearRegion(Color32[] canvas, int canvasWidth, int canvasHeight, int left, int top, int width, int height, Color32 color)
    {
        int minY = Mathf.Max(0, top);
        int maxY = Mathf.Min(canvasHeight, top + height);
        int minX = Mathf.Max(0, left);
        int maxX = Mathf.Min(canvasWidth, left + width);

        for (int y = minY; y < maxY; y++)
        {
            int rowStart = y * canvasWidth;
            for (int x = minX; x < maxX; x++)
                canvas[rowStart + x] = color;
        }
    }

    private void OnDisable()
    {
        StopPlayback();
    }

    private void OnDestroy()
    {
        foreach (List<GifFrameData> frames in cache.Values)
        {
            for (int index = 0; index < frames.Count; index++)
            {
                if (frames[index].texture != null)
                    Destroy(frames[index].texture);
            }
        }

        cache.Clear();
    }
}