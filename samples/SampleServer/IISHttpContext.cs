using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.WebUtilities;

namespace SampleServer
{
    public abstract partial class IISHttpContext
    {
        private const int MinAllocBufferSize = 2048;

        protected readonly IntPtr _pHttpContext;
        private bool _upgradeAvailable;
        private bool _wasUpgraded;

        private readonly object _onStartingSync = new Object();
        private readonly object _onCompletedSync = new Object();

        protected Stack<KeyValuePair<Func<object, Task>, object>> _onStarting;
        protected Stack<KeyValuePair<Func<object, Task>, object>> _onCompleted;
        protected Exception _applicationException;
        private PipeFactory _pipeFactory;

        private readonly GCHandle _readOperationGcHandle;
        private readonly IISAwaitable _readOperation = new IISAwaitable();
        private BufferHandle _inputHandle;

        private readonly IISAwaitable _writeOperation = new IISAwaitable();
        private readonly GCHandle _writeOperationGcHandle;

        protected Task _readingTask;
        protected Task _writingTask;

        public IISHttpContext(PipeFactory pipeFactory, IntPtr pHttpContext)
        {
            _readOperationGcHandle = GCHandle.Alloc(_readOperation);
            _writeOperationGcHandle = GCHandle.Alloc(_writeOperation);

            _pipeFactory = pipeFactory;
            _pHttpContext = pHttpContext;

            unsafe
            {
                var pHttpRequest = NativeMethods.http_get_raw_request(pHttpContext);

                var verb = pHttpRequest->Request.Verb;
                if (verb > HttpApi.HTTP_VERB.HttpVerbUnknown && verb < HttpApi.HTTP_VERB.HttpVerbMaximum)
                {
                    Method = HttpApi.HttpVerbs[(int)verb];
                }
                else
                {
                    // TODO: Handle unknown verbs
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

                // TODO: Parse socket ADDR for local and remote end point
                // var localAddress = GetSocketAddress(pHttpRequest->Request.Address.pLocalAddress);
                // var remoteAddress = GetSocketAddress(pHttpRequest->Request.Address.pRemoteAddress);

                RequestHeaders = new RequestHeaders(pHttpRequest);
            }

            RequestBody = new IISHttpRequestBody(this);
            ResponseBody = new IISHttpResponseBody(this);

            Input = _pipeFactory.Create();
            Output = _pipeFactory.Create();

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
        public int StatusCode { get; set; } = 200;
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

        public IPipe Input { get; set; }
        public IPipe Output { get; set; }

        public IHeaderDictionary RequestHeaders { get; set; }
        public IHeaderDictionary ResponseHeaders { get; set; } = new HeaderDictionary();

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await InitializeResponse();

            // TODO: Call FlushAsync
        }

        private Task InitializeResponse()
        {
            if (!HasResponseStarted)
            {
                // First flush
                unsafe
                {
                    // TODO: Don't allocate a string
                    var reasonPhrase = ReasonPhrases.GetReasonPhrase(StatusCode);
                    var reasonPhraseBytes = Encoding.UTF8.GetBytes(reasonPhrase);

                    fixed (byte* pReasonPhrase = reasonPhraseBytes)
                    {
                        // This copies data into the underying buffer
                        NativeMethods.http_set_response_status_code(_pHttpContext, (ushort)StatusCode, pReasonPhrase);
                    }

                    HttpApi.HTTP_RESPONSE_V2* pHttpResponse = NativeMethods.http_get_raw_response(_pHttpContext);

                    // TODO: Copy headers here
                }

                HasResponseStarted = true;
            }

            return Task.CompletedTask;
        }

        public void Abort()
        {

        }

        public void StartReadingRequestBody()
        {
            if (_readingTask == null)
            {
                _readingTask = ProcessRequestBody();
            }
        }

        private async Task ProcessRequestBody()
        {
            try
            {
                while (true)
                {
                    // These buffers are pinned
                    var wb = Input.Writer.Alloc(MinAllocBufferSize);
                    var handle = wb.Buffer.Pin();

                    try
                    {
                        int read = await ReadAsync(wb.Buffer.Length);
                        wb.Advance(read);
                        var result = await wb.FlushAsync();

                        if (result.IsCompleted || result.IsCancelled)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
            }
            catch (Exception ex)
            {
                Input.Writer.Complete(ex);
            }
            finally
            {
                Input.Writer.Complete();
            }
        }

        public void StartWritingResponseBody()
        {
            if (_writingTask == null)
            {
                _writingTask = ProcessResponseBody();
            }
        }

        private async Task ProcessResponseBody()
        {
            while (true)
            {
                ReadResult result;

                try
                {
                    result = await Output.Reader.ReadAsync();
                }
                catch
                {
                    Output.Reader.Complete();
                    return;
                }

                var buffer = result.Buffer;
                var consumed = buffer.End;

                try
                {
                    if (result.IsCancelled)
                    {
                        break;
                    }

                    await InitializeResponse();

                    if (!buffer.IsEmpty)
                    {
                        await WriteAsync(buffer);
                    }
                    else if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    Output.Reader.Advance(consumed);
                }
            }

            Output.Reader.Complete();
        }

        private unsafe IISAwaitable WriteAsync(ReadableBuffer buffer)
        {
            var fCompletionExpected = false;
            var hr = 0;

            if (!buffer.IsSingleSpan)
            {
                // TODO: Handle mutiple buffers (in a single write)
                var copy = buffer.ToArray();
                fixed (byte* pBuffer = &copy[0])
                {
                    hr = NativeMethods.http_write_response_bytes(_pHttpContext, pBuffer, buffer.Length, IISAwaitable.Callback, (IntPtr)_writeOperationGcHandle, out fCompletionExpected);
                }
            }
            else
            {
                fixed (byte* pBuffer = &buffer.First.Span.DangerousGetPinnableReference())
                {
                    hr = NativeMethods.http_write_response_bytes(_pHttpContext, pBuffer, buffer.Length, IISAwaitable.Callback, (IntPtr)_writeOperationGcHandle, out fCompletionExpected);
                }
            }

            if (!fCompletionExpected || hr != NativeMethods.S_OK)
            {
                _writeOperation.Complete(hr, cbBytes: 0);
            }

            return _writeOperation;
        }

        private unsafe IISAwaitable ReadAsync(int length)
        {
            var hr = NativeMethods.http_read_request_bytes(
                                _pHttpContext,
                                (byte*)_inputHandle.PinnedPointer,
                                length,
                                IISAwaitable.Callback,
                                (IntPtr)_readOperationGcHandle,
                                out var dwReceivedBytes,
                                out bool fCompletionExpected);

            if (!fCompletionExpected || hr != NativeMethods.S_OK)
            {
                _readOperation.Complete(hr, cbBytes: dwReceivedBytes);
            }

            return _readOperation;
        }

        public abstract Task ProcessRequestAsync();

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

        protected async Task FireOnStarting()
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

        protected async Task FireOnCompleted()
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

        protected void ReportApplicationError(Exception ex)
        {
            if (_applicationException == null)
            {
                _applicationException = ex;
            }
            else if (_applicationException is AggregateException)
            {
                _applicationException = new AggregateException(_applicationException, ex).Flatten();
            }
            else
            {
                _applicationException = new AggregateException(_applicationException, ex);
            }

            // TODO: Log
        }
    }
}
