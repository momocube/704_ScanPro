using System;
using System.Collections.Generic;
using System.Text;

namespace ScanPro704
{
    public readonly struct ScannerFrame
    {
        public readonly string Prefix, Code, Device;
        public ScannerFrame(string prefix, string code, string device) { Prefix = prefix; Code = code; Device = device; }
    }
    // Buffers are isolated by physical Raw Input device handle (or COM/source key).
    // A/0000 is complete at its fourth digit; optional CR/LF is consumed harmlessly.
    public sealed class ScannerFramer
    {
        sealed class Buffer { public readonly StringBuilder Text = new StringBuilder(); public double Last; }
        readonly Dictionary<string, Buffer> buffers = new Dictionary<string, Buffer>();
        public event Action<ScannerFrame> Frame;
        public double PartialTimeoutMs = 1000;
        public void Push(string device, char character, double milliseconds)
        {
            if (string.IsNullOrEmpty(device)) return;
            if (!buffers.TryGetValue(device, out var buffer)) buffers.Add(device, buffer = new Buffer());
            if (milliseconds - buffer.Last > PartialTimeoutMs) buffer.Text.Clear();
            buffer.Last = milliseconds;
            if (character == '\r' || character == '\n') { buffer.Text.Clear(); return; }
            if (character == '\b') { if (buffer.Text.Length > 0) buffer.Text.Length--; return; }
            if (buffer.Text.Length == 0 && !char.IsLetter(character)) return;
            buffer.Text.Append(char.ToUpperInvariant(character));
            if (buffer.Text.Length > 24) { buffer.Text.Clear(); return; }
            var text = buffer.Text.ToString(); var slash = text.IndexOf('/');
            if (slash < 1) return;
            var digitCount = text.Length - slash - 1;
            for (var i = slash + 1; i < text.Length; i++)
                if (text[i] < '0' || text[i] > '9') { buffer.Text.Clear(); return; }
            if (digitCount != 4) return;
            var result = new ScannerFrame(text.Substring(0, slash), text.Substring(slash + 1), device);
            buffer.Text.Clear(); Frame?.Invoke(result);
        }
        public void Reset() => buffers.Clear();
    }
}
