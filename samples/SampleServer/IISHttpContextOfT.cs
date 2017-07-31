using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace SampleServer
{
    public class IISHttpContextOfT<T> : IISHttpContext
    {
        private readonly IHttpApplication<T> _application;

        public IISHttpContextOfT(PipeFactory pipeFactory, IHttpApplication<T> application, IntPtr pHttpContext)
            : base(pipeFactory, pHttpContext)
        {
            _application = application;
        }

        public override async Task ProcessRequestAsync()
        {
            var context = _application.CreateContext(this);

            try
            {
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
                // The app is finished and there should be nobody writing to the response pipe
                Output.Writer.Complete();

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

                PostCompletion();
            }
        }
    }
}
