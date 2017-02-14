using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Net.Http.Headers;

namespace CompressionTest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new WebHostBuilder()
                .ConfigureLogging(options => options.AddDebug())
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseKestrel()
                .UseIISIntegration()
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression();
            services.AddResponseCaching();
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.UseResponseCompression();
            app.UseResponseCaching();
            app.UseDefaultFiles();
            app.UseStaticFiles(
                new StaticFileOptions()
                {
                    OnPrepareResponse = context =>
                    {
                        //
                        // FYI, below line can be simplified with 
                        //    context.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=10";
                        //
                        context.Context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
                        {
                            Public = true,
                            MaxAge = TimeSpan.FromSeconds(10)
                        };
                        context.Context.Response.Headers[HeaderNames.Vary] = new string[] { "Accept-Encoding" };
                        context.Context.Response.Headers.Append("MyCustomHeader", DateTime.Now.Second.ToString());                        
                    }
                }
            );
            app.Run(
                context =>
                {
                    context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromSeconds(10)
                    };
                    context.Response.Headers[HeaderNames.Vary] = new string[] { "Accept-Encoding" };
                    return context.Response.WriteAsync("Hello World!!! I am non-static file handler!!! " + DateTime.UtcNow);
                });
        }
    }
}
