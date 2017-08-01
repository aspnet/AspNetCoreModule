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
        private static NativeMethods.PFN_REQUEST_HANDLER _requestHandler = HandleRequest;

        public IISContextFactory _iisContextFactory { get; private set; }

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

        private async Task ProcessRequest(IISHttpContext context)
        {
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
            // TODO
        }

        private static NativeMethods.REQUEST_NOTIFICATION_STATUS HandleRequest(IntPtr pHttpContext, IntPtr pvRequestContext)
        {
            // Unwrap the server so we can create an http context and process the request
            var server = (IISHttpServer)GCHandle.FromIntPtr(pvRequestContext).Target;

            var context = server._iisContextFactory.CreateHttpContext(pHttpContext);
            // Ignore the task here this should never fail
            var _ = server.ProcessRequest(context);

            return NativeMethods.REQUEST_NOTIFICATION_STATUS.RQ_NOTIFICATION_PENDING;
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

            public IISHttpContext CreateHttpContext(IntPtr pHttpContext)
            {
                return new IISHttpContextOfT<T>(_pipeFactory, _application, pHttpContext);
            }
        }
    }

    // Over engineering to avoid allocations...
    public interface IISContextFactory
    {
        IISHttpContext CreateHttpContext(IntPtr pHttpContext);
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
