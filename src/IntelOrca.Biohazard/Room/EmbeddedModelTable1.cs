using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IntelOrca.Biohazard.Room
{
    public readonly struct EmbeddedModelTable1
    {
        public ReadOnlyMemory<byte> Data { get; }

        public EmbeddedModelTable1(ReadOnlyMemory<byte> data)
        {
            Data = data;
        }

        public int Count => Data.Length / 8;
        public ReadOnlySpan<ModelTextureOffset> Offsets => MemoryMarshal.Cast<byte, ModelTextureOffset>(Data.Span);
    }

    public readonly struct EmbeddedModelTable2
    {
        public ReadOnlyMemory<byte> Data { get; }

        public EmbeddedModelTable2(ReadOnlyMemory<byte> data)
        {
            Data = data;
        }

        public int Count => Data.Length / 8;
        public ReadOnlySpan<TextureModelOffset> Offsets => MemoryMarshal.Cast<byte, TextureModelOffset>(Data.Span);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ModelTextureOffset
    {
        private readonly int _model;
        private readonly int _texture;

        public int Model => _model;
        public int Texture => _texture;

        public ModelTextureOffset(int model, int texture)
        {
            _model = model;
            _texture = texture;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TextureModelOffset
    {
        private readonly int _texture;
        private readonly int _model;

        public int Texture => _texture;
        public int Model => _model;

        public TextureModelOffset(int texture, int model)
        {
            _texture = texture;
            _model = model;
        }
    }

    /// <summary>
    /// Used for RDT builders so that they can refer to models and textures by index.
    /// </summary>
    [DebuggerDisplay("Model = {Model} Texture = {Texture}")]
    public readonly struct ModelTextureIndex
    {
        public int Model { get; }
        public int Texture { get; }

        public ModelTextureIndex(int model, int texture)
        {
            Model = model;
            Texture = texture;
        }
    }
}
