using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SampleServer
{
    public class IISHttpRequestBody : Stream
    {
        private readonly IntPtr _pHttpContext;
        private TaskCompletionSource<int> _currentOperation;
        private IntPtr _thisPtr;
        private GCHandle _pinnedBuffer;
        private static NativeMethods.PFN_ASYNC_COMPLETION _readCallback = ReadCallback;
        private object _lockObj = new object();

        public IISHttpRequestBody(IntPtr pHttpContext)
        {
            _pHttpContext = pHttpContext;
            _thisPtr = (IntPtr)GCHandle.Alloc(this);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override unsafe Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            lock (_lockObj)
            {
                _pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                bool async = NativeMethods.http_read_request_bytes(
                    _pHttpContext,
                    (byte*)_pinnedBuffer.AddrOfPinnedObject() + offset,
                    count,
                    _readCallback,
                    _thisPtr,
                    out int bytesRead);

                if (async)
                {
                    // Only allocate if we're going async, this is inside of the lock
                    // so the callback waits until it's assigned to 
                    _currentOperation = new TaskCompletionSource<int>(TaskContinuationOptions.RunContinuationsAsynchronously);
                    return _currentOperation.Task;
                }
                else
                {
                    _pinnedBuffer.Free();
                }

                return Task.FromResult(bytesRead);
            }
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
            throw new NotSupportedException();
        }

        private static NativeMethods.REQUEST_NOTIFICATION_STATUS ReadCallback(IntPtr pHttpContext, IntPtr pCompletionInfo, IntPtr state)
        {
            var handle = GCHandle.FromIntPtr(state);
            var stream = (IISHttpRequestBody)handle.Target;

#pragma warning disable CS1690 // Accessing a member on a field of a marshal-by-reference class may cause a runtime exception
            stream._pinnedBuffer.Free();
#pragma warning restore CS1690 // Accessing a member on a field of a marshal-by-reference class may cause a runtime exception

            // Lock so that we guarantee ReadAsync completed before executing the callback
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
                    stream._currentOperation.TrySetResult(count);
                }

                return NativeMethods.REQUEST_NOTIFICATION_STATUS.RQ_NOTIFICATION_CONTINUE;
            }
        }
    }
}
