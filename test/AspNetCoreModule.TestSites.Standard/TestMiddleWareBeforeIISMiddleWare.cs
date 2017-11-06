// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using System;
using System.Security.Principal;
using System.Threading.Tasks;


namespace AspnetCoreModule.TestSites.Standard

{
    public class TestMiddleWareBeforeIISMiddleWare
    {
        private readonly RequestDelegate next;

        public TestMiddleWareBeforeIISMiddleWare(RequestDelegate next)
        {
            this.next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            // if the given request is shutdown message from ANCM and the value of GracefulShutdown environment variable is set,
            // the shutdown message is handled by this middleware instead of IISMiddleware.

            if (HttpMethods.IsPost(context.Request.Method) &&
                string.Equals("shutdown", context.Request.Headers["MS-ASPNETCORE-EVENT"], StringComparison.OrdinalIgnoreCase))
            {
                string shutdownMode = Environment.GetEnvironmentVariable("GracefulShutdown");
                if (!string.IsNullOrWhiteSpace(shutdownMode) && shutdownMode.ToLower().StartsWith("disabled"))
                {
                    //ignore shutdown Message returning 200 instead of 202 because the gracefulshutdown is disabled
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsync("Called ShutdownMessage with disabled of GracefulShutdown");
                    return;
                }
                // The below code might be required for old version of AspnetCore in case IISMiddleware does not handle the graceful shutdown message.
                // In that case, the below code should be added back later.
                //else
                //{
                //    // invoke StopApplication to complete gracefulshutdown
                //    Program.AappLifetime.StopApplication();
                //    // return 202 and start gracefulshutdown
                //    context.Response.StatusCode = StatusCodes.Status202Accepted;
                //    await context.Response.WriteAsync("Called ShutdownMessage with enabled of GracefulShutdown");
                //    return;
                //}
            }
            await next.Invoke(context);
        }
    }
}
