using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WebApplication26
{
    public class IISHttpResponseBody : Stream
    {
        private readonly IntPtr _pHttpContext;
        private TaskCompletionSource<object> _currentOperation;
        private IntPtr _thisPtr;
        private static NativeMethods.completion_callback _writeCallback = WriteCb;
        private object _lockObj = new object();

        public IISHttpResponseBody(IntPtr pHttpContext)
        {
            _pHttpContext = pHttpContext;
            _thisPtr = (IntPtr)GCHandle.Alloc(this);
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
            // Don't allow overlapping writes
            lock (_lockObj)
            {
                // The data is being copied synchronously in the method so it doesn't
                // need to be pinned for the length of the async operation
                fixed (byte* pBuffer = buffer)
                {
                    bool async = NativeMethods.http_write_response_bytes(
                        _pHttpContext,
                        pBuffer + offset,
                        count,
                        _writeCallback,
                        _thisPtr);

                    if (async)
                    {
                        // Only allocate if we're going async, this is inside of the lock
                        // so the callback waits until it's assigned to 
                        _currentOperation = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);
                        return _currentOperation.Task;
                    }
                }

                return Task.CompletedTask;
            }
        }

        private static NativeMethods.REQUEST_NOTIFICATION_STATUS WriteCb(IntPtr pHttpContext, IntPtr pCompletionInfo, IntPtr state)
        {
            var handle = GCHandle.FromIntPtr(state);
            var stream = (IISHttpResponseBody)handle.Target;

            // Lock so that we guarantee WriteAsync completed before executing the callback
            lock (stream._lockObj)
            {
                NativeMethods.http_get_completion_info(pCompletionInfo, out var count, out var status);

                // This always dispatches so we're fine doing it inside of the lock
                if (status != NativeMethods.S_OK)
                {
                    stream._currentOperation.TrySetException(Marshal.GetExceptionForHR(status));
                }
                else
                {
                    stream._currentOperation.TrySetResult(null);
                }

                return NativeMethods.REQUEST_NOTIFICATION_STATUS.RQ_NOTIFICATION_CONTINUE;
            }
        }
    }
}
