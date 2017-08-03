using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SampleServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            // Console.ReadLine();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Run(async (context) =>
            {

                if (HttpMethods.IsPost(context.Request.Method))
                {
                    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                    Console.WriteLine($"Request body: {body}");
                }

                context.Response.Headers["X-Foo"] = "This is a test";
                context.Response.Headers["Server"] = "Justin's server";
                await context.Response.WriteAsync("Hello World!");
            });
        }
    }
}
