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

            string tempstring = Environment.GetEnvironmentVariable("ASPNETCORE_TOKEN");
            if (!string.IsNullOrEmpty(tempstring))
            {
                InprocessMode = false;
                tempstring = null;
            }
            else
            {
                InprocessMode = true;
            }
           
            tempstring = Environment.GetEnvironmentVariable("ANCMTestStartUpDelay");
            if (!string.IsNullOrEmpty(tempstring))
            {
                SleeptimeWhileStarting = Convert.ToInt32(tempstring);
                Startup.SleeptimeWhileStarting = SleeptimeWhileStarting;
                Console.WriteLine("SleeptimeWhileStarting: " + Startup.SleeptimeWhileStarting);
                tempstring = null;
            }

            tempstring = Environment.GetEnvironmentVariable("ANCMTestShutdownDelay");
            if (!string.IsNullOrEmpty(tempstring))
            {
                SleeptimeWhileClosing = Convert.ToInt32(tempstring);
                Startup.SleeptimeWhileClosing = SleeptimeWhileClosing;
                Console.WriteLine("SleeptimeWhileClosing: " + Startup.SleeptimeWhileClosing);
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
                        Console.WriteLine("Certificate file found");
                        _x509Certificate2 = new X509Certificate2(@".\TestResources\testcert.pfx", pfxPassword);
                    }
                    else
                    {
                        Console.WriteLine("Error!!! Certificate file not found");
                        //throw new Exception("Error!!! Certificate file not found");
                    }
                }
                else if (startUpClassString == "StartupCompressionCaching" || startUpClassString == "StartupNoCompressionCaching")
                {
                    if (startUpClassString == "StartupNoCompressionCaching")
                    {
                        StartupCompressionCaching.CompressionMode = false;
                    }
                    host = CreateDefaultBuilder(args)
                        .UseConfiguration(config)
                        // BUGBUG below line is commented out because it causes 404 error with inprocess mode
                        //.UseContentRoot(Directory.GetCurrentDirectory())
                        .UseStartup<StartupCompressionCaching>()
                        .Build();
                }
                else if (startUpClassString == "StartupHelloWorld")
                {
                    host = CreateDefaultBuilder(args)
                        .UseConfiguration(config)
                        .UseStartup<StartupHelloWorld>()
                        .Build();
                }
                else if (startUpClassString == "StartupNtlmAuthentication")
                {
                    host = CreateDefaultBuilder(args)
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
                host = CreateDefaultBuilder(args)
                    .UseStartup<Startup>()
                    .Build();
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
                    tempstring = Environment.GetEnvironmentVariable("ANCMTestDisableWebCocketConnectionsCloseAll");
                    if (string.IsNullOrEmpty(tempstring))
                    {
                        Console.WriteLine("Begin: WebSocketConnections");
                        WebSocketConnections.CloseAll();
                        Console.WriteLine("End: WebSocketConnections");
                    }

                    Console.WriteLine("Begin: AppLifetime.ApplicationStopping.Register(), sleeping " + Startup.SleeptimeWhileClosing / 2);
                    Thread.Sleep(Startup.SleeptimeWhileClosing);
                    Startup.SleeptimeWhileClosing = 0;
                    Console.WriteLine("End: AppLifetime.ApplicationStopping.Register()");
                }
            );
            AppLifetime.ApplicationStopped.Register(
                () =>
                {
                    Console.WriteLine("AppLifetime.ApplicationStopped.Register()");
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

        private static IWebHostBuilder CreateDefaultBuilder(string[] args)
        {
            // Note, as of time of this code being authored, CreateDefaultBuilder calls UseIISIntegration but not UseIIS
            return WebHost.CreateDefaultBuilder(args).UseIIS();
        }
    }
}
