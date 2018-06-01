﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AspNetCoreModule.Test.Framework
{
    public class TestWebSite : IDisposable
    {
        static private bool _publishedAspnetCoreApp = false;

        public TestWebApplication RootAppContext;
        public TestWebApplication AspNetCoreApp;
        public TestWebApplication WebSocketApp;
        public TestWebApplication URLRewriteApp;
        public TestUtility testHelper;
        private ILogger _logger;
        public int IisExpressPidBackup = -1;

        private string postfix = string.Empty;

        public void Dispose()
        {
            TestUtility.LogInformation("TestWebSite::Dispose() Start");

            if (IisExpressPidBackup != -1)
            {
                try
                {
                    var iisExpressProcess = Process.GetProcessById(Convert.ToInt32(IisExpressPidBackup));
                    iisExpressProcess.Kill();
                    iisExpressProcess.WaitForExit();
                    iisExpressProcess.Close();
                }
                catch
                {
                    if (this.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                    {
                        // IISExpress seems to be already recycled under Inprocess mode.
                    }
                    else
                    {
                        TestUtility.RunPowershellScript("stop-process -id " + IisExpressPidBackup);
                    }
                }
            }
            TestUtility.LogInformation("TestWebSite::Dispose() End");
        }

        public string _hostName = null;
        public string HostName
        {
            get
            {
                if (_hostName == null)
                {
                    _hostName = "localhost";
                }
                return _hostName;
            }
            set
            {
                _hostName = value;
            }
        }

        public string ThumbPrint { get; set; }

        public string _siteName = null;
        public string SiteName
        {
            get
            {
                return _siteName;
            }
            set
            {
                _siteName = value;
            }
        }

        public string _postFix = null;
        public string PostFix
        {
            get
            {
                return _postFix;
            }
            set
            {
                _postFix = value;
            }
        }

        public int _tcpPort = 8080;
        public int TcpPort
        {
            get
            {
                return _tcpPort;
            }
            set
            {
                _tcpPort = value;
            }
        }

        private int _workerProcessID = 0;
        public int WorkerProcessID
        {
            get
            {
                if (_workerProcessID == 0)
                {
                    try
                    {
                        if (IisServerType == ServerType.IISExpress)
                        {
                            _workerProcessID = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("iisexpress.exe", "Handle", null));
                        }
                        else
                        {
                            _workerProcessID = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", null));
                        }
                    }
                    catch
                    {
                        TestUtility.LogInformation("Failed to get process id of w3wp.exe");
                    }
                }
                return _workerProcessID;
            }
            set
            {
                _workerProcessID = value;
            }
        }

        public string HostNameBinding
        {
            get
            {
                if (IisServerType == ServerType.IISExpress)
                {
                    return "localhost";
                }
                else
                {
                    return "";
                }
            }
        }

        public ServerType IisServerType { get; set; }
        public string IisExpressConfigPath { get; set; }
        public int SiteId { get; set; }
        public IISConfigUtility.AppPoolBitness AppPoolBitness { get; set; }

        public TestWebSite(IISConfigUtility.AppPoolBitness appPoolBitness, string loggerPrefix = "ANCMTest", bool copyAllPublishedFiles = false, bool attachAppVerifier = false, bool publishing = true, int tcpPort = -1)
        {
            AppPoolBitness = appPoolBitness;
            
            //
            // Initialize IisServerType
            //
            if (TestFlags.Enabled(TestFlags.UseFullIIS))
            {
                IisServerType = ServerType.IIS;
            }
            else
            {
                IisServerType = ServerType.IISExpress;
            }

            //
            // Use localhost hostname for IISExpress
            //
            if (IisServerType == ServerType.IISExpress 
                && TestFlags.Enabled(TestFlags.Wow64BitMode))
            {
                //
                // In Wow64/IISExpress test context, always use 32 bit worker process
                //
                if (AppPoolBitness == IISConfigUtility.AppPoolBitness.noChange)
                {
                    TestUtility.LogInformation("Warning!!! In Wow64, _appPoolBitness should be set with enable32bit");
                    AppPoolBitness = IISConfigUtility.AppPoolBitness.enable32Bit;
                }
            }

            TestUtility.LogInformation("TestWebSite::TestWebSite() Start");

            string solutionPath = InitializeTestMachine.GetSolutionDirectory();

            if (IisServerType == ServerType.IIS)
            {
                // check JitDebugger before continuing 
                TestUtility.ResetHelper(ResetHelperMode.KillVSJitDebugger);
            }

            // initialize logger for TestUtility
            _logger = new LoggerFactory()
                    .AddConsole()
                    .CreateLogger(string.Format(loggerPrefix));

            testHelper = new TestUtility(_logger);

            //
            // Initialize context variables
            //
            string siteRootPath = string.Empty;
            string siteName = string.Empty;
            string postfix = string.Empty;

            // repeat three times until getting the valid temporary directory path
            for (int i = 0; i < 3; i++)
            {
                postfix = Path.GetRandomFileName();
                siteName = loggerPrefix.Replace(" ", "") + "_" + postfix;
                siteRootPath = Path.Combine(InitializeTestMachine.TestRootDirectory, siteName);
                if (!Directory.Exists(siteRootPath))
                {
                    break;
                }
            }

            TestUtility.DirectoryCopy(Path.Combine(solutionPath, "test", "WebRoot"), siteRootPath);
            string aspnetCoreAppRootPath = Path.Combine(siteRootPath, "AspNetCoreApp");
            string srcPath = TestUtility.GetApplicationPath();

            // copy http.config to the test site root directory and initialize iisExpressConfigPath with the path
            if (IisServerType == ServerType.IISExpress)
            {
                IisExpressConfigPath = Path.Combine(siteRootPath, "http.config");
                TestUtility.FileCopy(Path.Combine(solutionPath, "test", "AspNetCoreModule.Test", "http.config"), IisExpressConfigPath);
            }

            //
            // By default we use DotnetCore v2.0
            //
            string SDKVersion = "netcoreapp2.0";

            if (TestFlags.Enabled(TestFlags.UseDotNetCore21))
            {
                SDKVersion = "netcoreapp2.1";
            }
            else if (TestFlags.Enabled(TestFlags.UseDotNetCore22))
            {
                SDKVersion = "netcoreapp2.2";
            }

            string publishPath = Path.Combine(srcPath, "bin", "Debug", SDKVersion, "publish");
            string publishPathOutput = Path.Combine(InitializeTestMachine.TestRootDirectory, "publishPathOutput");
            
            //
            // Publish aspnetcore app
            //
            if (_publishedAspnetCoreApp != true)
            {
                if ((publishing == false && File.Exists(Path.Combine(publishPath, "AspNetCoreModule.TestSites.Standard.dll")))
                    || Debugger.IsAttached && File.Exists(Path.Combine(publishPath, "AspNetCoreModule.TestSites.Standard.dll")))
                {
                    // skip publishing
                }
                else
                {
                    string argumentForDotNet = "publish " + srcPath + " --framework " + SDKVersion;
                    TestUtility.LogInformation("TestWebSite::TestWebSite() StandardTestApp is not published, trying to publish on the fly: dotnet.exe " + argumentForDotNet);
                    TestUtility.DeleteDirectory(publishPath);

                    try
                    {
                        if (TestFlags.Enabled(TestFlags.UseDotNetCore22))
                        {
                            TestUtility.FileCopy(Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj.UseDotNetCore22"), Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj"));
                        }
                        else if (TestFlags.Enabled(TestFlags.UseDotNetCore21))
                        {
                            TestUtility.FileCopy(Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj.UseDotNetCore21"), Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj"));
                        }
                        else
                        {
                            TestUtility.FileCopy(Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj.UseDotNetCore20"), Path.Combine(srcPath, "AspNetCoreModule.TestSites.Standard.csproj"));
                        }
                    }
                    catch
                    {
                        TestUtility.LogInformation("Failed to overwrite project file, update the project file manually");
                    }


                    if (TestFlags.Enabled(TestFlags.UseDotNetCore22))
                    {
                        var aspNetCoreModuleHostingModel = TestFlags.Enabled(TestFlags.InprocessMode) ? "inprocess" : "OutOfProcess";
                        argumentForDotNet += $" /p:AspNetCoreModuleHostingModel={aspNetCoreModuleHostingModel}";
                    }

                    TestUtility.RunCommand("dotnet", argumentForDotNet);
                }

                if (!File.Exists(Path.Combine(publishPath, "AspNetCoreModule.TestSites.Standard.dll")))
                {
                    throw new Exception("Failed to publish");
                }
                TestUtility.DirectoryCopy(publishPath, publishPathOutput);
                TestUtility.FileCopy(Path.Combine(publishPathOutput, "web.config"), Path.Combine(publishPathOutput, "web.config.bak"));

                // Adjust the arguments attribute value with IISConfigUtility from a temporary site
                using (var iisConfig = new IISConfigUtility(IisServerType, IisExpressConfigPath))
                {
                    string tempSiteName = "ANCMTest_Temp";
                    int tempId = InitializeTestMachine.SiteId - 1;
                    string argumentFileName = (new TestWebApplication("/", publishPathOutput, null)).GetArgumentFileName();
                    if (string.IsNullOrEmpty(argumentFileName))
                    {
                        argumentFileName = "AspNetCoreModule.TestSites.Standard.dll";
                    }
                    iisConfig.CreateSite(tempSiteName, HostNameBinding, publishPathOutput, tempId, tempId);
                    iisConfig.SetANCMConfig(tempSiteName, "/", "arguments", Path.Combine(publishPathOutput, argumentFileName));
                    iisConfig.DeleteSite(tempSiteName);
                }
                _publishedAspnetCoreApp = true;
            }
            
            if (copyAllPublishedFiles)
            {
                // Copy all the files in the pubishpath to the standardAppRootPath
                TestUtility.DirectoryCopy(publishPath, aspnetCoreAppRootPath);
                TestUtility.FileCopy(Path.Combine(publishPathOutput, "web.config.bak"), Path.Combine(aspnetCoreAppRootPath, "web.config"));
            }
            else
            {
                // Copy only web.config file, which points to the shared publishPathOutput, to the standardAppRootPath
                TestUtility.CreateDirectory(aspnetCoreAppRootPath);
                TestUtility.FileCopy(Path.Combine(publishPathOutput, "web.config"), Path.Combine(aspnetCoreAppRootPath, "web.config"));
            }

            //
            // initialize class member variables
            //
            string appPoolName = null;
            if (IisServerType == ServerType.IIS)
            {
                appPoolName = "AspNetCoreModuleTestAppPool";
            }
            else if (IisServerType == ServerType.IISExpress)
            {
                appPoolName = "Clr4IntegratedAppPool";
            }

            // Initialize member variables
            _hostName = "localhost";
            _siteName = siteName;
            _postFix = postfix;
            if (tcpPort != -1)
            {
                _tcpPort = tcpPort;
            }
            else
            {
                _tcpPort = InitializeTestMachine.SiteId++;
                InitializeTestMachine.SiteId++;
            }
            SiteId = _tcpPort;

            RootAppContext = new TestWebApplication("/", Path.Combine(siteRootPath, "WebSite1"), this);
            RootAppContext.RestoreFile("web.config");
            RootAppContext.DeleteFile("app_offline.htm");
            RootAppContext.AppPoolName = appPoolName;
            RootAppContext.IisServerType = IisServerType;

            AspNetCoreApp = new TestWebApplication("/AspNetCoreApp", aspnetCoreAppRootPath, this);
            AspNetCoreApp.AppPoolName = appPoolName;
            AspNetCoreApp.RestoreFile("web.config");
            AspNetCoreApp.DeleteFile("app_offline.htm");
            AspNetCoreApp.IisServerType = IisServerType;

            WebSocketApp = new TestWebApplication("/WebSocketApp", Path.Combine(siteRootPath, "WebSocket"), this);
            WebSocketApp.AppPoolName = appPoolName;
            WebSocketApp.RestoreFile("web.config");
            WebSocketApp.DeleteFile("app_offline.htm");
            WebSocketApp.IisServerType = IisServerType;

            URLRewriteApp = new TestWebApplication("/URLRewriteApp", Path.Combine(siteRootPath, "URLRewrite"), this);
            URLRewriteApp.AppPoolName = appPoolName;
            URLRewriteApp.RestoreFile("web.config");
            URLRewriteApp.DeleteFile("app_offline.htm");
            URLRewriteApp.IisServerType = IisServerType;

            //
            // Create site and apps
            //
            using (var iisConfig = new IISConfigUtility(IisServerType, IisExpressConfigPath))
            {
                // Create apppool
                if (IisServerType == ServerType.IIS)
                {
                    iisConfig.DeleteAppPool(appPoolName);
                    iisConfig.CreateAppPool(appPoolName);
                    iisConfig.SetAppPoolSetting(appPoolName, "rapidFailProtectionMaxCrashes", 100);

                    // Switch bitness
                    if (TestUtility.IsOSAmd64 && appPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
                    {
                        iisConfig.SetAppPoolSetting(appPoolName, "enable32BitAppOnWin64", true);
                    }
                }
                
                if (TestFlags.Enabled(TestFlags.UsePrivateANCM) && IisServerType == ServerType.IISExpress)
                {
                    if (TestUtility.IsOSAmd64)
                    {
                        if (AppPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
                        {
                            iisConfig.AddModule("AspNetCoreModule", (InitializeTestMachine.IisExpressAspnetcore_X86_path), null);
                        }
                        else
                        {
                            iisConfig.AddModule("AspNetCoreModule", (InitializeTestMachine.IisExpressAspnetcore_path), null);
                        }
                    }
                    else
                    {
                        iisConfig.AddModule("AspNetCoreModule", (InitializeTestMachine.IisExpressAspnetcore_path), null);
                    }
                }

                iisConfig.CreateSite(siteName, HostNameBinding, RootAppContext.PhysicalPath, SiteId, TcpPort, appPoolName);
                iisConfig.CreateApp(siteName, AspNetCoreApp.Name, AspNetCoreApp.PhysicalPath, appPoolName);
                iisConfig.CreateApp(siteName, WebSocketApp.Name, WebSocketApp.PhysicalPath, appPoolName);
                iisConfig.CreateApp(siteName, URLRewriteApp.Name, URLRewriteApp.PhysicalPath, appPoolName);

                if (TestFlags.Enabled(TestFlags.UseANCMV2))
                {
                    iisConfig.SetHandler(siteName, AspNetCoreApp.Name, "aspNetCore", "modules", "AspNetCoreModuleV2");
                }

                // Configure hostingModel for aspnetcore app
                if (TestFlags.Enabled(TestFlags.InprocessMode))
                {
                    AspNetCoreApp.HostingModel = TestWebApplication.HostingModelValue.Inprocess;
                    iisConfig.SetANCMConfig(siteName, AspNetCoreApp.Name, "hostingModel", TestWebApplication.HostingModelValue.Inprocess);
                    AspNetCoreApp.DeleteFile("web.config.bak");
                    AspNetCoreApp.BackupFile("web.config");
                }
            }

            TestUtility.LogInformation("TestWebSite::TestWebSite() End");
        }

        public string GetAppVerifierPath()
        {
            string cmdline = null;
            if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%")) && AppPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
            {
                cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "syswow64", "appverif.exe");
                if (!File.Exists(cmdline))
                {
                    throw new ApplicationException("Not found :" + cmdline + "; this test requires appverif.exe.");
                }
            }
            else
            {
                cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "appverif.exe");
                if (!File.Exists(cmdline))
                {
                    throw new ApplicationException("Not found :" + cmdline + "; this test requires appverif.exe.");
                }
            }
            return cmdline;
        }

        public void AttachAppverifier()
        {
            string cmdline = GetAppVerifierPath();
            string processName = "iisexpress.exe";
            if (IisServerType == ServerType.IIS)
            {
                processName = "w3wp.exe";
            }
            string argument = "-enable Heaps COM RPC Handles Locks Memory TLS Exceptions Threadpool Leak SRWLock -for " + processName;

            try
            {
                TestUtility.LogInformation("Configure Appverifier: " + cmdline + " " + argument);
                TestUtility.RunCommand(cmdline, argument, true, false);
            }
            catch
            {
                throw new ApplicationException("Failed to configure Appverifier");
            }
        }

        public void AttachWinDbg(int processIdOfWorkerProcess, string initialCommand = null)
        {
            string processName = "iisexpress.exe";
            string debuggerCmdline;
            if (IisServerType == ServerType.IIS)
            {
                processName = "w3wp.exe";
            }
            string argument = "-enable Heaps COM RPC Handles Locks Memory TLS Exceptions Threadpool Leak SRWLock -for " + processName;
            if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%")) && AppPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
            {
                debuggerCmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "Debugging Tools for Windows (x64)", "wow64", "windbg.exe");
                if (!File.Exists(debuggerCmdline))
                {
                    throw new ApplicationException("Not found :" + debuggerCmdline + "; this test requires windbg.exe.");
                }
            }
            else
            {
                if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%")))
                {
                    debuggerCmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "Debugging Tools for Windows (x64)", "windbg.exe");
                }
                else
                {
                    debuggerCmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "Debugging Tools for Windows (x86)", "windbg.exe");
                }
                if (!File.Exists(debuggerCmdline))
                {
                    throw new ApplicationException("Not found :" + debuggerCmdline + "; this test requires windbg.exe.");
                }
            }
            
            try
            {
                if (initialCommand != null)
                {
                    TestUtility.RunCommand(debuggerCmdline, " -c \"" + initialCommand + "\" -g -G -p " + processIdOfWorkerProcess.ToString(), true, false);
                }
                else
                {
                    TestUtility.RunCommand(debuggerCmdline, " -g -G -p " + processIdOfWorkerProcess.ToString(), true, false);
                }
                System.Threading.Thread.Sleep(3000);
            }
            catch
            {
                throw new ApplicationException("Failed to attach debuger");
            }
        }

        public void DetachAppverifier()
        {
            try
            {
                string cmdline;
                string processName = "iisexpress.exe";
                string debuggerCmdline;
                if (IisServerType == ServerType.IIS)
                {
                    processName = "w3wp.exe";
                }

                string argument = "-disable * -for " + processName;
                if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%")) && AppPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
                {
                    cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "syswow64", "appverif.exe");
                    if (!File.Exists(cmdline))
                    {
                        throw new ApplicationException("Not found :" + cmdline);
                    }
                    debuggerCmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "Debugging Tools for Windows (x64)", "wow64", "windbg.exe");
                    if (!File.Exists(debuggerCmdline))
                    {
                        throw new ApplicationException("Not found :" + debuggerCmdline);
                    }
                }
                else
                {
                    cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "appverif.exe");
                    if (!File.Exists(cmdline))
                    {
                        throw new ApplicationException("Not found :" + cmdline);
                    }
                    debuggerCmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "Debugging Tools for Windows (x64)", "windbg.exe");
                    if (!File.Exists(debuggerCmdline))
                    {
                        throw new ApplicationException("Not found :" + debuggerCmdline);
                    }
                }
                TestUtility.RunCommand(cmdline, argument, true, false);
            }
            catch
            {
                TestUtility.LogInformation("Failed to detach Appverifier");
            }
        }
    }
}
