using System;
using System.Runtime.InteropServices;

namespace SampleServer
{
    public static class NativeMethods
    {
        public const int S_OK = 0;
        private const string AspNetCoreModuleDll = "aspnetcore.dll";

        public enum REQUEST_NOTIFICATION_STATUS
        {
            RQ_NOTIFICATION_CONTINUE,
            RQ_NOTIFICATION_PENDING,
            RQ_NOTIFICATION_FINISH_REQUEST
        }

        public delegate REQUEST_NOTIFICATION_STATUS PFN_REQUEST_HANDLER(IntPtr pHttpContext, IntPtr pvRequestContext);
        public delegate REQUEST_NOTIFICATION_STATUS PFN_ASYNC_COMPLETION(IntPtr pHttpContext, IntPtr completionInfo, IntPtr pvCompletionContext);

        [DllImport(AspNetCoreModuleDll)]
        public static extern int http_post_completion(IntPtr pHttpContext, int cbBytes);

        //[DllImport(AspNetCoreModuleDll)]
        //public static extern int http_post_completion(IntPtr pHttpContext, int cbBytes);

        [DllImport(AspNetCoreModuleDll)]
        public static extern void http_indicate_completion(IntPtr pHttpContext, REQUEST_NOTIFICATION_STATUS notificationStatus);

        [DllImport(AspNetCoreModuleDll)]
        public static extern void register_request_callback(PFN_REQUEST_HANDLER callback, IntPtr pvRequestContext);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern int http_write_response_bytes(IntPtr pHttpContext, HttpApi.HTTP_DATA_CHUNK* pDataChunks, int nChunks, PFN_ASYNC_COMPLETION pfnCompletionCallback, IntPtr pvCompletionContext, out bool fCompletionExpected);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern int http_flush_response_bytes(IntPtr pHttpContext, PFN_ASYNC_COMPLETION pfnCompletionCallback, IntPtr pvCompletionContext, out bool fCompletionExpected);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern HttpApi.HTTP_REQUEST_V2* http_get_raw_request(IntPtr pHttpContext);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern HttpApi.HTTP_RESPONSE_V2* http_get_raw_response(IntPtr pHttpContext);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern void http_set_response_status_code(IntPtr pHttpContext, ushort statusCode, byte* pszReason);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern int http_read_request_bytes(IntPtr pHttpContext, byte* pvBuffer, int cbBuffer, PFN_ASYNC_COMPLETION pfnCompletionCallback, IntPtr pvCompletionContext, out int dwBytesReceived, out bool fCompletionExpected);

        [DllImport(AspNetCoreModuleDll)]
        public unsafe static extern bool http_get_completion_info(IntPtr pCompletionInfo, out int cbBytes, out int hr);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static bool is_ancm_loaded()
        {
            return GetModuleHandle(AspNetCoreModuleDll) != IntPtr.Zero;
        }
    }
}
