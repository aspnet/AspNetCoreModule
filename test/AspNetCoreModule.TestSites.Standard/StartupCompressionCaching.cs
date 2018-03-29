// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Linq;

namespace AspnetCoreModule.TestSites.Standard
{
    public class StartupCompressionCaching
    {
        public static bool CompressionMode = true;

        public void ConfigureServices(IServiceCollection services)
        {
            if (CompressionMode)
            {
                services.AddResponseCompression(options =>
                {
                    // adding video/mp4 is not a good idea because MP4 file is already compressed. This is only for testing purpose
                    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "video/mp4" });
                });

            }
            services.AddResponseCaching();
        } 

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            if (CompressionMode)
            {
                app.UseResponseCompression();
            }
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
                        context.Context.Response.Headers.Append("MyCustomHeader", DateTime.Now.Second.ToString());
                        var accept = context.Context.Request.Headers[HeaderNames.AcceptEncoding];
                        if (!StringValues.IsNullOrEmpty(accept))
                        {
                            context.Context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
                        }

                        // return video/mp4 content type if request ends with ".mp4"
                        if (context.Context.Request.Path.Value.TrimEnd().EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Context.Response.ContentType = "video/mp4";
                        }
                        else
                        {
                            context.Context.Response.ContentType = "text/plain";
                        }
                    }
                }
            );
            app.UseStaticFiles();
        }
    }
}
