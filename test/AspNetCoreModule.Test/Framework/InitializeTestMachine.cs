// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.PlatformAbstractions;
using System.Security.Principal;
using System.Security.AccessControl;

namespace AspNetCoreModule.Test.Framework
{
    public static class ANCMTestFlags
    {
        public const string TestSkipContext = "SkipTest";
        public const string UsePrivateANCM = "UsePrivateANCM";
        public const string UseIISExpressContext = "UseIISExpress";
        public const string UseFullIISContext = "UseFullIIS";
        public const string RunAsNonAdminUser = "RunAsNonAdminUser";
        public const string MakeCertExeAvailable = "MakeCertExeAvailable";
        public const string X86Platform = "X86Platform";
        public const string Wow64BitMode = "Wow64BitMode";
        public const string P0 = "P0";
        public const string P1 = "P1";
    }

    public class InitializeTestMachine : IDisposable
    {
        public const string ANCMTestFlagsEnvironmentVariable = "%ANCMTestFlags%";

        public const string ANCMTestFlags_DefaultContext_IISExpressP0Only = ANCMTestFlags.UsePrivateANCM
                                                        + ";"
                                                        + ANCMTestFlags.UseIISExpressContext
                                                        + ";"
                                                        + ANCMTestFlags.RunAsNonAdminUser
                                                        + ";"
                                                        + ANCMTestFlags.P0;

        public const string ANCMTestFlags_DefaultContext_FullIIS = ANCMTestFlags.UsePrivateANCM
                                                        + ";"
                                                        + ANCMTestFlags.P0
                                                        + ";"
                                                        + ANCMTestFlags.P1;

        private static bool? _usePrivateAspNetCoreFile = null;
        public static bool? UsePrivateAspNetCoreFile
        {
            get {
                if (_usePrivateAspNetCoreFile == null)
                {
                    _usePrivateAspNetCoreFile = false;
                    var envValue = GlobalTestFlags;
                    if (envValue.ToLower().Contains(ANCMTestFlags.UsePrivateANCM.ToLower()))
                    {
                        TestUtility.LogInformation("PrivateAspNetCoreFile is set");
                        _usePrivateAspNetCoreFile = true;
                    }
                    else
                    {
                        TestUtility.LogInformation("PrivateAspNetCoreFile is not set");
                    }
                }
                return _usePrivateAspNetCoreFile;
            }
            set
            {
                _usePrivateAspNetCoreFile = value;
            }
        }

        public static int SiteId = 40000;
        public const string PrivateFileName = "aspnetcore_private.dll";
        public static string FullIisAspnetcore_path = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "inetsrv", PrivateFileName);
        public static string FullIisAspnetcore_path_original = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "inetsrv", "aspnetcore.dll");
        public static string FullIisAspnetcore_X86_path = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "syswow64", "inetsrv", PrivateFileName);
        public static string IisExpressAspnetcore_path;
        public static string IisExpressAspnetcore_X86_path;

        public static string IisExpressAspnetcoreSchema_path = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "IIS Express", "config", "schema", "aspnetcore_schema.xml");
        public static string IisExpressAspnetcoreSchema_X86_path = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%"), "IIS Express", "config", "schema", "aspnetcore_schema.xml");
        public static string FullIisAspnetcoreSchema_path = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "inetsrv", "config", "schema", "aspnetcore_schema.xml");
        public static int _referenceCount = 0;
        private static bool _InitializeTestMachineCompleted = false;
        private string _setupScriptPath = null;
        
        private static bool? _MakeCertExeAvailable = null;
        public static bool? MakeCertExeAvailable
        {
            get
            {
                if (_MakeCertExeAvailable == null)
                {
                    _MakeCertExeAvailable = false;
                    try
                    {
                        string makecertExeFilePath = TestUtility.GetMakeCertPath();
                        TestUtility.RunCommand(makecertExeFilePath, null, true, true);
                        TestUtility.LogInformation("Verified makecert.exe is available : " + makecertExeFilePath);
                        _MakeCertExeAvailable = true;
                    }
                    catch
                    {
                        _MakeCertExeAvailable = false;
                    }
                }
                return _MakeCertExeAvailable;
            }
        }

        public static string TestRootDirectory
        {
            get
            {
                return Path.Combine(Environment.ExpandEnvironmentVariables("%SystemDrive%") + @"\", "_ANCMTest");
            }
        }

        private static string _globalTestFlags = null;
        public static string GlobalTestFlags
        {
            get
            {
                if (_globalTestFlags == null)
                {
                    bool isElevated;
                    WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                    // check if this test process is started with the Run As Administrator start option
                    _globalTestFlags = Environment.ExpandEnvironmentVariables(ANCMTestFlagsEnvironmentVariable);
                    _globalTestFlags = _globalTestFlags.ToLower();
                    if (_globalTestFlags.Contains(ANCMTestFlags.UseIISExpressContext.ToLower()) && _globalTestFlags.Contains(ANCMTestFlags.UseFullIISContext.ToLower()))
                    {
                        throw new ApplicationException(ANCMTestFlagsEnvironmentVariable + " can't be set with both "
                            + ANCMTestFlags.UseIISExpressContext + " and " + ANCMTestFlags.UseFullIISContext);
                    }

                    // set default context with ANCMTestFlags_DefaultContext_IISExpressP0Only
                    if (!isElevated
                        || _globalTestFlags.Replace(ANCMTestFlagsEnvironmentVariable.ToLower(), "").Trim() == ""
                        || _globalTestFlags.Contains(ANCMTestFlags.UseIISExpressContext.ToLower()))
                    {
                        _globalTestFlags = ANCMTestFlags_DefaultContext_IISExpressP0Only;
                    }

                    // if users set UseFullIIS, the default value should be overwritten with ANCMTestFlags_DefaultContext_FullIIS
                    if (isElevated
                        && _globalTestFlags.Contains(ANCMTestFlags.UseFullIISContext.ToLower()))
                    {
                        _globalTestFlags = ANCMTestFlags_DefaultContext_FullIIS;
                    }

                    // adjust the default test context in run time to figure out wrong test context values
                    if (!isElevated)
                    {
                        if (_globalTestFlags.Contains(ANCMTestFlags.UseFullIISContext.ToLower()))
                        {
                            _globalTestFlags = _globalTestFlags.Replace(ANCMTestFlags.UseFullIISContext.ToLower(), "");
                        }

                        if (!_globalTestFlags.Contains(ANCMTestFlags.UseIISExpressContext.ToLower()))
                        {
                            TestUtility.LogInformation("Added test context of " + ANCMTestFlags.UseIISExpressContext);
                            _globalTestFlags += ";" + ANCMTestFlags.UseIISExpressContext;
                        }

                        if (!_globalTestFlags.Contains(ANCMTestFlags.RunAsNonAdminUser.ToLower()))
                        {
                            TestUtility.LogInformation("Added test context of " + ANCMTestFlags.RunAsNonAdminUser);
                            _globalTestFlags += ";" + ANCMTestFlags.RunAsNonAdminUser;
                        }
                    }

                    if (MakeCertExeAvailable == true)
                    {
                        if (!_globalTestFlags.Contains(ANCMTestFlags.MakeCertExeAvailable.ToLower()))
                        {
                            TestUtility.LogInformation("Added test context of " + ANCMTestFlags.MakeCertExeAvailable);
                            _globalTestFlags += ";" + ANCMTestFlags.MakeCertExeAvailable;
                        }
                    }

                    if (!Environment.Is64BitOperatingSystem)
                    {
                        if (!_globalTestFlags.Contains(ANCMTestFlags.X86Platform.ToLower()))
                        {
                            TestUtility.LogInformation("Added test context of " + ANCMTestFlags.X86Platform);
                            _globalTestFlags += ";" + ANCMTestFlags.X86Platform;
                        }
                    }

                    if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
                    {
                        if (!_globalTestFlags.Contains(ANCMTestFlags.Wow64BitMode.ToLower()))
                        {
                            TestUtility.LogInformation("Added test context of " + ANCMTestFlags.Wow64BitMode);
                            _globalTestFlags += ";" + ANCMTestFlags.Wow64BitMode;
                        }
                    }

                    _globalTestFlags = _globalTestFlags.ToLower();
                }

                return _globalTestFlags;
            }            
        }

        public InitializeTestMachine()
        {
            _referenceCount++;

            if (_referenceCount == 1)
            {
                TestUtility.LogInformation("InitializeTestMachine::InitializeTestMachine() Start");

                _InitializeTestMachineCompleted = false;

                TestUtility.LogInformation("InitializeTestMachine::Start");
                if (Environment.ExpandEnvironmentVariables("%ANCMTEST_DEBUG%").ToLower() == "true")
                {
                    System.Diagnostics.Debugger.Launch();                    
                }
                
                //
                // Clean up IISExpress process
                //
                TestUtility.ResetHelper(ResetHelperMode.KillIISExpress);

                // Check if we can use IIS server instead of IISExpress
                try
                {
                    IISConfigUtility.IsIISReady = false;
                    if (IISConfigUtility.IsIISInstalled == true)
                    {
                        var envValue = GlobalTestFlags;
                        
                        if (envValue.ToLower().Contains(ANCMTestFlags.UseIISExpressContext.ToLower()))
                        {
                            TestUtility.LogInformation("UseIISExpress is set");
                            throw new System.ApplicationException("'ANCMTestServerType' environment variable is set to 'true'");
                        }
                        else
                        {
                            TestUtility.LogInformation("UseIISExpress is not set");
                        }
                        
                        // Check websocket is installed
                        if (File.Exists(Path.Combine(IISConfigUtility.Strings.IIS64BitPath, "iiswsock.dll")))
                        {
                            TestUtility.LogInformation("Websocket is installed");
                        }
                        else
                        {
                            throw new System.ApplicationException("websocket module is not installed");
                        }

                        // Clean up IIS worker process
                        TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);

                        // Reset applicationhost.config
                        TestUtility.LogInformation("Restoring applicationhost.config");                        
                        IISConfigUtility.RestoreAppHostConfig(restoreFromMasterBackupFile:true);
                        TestUtility.StartW3svc();

                        // check w3svc is running after resetting applicationhost.config
                        if (IISConfigUtility.GetServiceStatus("w3svc") == "Running")
                        {
                            TestUtility.LogInformation("W3SVC service is restarted after restoring applicationhost.config");
                        }
                        else
                        {
                            throw new System.ApplicationException("WWW service can't start");
                        }

                        // check URLRewrite module exists
                        if (File.Exists(Path.Combine(IISConfigUtility.Strings.IIS64BitPath, "rewrite.dll")))
                        {
                            TestUtility.LogInformation("Verified URL Rewrite module installed for IIS server");
                        }
                        else
                        {
                            throw new System.ApplicationException("URL Rewrite module is not installed");
                        }

                        if (IISConfigUtility.ApppHostTemporaryBackupFileExtention == null)
                        {
                            throw new System.ApplicationException("Failed to backup applicationhost.config");
                        }
                        IISConfigUtility.IsIISReady = true;
                    }
                }
                catch (Exception ex)
                {
                    RollbackIISApplicationhostConfigFile();
                    TestUtility.LogInformation("We will use IISExpress instead of IIS: " + ex.Message);
                }

                string siteRootPath = TestRootDirectory;
                if (!Directory.Exists(siteRootPath))
                {
                    Directory.CreateDirectory(siteRootPath);
                    DirectorySecurity sec = Directory.GetAccessControl(siteRootPath);
                    SecurityIdentifier authenticatedUser = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                    sec.AddAccessRule(new FileSystemAccessRule(authenticatedUser, FileSystemRights.Modify | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    Directory.SetAccessControl(siteRootPath, sec);
                }

                foreach (string directory in Directory.GetDirectories(siteRootPath))
                {
                    bool successDeleteChildDirectory = true;
                    try
                    {
                        TestUtility.DeleteDirectory(directory);
                    }
                    catch
                    {
                        successDeleteChildDirectory = false;
                        TestUtility.LogInformation("Failed to delete " + directory);
                    }
                    if (successDeleteChildDirectory)
                    {
                        try
                        {
                            TestUtility.DeleteDirectory(siteRootPath);
                        }
                        catch
                        {
                            TestUtility.LogInformation("Failed to delete " + siteRootPath);
                        }
                    }
                }
                
                if (InitializeTestMachine.UsePrivateAspNetCoreFile == true)
                {
                    PreparePrivateANCMFiles(IISConfigUtility.IsIISReady);

                    // update applicationhost.config for IIS server
                    if (IISConfigUtility.IsIISReady)
                    {
                        using (var iisConfig = new IISConfigUtility(ServerType.IIS, null))
                        {
                            iisConfig.AddModule("AspNetCoreModule", FullIisAspnetcore_path, null);
                        }
                    }
                }

                _InitializeTestMachineCompleted = true;
                TestUtility.LogInformation("InitializeTestMachine::InitializeTestMachine() End");
            }

            for (int i=0; i<120; i++)                    
            {
                if (_InitializeTestMachineCompleted)
                {
                    break;
                }   
                else
                {
                    TestUtility.LogInformation("InitializeTestMachine::InitializeTestMachine() Waiting...");
                    Thread.Sleep(500);
                }
            }
            if (!_InitializeTestMachineCompleted)
            {
                throw new System.ApplicationException("InitializeTestMachine failed");
            }
        }

        public void Dispose()
        {
            _referenceCount--;

            if (_referenceCount == 0)
            {
                TestUtility.LogInformation("InitializeTestMachine::Dispose() Start");
                TestUtility.ResetHelper(ResetHelperMode.KillIISExpress);
                RollbackIISApplicationhostConfigFile();
                TestUtility.LogInformation("InitializeTestMachine::Dispose() End");
            }
        }

        private void RollbackIISApplicationhostConfigFile()
        {
            if (IISConfigUtility.ApppHostTemporaryBackupFileExtention != null)
            {
                try
                {
                    TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);
                }
                catch
                {
                    TestUtility.LogInformation("Failed to stop IIS worker processes");
                }
                try
                {
                    IISConfigUtility.RestoreAppHostConfig(restoreFromMasterBackupFile: false);
                }
                catch
                {
                    TestUtility.LogInformation("Failed to rollback applicationhost.config");
                }
                try
                {
                    TestUtility.StartW3svc();
                }
                catch
                {
                    TestUtility.LogInformation("Failed to start w3svc");
                }
                IISConfigUtility.ApppHostTemporaryBackupFileExtention = null;
            }
        }

        private void PreparePrivateANCMFiles(bool isIISReady)
        {
            var solutionRoot = GetSolutionDirectory();
            string outputPath = string.Empty;
            _setupScriptPath = Path.Combine(solutionRoot, "tools");

            // First try with release build
            outputPath = Path.Combine(solutionRoot, "artifacts", "build", "AspNetCore", "bin", "Release");

            // If release build is not available, try with debug build
            if (!File.Exists(Path.Combine(outputPath, "Win32", "aspnetcore.dll"))
                || !File.Exists(Path.Combine(outputPath, "x64", "aspnetcore.dll"))
                || !File.Exists(Path.Combine(outputPath, "x64", "aspnetcore_schema.xml")))
            {
                outputPath = Path.Combine(solutionRoot, "artifacts", "build", "AspNetCore", "bin", "Debug");
            }
            
            if (!File.Exists(Path.Combine(outputPath, "Win32", "aspnetcore.dll"))
                || !File.Exists(Path.Combine(outputPath, "x64", "aspnetcore.dll"))
                || !File.Exists(Path.Combine(outputPath, "x64", "aspnetcore_schema.xml")))
            {
                throw new ApplicationException("aspnetcore.dll is not available; check if there is any build issue!!!");
            }
            
            // create an extra private copy of the private file on IIS directory
            if (InitializeTestMachine.UsePrivateAspNetCoreFile == true)
            {
                bool updateSuccess = false;

                for (int i = 0; i < 3; i++)
                {
                    updateSuccess = false;
                    try
                    {
                        //
                        // NOTE: ANCM schema file can't be overwritten here, if there is any schema change, that should be updated with installing setup
                        //

                        if (isIISReady)
                        {
                            TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);
                            TestUtility.ResetHelper(ResetHelperMode.StopW3svcStartW3svc);
                            Thread.Sleep(1000);
                        }

                        string from = Path.Combine(outputPath, "x64", "aspnetcore.dll");
                        if (isIISReady)
                        {
                            // Copy private file on Inetsrv directory
                            TestUtility.FileCopy(from, FullIisAspnetcore_path, overWrite: true, ignoreExceptionWhileDeletingExistingFile: false);
                        }

                        // Initialize IisExpressAspnetcore_path
                        IisExpressAspnetcore_path = from;
                                                
                        if (TestUtility.IsOSAmd64)
                        {
                            string from_32bit = Path.Combine(outputPath, "Win32", "aspnetcore.dll");

                            if (isIISReady)
                            {
                                // Copy 32bit private file on Inetsrv directory
                                TestUtility.FileCopy(from_32bit, FullIisAspnetcore_X86_path, overWrite: true, ignoreExceptionWhileDeletingExistingFile: false);
                            }

                            // Initialize 32 bit IisExpressAspnetcore_path
                            IisExpressAspnetcore_X86_path = from_32bit;
                        }

                        updateSuccess = true;
                    }
                    catch
                    {
                        updateSuccess = false;
                    }
                    if (updateSuccess)
                    {
                        break;
                    }
                }
                if (!updateSuccess)
                {
                    throw new System.ApplicationException("Failed to update aspnetcore.dll");
                }
            }
        }

        public static string GetSolutionDirectory()
        {
            var applicationBasePath = PlatformServices.Default.Application.ApplicationBasePath;
            var directoryInfo = new DirectoryInfo(applicationBasePath);
            do
            {
                var solutionFile = new FileInfo(Path.Combine(directoryInfo.FullName, "AspNetCoreModule.sln"));
                if (solutionFile.Exists)
                {
                    return directoryInfo.FullName;
                }

                directoryInfo = directoryInfo.Parent;
            }
            while (directoryInfo.Parent != null);

            throw new Exception($"Solution root could not be located using application root {applicationBasePath}.");
        }
    }
}
