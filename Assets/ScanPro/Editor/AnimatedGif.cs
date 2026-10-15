using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ScanPro704.Editor
{
    // GIF89a with a fixed 3/3/2 RGB palette. Frequent LZW clears keep all codes
    // 9 bits while remaining standards-compliant; sufficient for short previews.
    public sealed class AnimatedGif : IDisposable
    {
        readonly BinaryWriter writer;
        readonly int width, height;
        public AnimatedGif(string path, int width, int height)
        {
            this.width = width; this.height = height; writer = new BinaryWriter(File.Create(path));
            writer.Write(Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)width); writer.Write((ushort)height);
            writer.Write((byte)0xf7); writer.Write((byte)0); writer.Write((byte)0);
            for (var i = 0; i < 256; i++) { writer.Write((byte)(((i >> 5) & 7) * 255 / 7)); writer.Write((byte)(((i >> 2) & 7) * 255 / 7)); writer.Write((byte)((i & 3) * 255 / 3)); }
            writer.Write(new byte[] { 0x21,0xff,11 }); writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); writer.Write(new byte[] { 3,1,0,0,0 });
        }
        public void Add(Color32[] bottomUpPixels, int delayCentiseconds = 10)
        {
            writer.Write(new byte[] { 0x21,0xf9,4,4 }); writer.Write((ushort)delayCentiseconds); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((byte)0x2c); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)width); writer.Write((ushort)height); writer.Write((byte)0); writer.Write((byte)8);
            using (var stream = new MemoryStream())
            {
                int bits = 0, bitCount = 0, count = 0;
                void Code(int code)
                {
                    bits |= code << bitCount; bitCount += 9;
                    while (bitCount >= 8) { stream.WriteByte((byte)bits); bits >>= 8; bitCount -= 8; }
                }
                Code(256);
                for (var y = height - 1; y >= 0; y--) for (var x = 0; x < width; x++)
                {
                    var p = bottomUpPixels[y * width + x];
                    Code(((p.r * 7 / 255) << 5) | ((p.g * 7 / 255) << 2) | (p.b * 3 / 255));
                    if (++count == 128) { Code(256); count = 0; }
                }
                Code(257); if (bitCount > 0) stream.WriteByte((byte)bits);
                var bytes = stream.ToArray();
                for (var i = 0; i < bytes.Length; i += 255) { var length = Math.Min(255, bytes.Length - i); writer.Write((byte)length); writer.Write(bytes, i, length); }
                writer.Write((byte)0);
            }
        }
        public void Dispose() { writer.Write((byte)0x3b); writer.Dispose(); }
    }
}
