using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace SampleServer
{
    public class IISHttpResponseBody : Stream
    {
        private readonly IISHttpContext _httpContext;
        private readonly object _lockObj = new object();

        private TaskCompletionSource<object> _flushTcs;
        private readonly object _flushLock = new object();
        private Action _flushCompleted;

        public IISHttpResponseBody(IISHttpContext httpContext)
        {
            _httpContext = httpContext;
            _flushCompleted = OnFlushCompleted;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public unsafe override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override unsafe Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var writableBuffer = default(WritableBuffer);

            // Don't allow overlapping writes
            lock (_lockObj)
            {
                _httpContext.StartWritingResponseBody();

                writableBuffer = _httpContext.Output.Writer.Alloc(1);
                var writer = new WritableBufferWriter(writableBuffer);
                if (count > 0)
                {
                    writer.Write(buffer, offset, count);
                }

                writableBuffer.Commit();
            }

            return FlushAsync(writableBuffer, cancellationToken);
        }
        private Task FlushAsync(WritableBuffer writableBuffer,
            CancellationToken cancellationToken)
        {
            var awaitable = writableBuffer.FlushAsync(cancellationToken);
            if (awaitable.IsCompleted)
            {
                // The flush task can't fail today
                return Task.CompletedTask;
            }
            return FlushAsyncAwaited(awaitable, writableBuffer.BytesWritten, cancellationToken);
        }

        private async Task FlushAsyncAwaited(WritableBufferAwaitable awaitable, long count, CancellationToken cancellationToken)
        {
            // https://github.com/dotnet/corefxlab/issues/1334
            // Since the flush awaitable doesn't currently support multiple awaiters
            // we need to use a task to track the callbacks.
            // All awaiters get the same task
            lock (_flushLock)
            {
                if (_flushTcs == null || _flushTcs.Task.IsCompleted)
                {
                    _flushTcs = new TaskCompletionSource<object>();

                    awaitable.OnCompleted(_flushCompleted);
                }
            }

            await _flushTcs.Task;

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void OnFlushCompleted()
        {
            _flushTcs.TrySetResult(null);
        }
    }
}
