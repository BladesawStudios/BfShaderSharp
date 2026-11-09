using System;
using System.Text;

namespace Ryujinx.Graphics.Shader
{
    /// <summary>
    /// Generated source text held as UTF-8 bytes in a buffer that is reused by the next program built on the same thread.
    /// Building the text as a string, then as bytes, put several multi-megabyte copies of every program on the large object heap.
    /// </summary>
    public sealed class Utf8Code : IDisposable
    {
        private const int MaxKeptBytes = 256 * 1024 * 1024;

        [ThreadStatic]
        private static byte[] _spare;

        private byte[] _buffer;
        private int _length;

        internal Utf8Code()
        {
            _buffer = _spare ?? new byte[1 << 20];
            _spare = null;
        }

        public ReadOnlySpan<byte> Span => new(_buffer, 0, _length);

        internal void Append(string text)
        {
            Reserve(Encoding.UTF8.GetMaxByteCount(text.Length));
            _length += Encoding.UTF8.GetBytes(text, 0, text.Length, _buffer, _length);
        }

        internal void AppendSpaces(int count)
        {
            Reserve(count);
            _buffer.AsSpan(_length, count).Fill((byte)' ');
            _length += count;
        }

        internal void AppendNewLine()
        {
            Append(Environment.NewLine);
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(_buffer, 0, _length);
        }

        private void Reserve(int count)
        {
            if (_length + count <= _buffer.Length)
            {
                return;
            }

            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        }

        /// <summary>Gives the buffer back for the next program on this thread. The text must not be used afterwards.</summary>
        public void Dispose()
        {
            byte[] buffer = _buffer;

            _buffer = null;
            _length = 0;

            if (buffer != null && buffer.Length <= MaxKeptBytes && (_spare == null || _spare.Length < buffer.Length))
            {
                _spare = buffer;
            }
        }
    }
}
