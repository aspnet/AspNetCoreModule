using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WebApplication26
{
    public static class NativeMethods
    {
        public const int S_OK = 0;

        public enum REQUEST_NOTIFICATION_STATUS
        {
            RQ_NOTIFICATION_CONTINUE,
            RQ_NOTIFICATION_PENDING,
            RQ_NOTIFICATION_FINISH_REQUEST
        }

        public delegate void request_handler_cb(int error, IntPtr pHttpContext, IntPtr state);
        public delegate void request_handler(IntPtr pHttpContext, request_handler_cb callback, IntPtr state);
        public delegate REQUEST_NOTIFICATION_STATUS completion_callback(IntPtr pHttpContext, IntPtr completionInfo, IntPtr pvCompletionContext);

        //[DllImport("aspnetcore.dll")]
        //public static extern void register_shutdown_callback(Action callback);

        [DllImport("aspnetcore.dll")]
        public static extern void register_request_callback(request_handler callback);

        [DllImport("aspnetcore.dll")]
        public unsafe static extern bool http_write_response_bytes(IntPtr pHttpContext, byte* buffer, int count, completion_callback callback, IntPtr state);

        [DllImport("aspnetcore.dll")]
        public unsafe static extern bool http_flush_response_bytes(IntPtr pHttpContext, completion_callback callback, IntPtr state);

        [DllImport("aspnetcore.dll")]
        public unsafe static extern bool http_read_request_bytes(IntPtr pHttpContext, byte* buffer, int count, completion_callback callback, IntPtr state, out int bytesRead);

        [DllImport("aspnetcore.dll")]
        public unsafe static extern bool http_get_completion_info(IntPtr pCompletionInfo, out int bytes, out int status);

        //[DllImport("aspnetcore.dll")]
        //public static extern void unregister_request_callback();

    }
}
