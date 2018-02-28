using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography.X509Certificates;


namespace AspnetCoreModule.TestSites.Standard
{
    public class Program
    {
        public static IApplicationLifetime AppLifetime;
        public static int GracefulShutdownDelayTime = 0;
        private static X509Certificate2 _x509Certificate2;
        public static bool InprocessMode = false;

        public static void Main(string[] args)
        {
            Console.WriteLine("BEGIN Main()");
            // initialize variables
            int SleeptimeWhileStarting = 0;
            int SleeptimeWhileClosing = 0;

            string Token = Environment.GetEnvironmentVariable("ASPNETCORE_TOKEN");
            if (!string.IsNullOrEmpty(Token))
            {
                InprocessMode = false;
            }
            else
            {
                InprocessMode = true;
            }
           
            string startupDelay = Environment.GetEnvironmentVariable("ANCMTestStartUpDelay");
            if (!string.IsNullOrEmpty(startupDelay))
            {
                SleeptimeWhileStarting = Convert.ToInt32(startupDelay);
            }

            string shutdownDelay = Environment.GetEnvironmentVariable("ANCMTestShutdownDelay");
            if (!string.IsNullOrEmpty(shutdownDelay))
            {
                SleeptimeWhileClosing = Convert.ToInt32(shutdownDelay);
            }

            Console.WriteLine("SleeptimeWhileStarting: " + SleeptimeWhileStarting);
            Console.WriteLine("SleeptimeWhileClosing: " + SleeptimeWhileClosing);


            // Sleep before starting
            if (SleeptimeWhileStarting != 0)
            {
                Startup.SleeptimeWhileStarting = SleeptimeWhileStarting;
                Thread.Sleep(SleeptimeWhileStarting);
            }
            
            // Build WebHost
            IWebHost host = null;
            IWebHostBuilder builder = null;
            string startUpClassString = Environment.GetEnvironmentVariable("ANCMTestStartupClassName");
            if (!string.IsNullOrEmpty(startUpClassString))
            {
                Console.WriteLine("ANCMTestStartupClassName: " + startUpClassString);
                IConfiguration config = new ConfigurationBuilder()
                    .AddCommandLine(args)
                    .Build();

                if (startUpClassString == "StartupHTTPS")
                {
                    // load .\testresources\testcert.pfx
                    string pfxPassword = "testPassword";
                    if (File.Exists(@".\TestResources\testcert.pfx"))
                    {
                        _x509Certificate2 = new X509Certificate2(@".\TestResources\testcert.pfx", pfxPassword);
                    }
                    else
                    {
                        throw new Exception(@"Certificate file not found: .\TestResources\testcert.pfx of which password should " + pfxPassword);
                    }
                }
                else if (startUpClassString == "StartupCompressionCaching" || startUpClassString == "StartupNoCompressionCaching")
                {
                    if (startUpClassString == "StartupNoCompressionCaching")
                    {
                        StartupCompressionCaching.CompressionMode = false;
                    }
                    host = WebHost.CreateDefaultBuilder(args)
                        .UseConfiguration(config)
                        .UseContentRoot(Directory.GetCurrentDirectory())
                        .UseStartup<StartupCompressionCaching>()
                        .Build();
                }
                else if (startUpClassString == "StartupHelloWorld")
                {
                    host = WebHost.CreateDefaultBuilder(args)
                        .UseConfiguration(config)
                        .UseStartup<StartupHelloWorld>()
                        .Build();
                }
                else if (startUpClassString == "StartupNtlmAuthentication")
                {
                    host = WebHost.CreateDefaultBuilder(args)
                        .UseConfiguration(config)
                        .UseStartup<StartupNtlmAuthentication>()
                        .Build();
                }
                else if (startUpClassString == "StartupWithShutdownDisabled")
                {
                    builder = new WebHostBuilder()
                        .UseKestrel()
                        .ConfigureServices(services =>
                        {
                            const string PairingToken = "TOKEN";

                            string paringToken = null;
                            if (InprocessMode)
                            {
                                Console.WriteLine("Don't use IISMiddleware for inprocess mode");
                                paringToken = null;
                            }
                            else
                            {
                                Console.WriteLine("Use IISMiddleware for outofprocess mode");
                                paringToken = builder.GetSetting(PairingToken) ?? Environment.GetEnvironmentVariable($"ASPNETCORE_{PairingToken}");
                            }
                            services.AddSingleton<IStartupFilter>(
                                new IISSetupFilter(paringToken)
                            );
                        })
                        .UseConfiguration(config)
                        .UseStartup<Startup>();

                    host = builder.Build();
                }
                else
                {
                    throw new Exception("Invalid startup class name : " + startUpClassString);
                }
            }

            if (host == null)
            {
                host = WebHost.CreateDefaultBuilder(args)
                    .UseStartup<Startup>()
                    .Build();
            }

            // Sleep before stopping
            if (SleeptimeWhileClosing != 0)
            {
                Startup.SleeptimeWhileClosing = SleeptimeWhileClosing;
            }

            string gracefulShutdownDelay = Environment.GetEnvironmentVariable("ANCMTestGracefulShutdownDelayTime");
            if (!string.IsNullOrEmpty(gracefulShutdownDelay))
            {
                GracefulShutdownDelayTime = Convert.ToInt32(gracefulShutdownDelay);
            }

            // Initialize AppLifeTime events handler
            AppLifetime = (IApplicationLifetime)host.Services.GetService(typeof(IApplicationLifetime));
            AppLifetime.ApplicationStarted.Register(
                () =>
                {
                    Thread.Sleep(1000);
                    Console.WriteLine("AppLifetime.ApplicationStarted.Register()");
                }
            );
            AppLifetime.ApplicationStopping.Register(
                () =>
                {
                    Console.WriteLine("Begin: WebSocketConnections");
                    WebSocketConnections.CloseAll();
                    Console.WriteLine("End: WebSocketConnections");

                    Console.WriteLine("Begin: AppLifetime.ApplicationStopping.Register(), sleeping " + Startup.SleeptimeWhileClosing / 2);
                    Thread.Sleep(Startup.SleeptimeWhileClosing / 2);
                    Startup.SleeptimeWhileClosing = Startup.SleeptimeWhileClosing / 2;
                    Console.WriteLine("End: AppLifetime.ApplicationStopping.Register()");
                }
            );
            AppLifetime.ApplicationStopped.Register(
                () =>
                {
                    Console.WriteLine("Begin: AppLifetime.ApplicationStopped.Register(), sleeping " + Startup.SleeptimeWhileClosing);
                    Thread.Sleep(Startup.SleeptimeWhileClosing);
                    Startup.SleeptimeWhileClosing = 0;
                    Console.WriteLine("End: AppLifetime.ApplicationStopped.Register()");
                }
            );

            // run
            try
            {
                Console.WriteLine("BEGIN Main::Run()");
                host.Run();
                Console.WriteLine("END Main::Run()");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception error!!! " + ex.Message);
            }

            // Sleep before finishing
            if (Startup.SleeptimeWhileClosing >  0)
            {
                Console.WriteLine("Begin: SleeptimeWhileClosing " + Startup.SleeptimeWhileClosing);
                Thread.Sleep(Startup.SleeptimeWhileClosing);
                Console.WriteLine("End: SleeptimeWhileClosing");
            }
            Console.WriteLine("END Main()");
        }
    }
}
