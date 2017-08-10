﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SampleServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .ConfigureLogging(l =>
                {
                    l.SetMinimumLevel(LogLevel.Critical);
                })
                .UseEnvironment("Development")
                .UseContentRoot("C:\\Users\\jukotali\\code\\aspnetcoremodule\\samples\\SampleServer")
                .UseNativeIIS()
                .UseStartup<Startup>()
                .Build();
    }
}
