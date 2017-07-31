using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace SampleServer
{
    public class IISHttpContextOfT<T> : IISHttpContext
    {
        private readonly IHttpApplication<T> _application;
        private readonly NativeMethods.request_handler_cb _pfnCompletionCallback;
        private readonly IntPtr _pvCompletionContext;

        public IISHttpContextOfT(PipeFactory pipeFactory, IHttpApplication<T> application, IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
            : base(pipeFactory, pHttpContext)
        {
            _application = application;
            _pfnCompletionCallback = pfnCompletionCallback;
            _pvCompletionContext = pvCompletionContext;
        }

        public override async Task ProcessRequestAsync()
        {
            var context = _application.CreateContext(this);

            try
            {
                _ = StartWritingResponseBody();
                await _application.ProcessRequestAsync(context);
            }
            catch (Exception ex)
            {
                ReportApplicationError(ex);
            }
            finally
            {
                if (!HasResponseStarted && _applicationException == null && _onStarting != null)
                {
                    await FireOnStarting();
                }

                if (_onCompleted != null)
                {
                    await FireOnCompleted();
                }
            }

            try
            {
                _application.DisposeContext(context, _applicationException);
            }
            catch (Exception ex)
            {
                // Log this
                _applicationException = _applicationException ?? ex;
            }
            finally
            {
                _pfnCompletionCallback(_applicationException?.HResult ?? 0, _pHttpContext, _pvCompletionContext);


                Input.Reader.Complete();
                Output.Writer.Complete();
            }
        }
    }
}
