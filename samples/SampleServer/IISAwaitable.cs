using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SampleServer
{
    public class IISAwaitable : ICriticalNotifyCompletion
    {
        private readonly static Action _callbackCompleted = () => { };

        private Action _callback;

        private Exception _exception;

        private int _cbBytes;

        public static readonly NativeMethods.PFN_ASYNC_COMPLETION Callback = (IntPtr pHttpContext, IntPtr pCompletionInfo, IntPtr pvCompletionContext) =>
        {
            var awaitable = (IISAwaitable)GCHandle.FromIntPtr(pvCompletionContext).Target;

            NativeMethods.http_get_completion_info(pCompletionInfo, out int cbBytes, out int hr);

            awaitable._exception = Marshal.GetExceptionForHR(hr);
            awaitable._cbBytes = cbBytes;

            var continuation = Interlocked.Exchange(ref awaitable._callback, _callbackCompleted);

            continuation?.Invoke();

            return NativeMethods.REQUEST_NOTIFICATION_STATUS.RQ_NOTIFICATION_CONTINUE;
        };

        public IISAwaitable GetAwaiter() => this;
        public bool IsCompleted => _callback == _callbackCompleted;

        public void GetResult()
        {
            var exception = _exception;
            var cbBytes = _cbBytes;

            // Reset the awaitable state
            _exception = null;
            _cbBytes = 0;
            _callback = null;

            // return new UvWriteResult(status, exception);
        }

        public void OnCompleted(Action continuation)
        {
            // There should never be a race between IsCompleted and OnCompleted since both operations
            // should always be on the libuv thread

            if (_callback == _callbackCompleted ||
                Interlocked.CompareExchange(ref _callback, continuation, null) == _callbackCompleted)
            {
                Debug.Fail($"{typeof(IISAwaitable)}.{nameof(OnCompleted)} raced with {nameof(IsCompleted)}, running callback inline.");

                // Just run it inline
                continuation();
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }
}
