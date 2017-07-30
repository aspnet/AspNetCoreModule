using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace WebApplication26
{
    public class IISHttpServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        {
            // Start the server by registering the callback (async void is evil but it gets the job done)
            NativeMethods.register_request_callback(async (pHttpContext, cb, state) =>
            {
                var features = new IISHttpContext(pHttpContext);

                // Create the hosting context
                var context = application.CreateContext(features);

                try
                {
                    await application.ProcessRequestAsync(context);
                    application.DisposeContext(context, exception: null);
                    cb(0, pHttpContext, state);
                }
                catch (Exception ex)
                {
                    application.DisposeContext(context, ex);
                    cb(ex.HResult, pHttpContext, state);
                }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: Drain pending requests

            // Stop all further calls back into managed code by unhooking the callback
            // unregister_request_callback();

            return Task.CompletedTask;
        }

        public void Dispose()
        {

        }
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
