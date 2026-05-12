using System;
using System.Buffers;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;
using EntitiesBinaryReader = Unity.Entities.Serialization.BinaryReader;

namespace VVardenfell.Runtime.Cache
{
    sealed unsafe class RuntimeCellSectionPayloadBinaryReader : EntitiesBinaryReader
    {
        const int BufferSize = 64 * 1024;

        readonly string _path;
        readonly FileStream _stream;
        readonly byte[] _buffer;
        readonly long _payloadOffset;
        readonly int _payloadLength;
        bool _disposed;

        public RuntimeCellSectionPayloadBinaryReader(string path, long payloadOffset, int payloadLength)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Runtime cell section path is empty.");
            if (payloadOffset < 0)
                throw new InvalidDataException($"Runtime cell section '{path}' has invalid entity payload offset {payloadOffset}.");
            if (payloadLength <= 0)
                throw new InvalidDataException($"Runtime cell section '{path}' has invalid entity payload length {payloadLength}.");

            _path = path;
            _payloadOffset = payloadOffset;
            _payloadLength = payloadLength;
            _stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Min(BufferSize, payloadLength));
            Position = 0;
        }

        public long Position
        {
            get => _stream.Position - _payloadOffset;
            set
            {
                if ((ulong)value > (ulong)_payloadLength)
                    throw new ArgumentOutOfRangeException(nameof(value), $"Runtime cell section '{_path}' seek {value} is outside entity payload length {_payloadLength}.");
                _stream.Position = _payloadOffset + value;
            }
        }

        public void ReadBytes(void* data, int bytes)
        {
            if (bytes < 0)
                throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Read byte count must be non-negative.");
            if (bytes == 0)
                return;
            if (Position + bytes > _payloadLength)
                throw new EndOfStreamException($"Runtime cell section '{_path}' read {bytes} bytes beyond entity payload length {_payloadLength}.");

            int remaining = bytes;
            fixed (byte* fixedBuffer = _buffer)
            {
                while (remaining > 0)
                {
                    int read = _stream.Read(_buffer, 0, Math.Min(_buffer.Length, remaining));
                    if (read <= 0)
                        throw new EndOfStreamException($"Runtime cell section '{_path}' ended before entity payload was fully read.");

                    UnsafeUtility.MemCpy(data, fixedBuffer, read);
                    data = (byte*)data + read;
                    remaining -= read;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _stream.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
