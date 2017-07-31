using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace SampleServer
{
    public class IISHttpServer : IISContextFactory, IServer
    {
        private static NativeMethods.request_handler _requestHandler = HandleRequest;

        private IISContextFactory _iisContextFactory;

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        {
            var httpServerHandle = (IntPtr)GCHandle.Alloc(this);

            _iisContextFactory = new IISContextFactory<TContext>(application);

            // Start the server by registering the callback
            NativeMethods.register_request_callback(_requestHandler, httpServerHandle);

            return Task.CompletedTask;
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

        // Async void is evil I know but it gets the job done
        // We may want to allow for synchronous completions here
        private static async void HandleRequest(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext, IntPtr pvRequestContext)
        {
            // Unwrap the server so we can create an http context and process the request
            var server = (IISHttpServer)GCHandle.FromIntPtr(pvRequestContext).Target;

            var context = server.CreateHttpContext(pHttpContext, pfnCompletionCallback, pvCompletionContext);

            await context.ProcessRequestAsync();
        }

        public override IISHttpContext CreateHttpContext(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
        {
            return _iisContextFactory.CreateHttpContext(pHttpContext, pfnCompletionCallback, pvCompletionContext);
        }

        private class IISContextFactory<T> : IISContextFactory
        {
            private readonly IHttpApplication<T> _application;

            public IISContextFactory(IHttpApplication<T> application)
            {
                _application = application;
            }

            public override IISHttpContext CreateHttpContext(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
            {
                return new IISHttpContextOfT<T>(_application, pHttpContext, pfnCompletionCallback, pvCompletionContext);
            }
        }
    }

    // Over engineering to avoid allocations...
    public abstract class IISContextFactory
    {
        public abstract IISHttpContext CreateHttpContext(IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext);
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
