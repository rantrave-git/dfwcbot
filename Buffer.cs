using System;
using System.Buffers;
using System.Text;

namespace DfwcResultsBot
{
    class Buffer : IDisposable
    {
        byte[] _buffer;
        int position = 0;
        Span<byte> Span => _buffer.AsSpan(position);
        public Memory<byte> Value => _buffer.AsMemory(0, position);

        private void EnsureAllocatedSize(int moreBytes)
        {
            if (_buffer == null || _buffer.Length < position + moreBytes)
            {
                var b = ArrayPool<byte>.Shared.Rent(_buffer?.Length * 2 ?? 1024);
                if (_buffer != null)
                {
                    Value.CopyTo(b);
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
                _buffer = b;
            }
        }
        public Buffer Start()
        {
            position = 0;
            EnsureAllocatedSize(0); // allocate buffer
            return this;
        }
        public Buffer WriteNext(string s)
        {
            var len = Encoding.UTF8.GetByteCount(s);
            EnsureAllocatedSize(len);
            Encoding.UTF8.GetBytes(s, Span);
            position += len;
            return this;
        }
        public Buffer End()
        {
            Span[0] = 0x0d;
            Span[1] = 0x0a;
            EnsureAllocatedSize(2);
            position += 2;
            return this;
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }
    }
}
