using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace SampleServer
{
    public class IISHttpServer : IServer
    {
        private static NativeMethods.request_handler _requestHandler = HandleRequest;

        private IISContextFactory _iisContextFactory;

        private PipeFactory _pipeFactory = new PipeFactory();

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        {
            var httpServerHandle = (IntPtr)GCHandle.Alloc(this);

            _iisContextFactory = new IISContextFactory<TContext>(_pipeFactory, application);

            // Start the server by registering the callback
            NativeMethods.register_request_callback(_requestHandler, httpServerHandle);

            return Task.CompletedTask;
        }

        private async Task ProcessRequest(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
        {
            var context = _iisContextFactory.CreateHttpContext(pHttpContext, pfnCompletionCallback, pvCompletionContext);

            await context.ProcessRequestAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: Drain pending requests

            // Stop all further calls back into managed code by unhooking the callback

            return Task.CompletedTask;
        }

        public void Dispose()
        {

        }

        private static void HandleRequest(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext, IntPtr pvRequestContext)
        {
            // Unwrap the server so we can create an http context and process the request
            var server = (IISHttpServer)GCHandle.FromIntPtr(pvRequestContext).Target;

            // Ignore the task here this should never fail
            _ = server.ProcessRequest(pHttpContext, pfnCompletionCallback, pvCompletionContext);
        }

        private class IISContextFactory<T> : IISContextFactory
        {
            private readonly IHttpApplication<T> _application;
            private readonly PipeFactory _pipeFactory;

            public IISContextFactory(PipeFactory pipeFactory, IHttpApplication<T> application)
            {
                _application = application;
                _pipeFactory = pipeFactory;
            }

            public IISHttpContext CreateHttpContext(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
            {
                return new IISHttpContextOfT<T>(_pipeFactory, _application, pHttpContext, pfnCompletionCallback, pvCompletionContext);
            }
        }
    }

    // Over engineering to avoid allocations...
    public interface IISContextFactory
    {
        IISHttpContext CreateHttpContext(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext);
    }

    public static class WebHostBuilderExtensions
    {
        public static IWebHostBuilder UseNativeIIS(this IWebHostBuilder builder)
        {
            return builder.ConfigureServices(services =>
            {
                services.AddSingleton<IServer, IISHttpServer>();
            });
        }
    }
}
