using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using System.Threading;

namespace SampleServer
{
    public class IISHttpContextOfT<TContext> : IISHttpContext
    {
        private readonly IHttpApplication<TContext> _application;

        public IISHttpContextOfT(PipeFactory pipeFactory, IHttpApplication<TContext> application, IntPtr pHttpContext)
            : base(pipeFactory, pHttpContext)
        {
            _application = application;
        }

        public override async Task ProcessRequestAsync()
        {
            var context = default(TContext);

            try
            {
                context = _application.CreateContext(this);

                await _application.ProcessRequestAsync(context);
                //if (Volatile.Read(ref _requestAborted) == 0)
                //{
                //    VerifyResponseContentLength();
                //}
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
                    // Dispose
                }

                if (_onCompleted != null)
                {
                    await FireOnCompleted();
                }
            }

            if (Volatile.Read(ref _requestAborted) == 0)
            {
                if (HasResponseStarted)
                {
                    // If the response has already started, call ProduceEnd() before
                    // consuming the rest of the request body to prevent
                    // delaying clients waiting for the chunk terminator:
                    //
                    // https://github.com/dotnet/corefx/issues/17330#issuecomment-288248663
                    //
                    // ProduceEnd() must be called before _application.DisposeContext(), to ensure
                    // HttpContext.Response.StatusCode is correctly set when
                    // IHttpContextFactory.Dispose(HttpContext) is called.
                    await ProduceEnd();
                }

                //// ForZeroContentLength does not complete the reader nor the writer
                //if (!messageBody.IsEmpty && _keepAlive)
                //{
                //    // Finish reading the request body in case the app did not.
                //    TimeoutControl.SetTimeout(Constants.RequestBodyDrainTimeout.Ticks, TimeoutAction.SendTimeoutResponse);
                //    await messageBody.ConsumeAsync();
                //    TimeoutControl.CancelTimeout();
                //}

                if (!HasResponseStarted)
                {
                    await ProduceEnd();
                }
            }
            else if (!HasResponseStarted)
            {
                // If the request was aborted and no response was sent, there's no
                // meaningful status code to log.
                StatusCode = 0;
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
                // The app is finished and there should be nobody writing to the response pipe
                Output.Dispose();

                if (_writingTask != null)
                {
                    await _writingTask;
                }

                // The app is finished and there should be nobody reading from the request pipe
                Input.Reader.Complete();

                if (_readingTask != null)
                {
                    await _readingTask;
                }
            }
        }
    }
}
