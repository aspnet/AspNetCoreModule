using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SampleServer
{
    public partial class IISHttpContext
    {
        private readonly IntPtr _pHttpContext;
        private bool _upgradeAvailable;
        private bool _wasUpgraded;

        private readonly object _onStartingSync = new Object();
        private readonly object _onCompletedSync = new Object();

        protected Stack<KeyValuePair<Func<object, Task>, object>> _onStarting;
        protected Stack<KeyValuePair<Func<object, Task>, object>> _onCompleted;

        public IISHttpContext(IntPtr pHttpContext)
        {
            _pHttpContext = pHttpContext;

            unsafe
            {
                var pHttpRequest = NativeMethods.http_get_raw_request(pHttpContext);

                var verb = pHttpRequest->Request.Verb;
                if (verb > HttpApi.HTTP_VERB.HttpVerbUnknown && verb < HttpApi.HTTP_VERB.HttpVerbMaximum)
                {
                    Method = HttpApi.HttpVerbs[(int)verb];
                }

                var major = pHttpRequest->Request.Version.MajorVersion;
                var minor = pHttpRequest->Request.Version.MinorVersion;

                HttpVersion = "HTTP/" + minor + "." + major;
                Scheme = pHttpRequest->Request.pSslInfo == null ? "http" : "https";

                // TODO: Read this from IIS config
                PathBase = string.Empty;
                Path = Encoding.ASCII.GetString(pHttpRequest->Request.pRawUrl, pHttpRequest->Request.RawUrlLength);
                RawTarget = Path;
                QueryString = new string(pHttpRequest->Request.CookedUrl.pQueryString, 0, pHttpRequest->Request.CookedUrl.QueryStringLength / 2);
                // TODO: Avoid using long.ToString, it's pretty slow
                ConnectionId = pHttpRequest->Request.ConnectionId.ToString(CultureInfo.InvariantCulture);

                // Copied from WebListener
                // This is the base GUID used by HTTP.SYS for generating the activity ID.
                // HTTP.SYS overwrites the first 8 bytes of the base GUID with RequestId to generate ETW activity ID.
                var guid = new Guid(0xffcb4c93, 0xa57f, 0x453c, 0xb6, 0x3f, 0x84, 0x71, 0xc, 0x79, 0x67, 0xbb);
                *((ulong*)&guid) = pHttpRequest->Request.RequestId;

                // TODO: Also make this not slow
                TraceIdentifier = guid.ToString();

                var pHttpResponse = NativeMethods.http_get_raw_request(pHttpContext);
            }

            RequestBody = new IISHttpRequestBody(pHttpContext);
            ResponseBody = new IISHttpResponseBody(pHttpContext);

            // TODO: Only upgradable on Win8 or higher
            _upgradeAvailable = true;

            ResetFeatureCollection();
        }

        public string HttpVersion { get; set; }
        public string Scheme { get; set; }
        public string Method { get; set; }
        public string PathBase { get; set; }
        public string Path { get; set; }
        public string QueryString { get; set; }
        public string RawTarget { get; set; }
        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public CancellationToken RequestAborted { get; set; }
        public bool HasResponseStarted { get; set; }
        public IPAddress RemoteIpAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalIpAddress { get; set; }
        public int LocalPort { get; set; }
        public string ConnectionId { get; set; }
        public string TraceIdentifier { get; set; }

        public Stream RequestBody { get; set; }
        public Stream ResponseBody { get; set; }

        // TODO: Optimize header
        public IHeaderDictionary RequestHeaders { get; set; } = new HeaderDictionary();
        public IHeaderDictionary ResponseHeaders { get; set; } = new HeaderDictionary();

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Abort()
        {

        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            lock (_onStartingSync)
            {
                if (HasResponseStarted)
                {
                    throw new InvalidOperationException("Response already started");
                }

                if (_onStarting == null)
                {
                    _onStarting = new Stack<KeyValuePair<Func<object, Task>, object>>();
                }
                _onStarting.Push(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            lock (_onCompletedSync)
            {
                if (_onCompleted == null)
                {
                    _onCompleted = new Stack<KeyValuePair<Func<object, Task>, object>>();
                }
                _onCompleted.Push(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        public async Task FireOnStarting()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onStarting = null;
            lock (_onStartingSync)
            {
                onStarting = _onStarting;
                _onStarting = null;
            }
            if (onStarting != null)
            {
                try
                {
                    foreach (var entry in onStarting)
                    {
                        await entry.Key.Invoke(entry.Value);
                    }
                }
                catch (Exception ex)
                {
                    ReportApplicationError(ex);
                }
            }
        }

        public async Task FireOnCompleted()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onCompleted = null;
            lock (_onCompletedSync)
            {
                onCompleted = _onCompleted;
                _onCompleted = null;
            }
            if (onCompleted != null)
            {
                foreach (var entry in onCompleted)
                {
                    try
                    {
                        await entry.Key.Invoke(entry.Value);
                    }
                    catch (Exception ex)
                    {
                        ReportApplicationError(ex);
                    }
                }
            }
        }

        private void ReportApplicationError(Exception ex)
        {
        }
    }
}
