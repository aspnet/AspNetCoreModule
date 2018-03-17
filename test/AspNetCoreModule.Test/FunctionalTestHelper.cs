// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using AspNetCoreModule.Test.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Xunit;
using Xunit.Sdk;
using System.Diagnostics;
using System.Net;
using System.Threading;
using AspNetCoreModule.Test.WebSocketClient;
using System.Text;
using System.IO;
using System.Security.Principal;
using System.IO.Compression;
using Microsoft.AspNetCore.Testing.xunit;

namespace AspNetCoreModule.Test
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ANCMTestFlags : Attribute, ITestCondition
    {
        private readonly string _attributeValue = null;

        public ANCMTestFlags(string attributeValue)
        {
            _attributeValue = attributeValue;

            if (_attributeValue == TestFlags.SkipTest && (TestFlags.Enabled(TestFlags.UseFullIIS) || TestFlags.Enabled(TestFlags.UseIISExpress)))
            {
                // Currently the global test flag is set to TestFlags.SkipTest.
                // However, if ANCMTestFlags environmentvariable is set to UseFullIIS or UseIISExpress, 
                // we need ignore the default global test flag to run test.
                _attributeValue = TestFlags.RunAsAdministrator;
            }
        }

        public bool IsMet
        {
            get
            {
                if (_attributeValue == TestFlags.SkipTest)
                {
                    AdditionalInfo = TestFlags.SkipTest + " is set";
                    return false;
                }

                if (_attributeValue == TestFlags.RequireRunAsAdministrator 
                    && !TestFlags.Enabled(TestFlags.RunAsAdministrator))
                { 
                    AdditionalInfo = _attributeValue + " is not belong to the given global test context(" + InitializeTestMachine.GlobalTestFlags + ")";
                    return false;
                }
                return true;
            }
        }

        public string SkipReason
        {
            get
            {
                return $"Skip condition: ANCMTestFlags: this test case is skipped becauset {AdditionalInfo}.";
            }
        }

        public string AdditionalInfo { get; set; }
    }

    public class FunctionalTestHelper
    {
        public FunctionalTestHelper()
        {
        }

        private const int _repeatCount = 3;
        
        public static async Task DoBasicTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoBasicTest"))
            {
                string backendProcessId_old = null;
                DateTime startTime = DateTime.Now;
                
                await StartIISExpress(testSite);

                string backendProcessId = await GetAspnetCoreAppProcessId(testSite); 
                if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess && backendProcessId_old != null)
                {
                    Assert.Equal(backendProcessId_old, backendProcessId);
                }
                else
                {
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                }

                var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));

                var httpClientHandler = new HttpClientHandler();
                var httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = testSite.AspNetCoreApp.GetUri(),
                    Timeout = TimeSpan.FromSeconds(5),
                };

                // Invoke given test scenario function
                await CheckChunkedAsync(httpClient, testSite.AspNetCoreApp);
            }
        }

        public static async Task DoRecycleApplicationAfterBackendProcessBeingKilled(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecycleApplicationAfterBackendProcessBeingKilled"))
            {
                await StartIISExpress(testSite);

                string backendProcessId_old = null;
                const int repeatCount = 3;
                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    
                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));

                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    backendProcess.Kill();
                    Thread.Sleep(500);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                }
            }
        }

        public static async Task DoRecycleApplicationAfterW3WPProcessBeingKilled(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecycleApplicationAfterW3WPProcessBeingKilled"))
            {
                if (testSite.IisServerType == ServerType.IISExpress)
                {
                    TestUtility.LogInformation("This test is not valid for IISExpress server type");
                    return;
                }

                await StartIISExpress(testSite);

                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();
                string backendProcessId_old = null;
                const int repeatCount = 3;

                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1000);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));

                    // get process id of IIS worker process (w3wp.exe)
                    string userName = testSite.RootAppContext.AppPoolName;
                    int processIdOfWorkerProcess = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));
                    var workerProcess = Process.GetProcessById(Convert.ToInt32(processIdOfWorkerProcess));
                    workerProcess.Kill();

                    Thread.Sleep(500);

                    // Verify the application file can be removed after worker process is stopped
                    testSite.AspNetCoreApp.BackupFile(appDllFileName);
                    testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                    testSite.AspNetCoreApp.RestoreFile(appDllFileName);
                }
            }
        }

        public static async Task DoRecycleApplicationAfterWebConfigUpdated(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecycleApplicationAfterWebConfigUpdated"))
            {
                await StartIISExpress(testSite);

                string backendProcessId_old = null;
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();
                const int repeatCount = 3;
                
                // configuration change from same level
                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1000);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    testSite.RootAppContext.MoveFile("web.config", "_web.config");
                    Thread.Sleep(500);
                    testSite.RootAppContext.MoveFile("_web.config", "web.config");

                    // Verify the application file can be removed after backend process is restarted
                    testSite.AspNetCoreApp.BackupFile(appDllFileName);
                    testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                    testSite.AspNetCoreApp.RestoreFile(appDllFileName);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                }

                // configuration change from same level
                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1000);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    testSite.AspNetCoreApp.MoveFile("web.config", "_web.config");
                    Thread.Sleep(500);
                    testSite.AspNetCoreApp.MoveFile("_web.config", "web.config");

                    // Verify the application file can be removed after backend process is restarted
                    testSite.AspNetCoreApp.BackupFile(appDllFileName);
                    testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                    testSite.AspNetCoreApp.RestoreFile(appDllFileName);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                }

                // restore web.config
                testSite.AspNetCoreApp.RestoreFile("web.config");

            }
        }

        public static async Task DoRecycleApplicationWithURLRewrite(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecycleApplicationWithURLRewrite"))
            {
                await StartIISExpress(testSite);

                string backendProcessId_old = null;
                const int repeatCount = 3;

                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1100);

                    string urlForUrlRewrite = testSite.URLRewriteApp.URL + "/Rewrite2/" + testSite.AspNetCoreApp.URL + "/GetProcessId";
                    string backendProcessId = (await GetAspnetCoreAppProcessId(testSite, testSite.RootAppContext.GetUri(urlForUrlRewrite)));
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));

                    testSite.AspNetCoreApp.MoveFile("web.config", "_web.config");
                    Thread.Sleep(500);
                    testSite.AspNetCoreApp.MoveFile("_web.config", "web.config");

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                }

                // restore web.config
                testSite.AspNetCoreApp.RestoreFile("web.config");

            }
        }

        public static async Task DoRecycleParentApplicationWithURLRewrite(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecycleParentApplicationWithURLRewrite"))
            {
                await StartIISExpress(testSite);

                string backendProcessId_old = null;
                const int repeatCount = 3;

                for (int i = 0; i < repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow("https://github.com/aspnet/IISIntegration/issues/670");

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1000);

                    string urlForUrlRewrite = testSite.URLRewriteApp.URL + "/Rewrite2/" + testSite.AspNetCoreApp.URL + "/GetProcessId";
                    string backendProcessId = (await GetAspnetCoreAppProcessId(testSite, testSite.RootAppContext.GetUri(urlForUrlRewrite)));
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    testSite.RootAppContext.MoveFile("web.config", "_web.config");
                    Thread.Sleep(500);
                    testSite.RootAppContext.MoveFile("_web.config", "web.config");

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                }

                // restore web.config
                testSite.RootAppContext.RestoreFile("web.config");
            }
        }

        public static async Task DoEnvironmentVariablesTest(string environmentVariableName, string environmentVariableValue, string expectedEnvironmentVariableValue, IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            if (environmentVariableName == null)
            {
                throw new InvalidDataException("envrionmentVarialbeName is null");
            }
            using (var testSite = new TestWebSite(appPoolBitness, "DoEnvironmentVariablesTest"))
            {
                await StartIISExpress(testSite);

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(500);

                    string totalNumber = (await SendReceive(testSite.AspNetCoreApp.GetUri("GetEnvironmentVariables"))).ResponseBody;
                    Assert.True(totalNumber == (await SendReceive(testSite.AspNetCoreApp.GetUri("GetEnvironmentVariables"))).ResponseBody);
                    string recycledProcessId = await GetAspnetCoreAppProcessId(testSite);
                    string backendProcessId = recycledProcessId;

                    iisConfig.SetANCMConfig(
                        testSite.SiteName,
                        testSite.AspNetCoreApp.Name,
                        "environmentVariable",
                        new string[] { "ANCMTestFoo", "foo" }
                        );

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, recycledProcessId);

                    Thread.Sleep(500);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    int expectedValue = Convert.ToInt32(totalNumber) + 1;
                    string totalResult = (await SendReceive(testSite.AspNetCoreApp.GetUri("GetEnvironmentVariables"))).ResponseBody;
                    Assert.True(expectedValue.ToString() == (await SendReceive(testSite.AspNetCoreApp.GetUri("GetEnvironmentVariables"))).ResponseBody);

                    bool setEnvironmentVariableConfiguration = true;

                    // Set authentication for ASPNETCORE_IIS_HTTPAUTH test scenarios
                    if (environmentVariableName == "ASPNETCORE_IIS_HTTPAUTH" && environmentVariableValue != "ignoredValue")
                    {
                        setEnvironmentVariableConfiguration = false;
                        bool windows = false;
                        bool basic = false;
                        bool anonymous = false;
                        if (environmentVariableValue.Contains("windows;"))
                        {
                            windows = true;
                        }
                        if (environmentVariableValue.Contains("basic;"))
                        {
                            basic = true;
                        }
                        if (environmentVariableValue.Contains("anonymous;"))
                        {
                            anonymous = true;
                        }

                        await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseBody: "Running", numberOfRetryCount: 10);
                        recycledProcessId = await GetAspnetCoreAppProcessId(testSite);

                        iisConfig.EnableIISAuthentication(testSite.SiteName, windows, basic, anonymous);
                    }

                    if (environmentVariableValue == "NA" || environmentVariableValue == null)
                    {
                        setEnvironmentVariableConfiguration = false;
                    }

                    // Add a new environment variable
                    if (setEnvironmentVariableConfiguration)
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseBody: "Running", numberOfRetryCount: 10);
                        recycledProcessId = await GetAspnetCoreAppProcessId(testSite);

                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { environmentVariableName, environmentVariableValue });

                        // Adjust the new expected total number of environment variables
                        if (environmentVariableName != "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES" &&
                            environmentVariableName != "ASPNETCORE_IIS_HTTPAUTH")
                        {
                            expectedValue++;
                        }
                    }

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, recycledProcessId);

                    Thread.Sleep(500);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();
                    totalResult = (await SendReceive(testSite.AspNetCoreApp.GetUri("GetEnvironmentVariables"))).ResponseBody;
                    Assert.True(expectedValue.ToString() == totalResult);
                    Assert.True("foo" == (await SendReceive(testSite.AspNetCoreApp.GetUri("ExpandEnvironmentVariablesANCMTestFoo"))).ResponseBody);
                    Assert.True(expectedEnvironmentVariableValue == (await SendReceive(testSite.AspNetCoreApp.GetUri("ExpandEnvironmentVariables" + environmentVariableName))).ResponseBody);

                    // verify environment variables passed to backend process
                    if (testSite.AspNetCoreApp.HostingModel != TestWebApplication.HostingModelValue.Inprocess)
                    {
                        // Verify other common environment variables
                        string temp = (await SendReceive(testSite.AspNetCoreApp.GetUri("DumpEnvironmentVariables"))).ResponseBody;
                        Assert.Contains("ASPNETCORE_PORT", temp);
                        Assert.Contains("ASPNETCORE_APPL_PATH", temp);
                        Assert.Contains("ASPNETCORE_IIS_HTTPAUTH", temp);
                        Assert.Contains("ASPNETCORE_TOKEN", temp);
                        Assert.Contains("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", temp);

                        // Verify other inherited environment variables
                        Assert.Contains("PROCESSOR_ARCHITECTURE", temp);
                        Assert.Contains("USERNAME", temp);
                        Assert.Contains("USERDOMAIN", temp);
                        Assert.Contains("USERPROFILE", temp);
                    }
                }

                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoAppOfflineTestWithRenaming(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            string appPoolName = null;
            using (var testSite = new TestWebSite(appPoolBitness, "DoAppOfflineTestWithRenaming"))
            {
                appPoolName = testSite.AspNetCoreApp.AppPoolName;
                string backendProcessId_old = null;
                string fileContent = "BackEndAppOffline";
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();

                testSite.AspNetCoreApp.CreateFile(new string[] { fileContent }, "App_Offline.Htm");

                await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: fileContent);

                for (int i = 0; i < _repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1100);
                    // verify 503 
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);

                    // Verify the application file can be removed under app_offline mode
                    testSite.AspNetCoreApp.BackupFile(appDllFileName);
                    testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                    testSite.AspNetCoreApp.RestoreFile(appDllFileName);

                    // rename app_offline.htm to _app_offline.htm and verify 200
                    testSite.AspNetCoreApp.MoveFile("App_Offline.Htm", "_App_Offline.Htm");
                    
                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));

                    // rename back to app_offline.htm
                    testSite.AspNetCoreApp.MoveFile("_App_Offline.Htm", "App_Offline.Htm");

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId_old, restartIISExpres: false);
                    await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: fileContent);
                }
            }
        }

        public static async Task DoAppOfflineTestWithUrlRewriteAndDeleting(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoAppOfflineTestWithUrlRewriteAndDeleting"))
            {
                string backendProcessId_old = null;
                string fileContent = "BackEndAppOffline2";
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();

                testSite.AspNetCoreApp.CreateFile(new string[] { fileContent }, "App_Offline.Htm");

                await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: fileContent);

                for (int i = 0; i < _repeatCount; i++)
                {
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(1100);

                    // verify 503 
                    string urlForUrlRewrite = testSite.URLRewriteApp.URL + "/Rewrite2/" + testSite.AspNetCoreApp.URL + "/GetProcessId";
                    await SendReceive(testSite.RootAppContext.GetUri(urlForUrlRewrite), expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);

                    // Verify the application file can be removed under app_offline mode
                    testSite.AspNetCoreApp.BackupFile(appDllFileName);
                    testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                    testSite.AspNetCoreApp.RestoreFile(appDllFileName);

                    // delete app_offline.htm and verify 200 
                    testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                    string backendProcessId = (await SendReceive(testSite.RootAppContext.GetUri(urlForUrlRewrite))).ResponseBody;
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                    Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                    Assert.NotEqual(backendProcessId_old, backendProcessId);
                    backendProcessId_old = backendProcessId;
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));

                    // create app_offline.htm again
                    testSite.AspNetCoreApp.CreateFile(new string[] { fileContent }, "App_Offline.Htm");

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId_old, restartIISExpres: false);
                    await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: fileContent);
                }
            }
        }

        public static async Task DoPostMethodTest(IISConfigUtility.AppPoolBitness appPoolBitness, string testData)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoPostMethodTest"))
            {
                await StartIISExpress(testSite);

                var postFormData = new[]
                {
                    new KeyValuePair<string, string>("FirstName", "Mickey"),
                    new KeyValuePair<string, string>("LastName", "Mouse"),
                    new KeyValuePair<string, string>("TestData", testData),
                };
                var expectedResponseBody = "FirstName=Mickey&LastName=Mouse&TestData=" + testData;
                await SendReceive(testSite.AspNetCoreApp.GetUri("EchoPostData"), postData: postFormData, expectedResponseBody: expectedResponseBody);
            }
        }

        public static async Task DoDisableStartUpErrorPageTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            int errorEventId = 1000;
            string errorMessageContainThis = "bogus"; // bogus path value to cause 502.3 error

            using (var testSite = new TestWebSite(appPoolBitness, "DoDisableStartUpErrorPageTest"))
            {
                testSite.AspNetCoreApp.DeleteFile("custom502-3.htm");
                string curstomErrorMessage502 = "ANCMTest502-3";
                testSite.AspNetCoreApp.CreateFile(new string[] { curstomErrorMessage502 }, "custom502-3.htm");

                testSite.AspNetCoreApp.DeleteFile("custom500-0.htm");
                string curstomErrorMessage500 = "ANCMTest500-0";
                testSite.AspNetCoreApp.CreateFile(new string[] { curstomErrorMessage500 }, "custom500-0.htm");

                Thread.Sleep(500);

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(500);

                    iisConfig.ConfigureCustomLogging(testSite.SiteName, testSite.AspNetCoreApp.Name, 502, 3, "custom502-3.htm");
                    iisConfig.ConfigureCustomLogging(testSite.SiteName, testSite.AspNetCoreApp.Name, 500, 0, "custom500-0.htm");
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "disableStartUpErrorPage", true);
                    
                    // Set bogus value to make error page
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "processPath", errorMessageContainThis);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "arguments", errorMessageContainThis);

                    if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                    {
                        await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: curstomErrorMessage500);

                        var responseBody = (await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseStatus: HttpStatusCode.InternalServerError)).ResponseBody;
                        responseBody = responseBody.Replace("\r", "").Replace("\n", "").Trim();
                        Assert.Equal(curstomErrorMessage500, responseBody);
                    }
                    else
                    {
                        await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.BadGateway, expectedResponseBody: curstomErrorMessage502);

                        var responseBody = (await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseStatus: HttpStatusCode.BadGateway)).ResponseBody;
                        responseBody = responseBody.Replace("\r", "").Replace("\n", "").Trim();
                        Assert.Equal(curstomErrorMessage502, responseBody);

                        // verify event error log
                        Assert.True(TestUtility.RetryHelper((arg1, arg2, arg3) => VerifyApplicationEventLog(arg1, arg2, arg3), errorEventId, startTime, errorMessageContainThis));

                        // try again after setting "false" value
                        startTime = DateTime.Now;
                        Thread.Sleep(500);
                    
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "disableStartUpErrorPage", false);
                        Thread.Sleep(3000);

                        // check JitDebugger before continuing 
                        CleanupVSJitDebuggerWindow();

                        responseBody = (await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseStatus:HttpStatusCode.BadGateway)).ResponseBody;
                        Assert.Contains("808681", responseBody);
                    }

                    // verify event error log
                    Assert.True(TestUtility.RetryHelper((arg1, arg2, arg3) => VerifyApplicationEventLog(arg1, arg2, arg3), errorEventId, startTime, errorMessageContainThis));
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoRapidFailsPerMinuteTest(IISConfigUtility.AppPoolBitness appPoolBitness, int valueOfRapidFailsPerMinute)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRapidFailsPerMinuteTest"))
            {
                if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                {
                    TestUtility.LogInformation("This test is not valid for Inprocess mode");
                    return;
                }
                
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    bool rapidFailsTriggered = false;
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "rapidFailsPerMinute", valueOfRapidFailsPerMinute);

                    await StartIISExpress(testSite);

                    string backendProcessId_old = null;
                    const int repeatCount = 10;

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(50);

                    for (int i = 0; i < repeatCount; i++)
                    {
                        // check JitDebugger before continuing 
                        CleanupVSJitDebuggerWindow();

                        DateTime startTimeInsideLooping = DateTime.Now;
                        Thread.Sleep(50);

                        var sendReceiveContext = await SendReceive(testSite.AspNetCoreApp.GetUri(""));
                        var statusCode = sendReceiveContext.ResponseStatus;

                        if (statusCode != HttpStatusCode.OK.ToString())
                        {
                            Assert.True(i >= valueOfRapidFailsPerMinute, i.ToString() + "is greater than or equals to " + valueOfRapidFailsPerMinute.ToString());
                            Assert.True(i < valueOfRapidFailsPerMinute + 3, i.ToString() + "is less than " + (valueOfRapidFailsPerMinute + 3).ToString());
                            rapidFailsTriggered = true;
                            break;
                        }

                        string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                        Assert.NotEqual(backendProcessId_old, backendProcessId);
                        backendProcessId_old = backendProcessId;
                        var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                        Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));

                        //Verifying EventID of new backend process is not necesssary and removed in order to fix some test reliablity issues
                        //Thread.Sleep(3000);
                        //Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTimeInsideLooping, backendProcessId), "Verifying event log of new backend process id " + backendProcessId);

                        backendProcess.Kill();
                        Thread.Sleep(3000);
                    }
                    Assert.True(rapidFailsTriggered, "Verify 502 Bad Gateway error");

                    // verify event error log
                    int errorEventId = 1003;
                    string errorMessageContainThis = "'" + valueOfRapidFailsPerMinute + "'"; // part of error message
                    Assert.True(TestUtility.RetryHelper((arg1, arg2, arg3) => VerifyApplicationEventLog(arg1, arg2, arg3), errorEventId, startTime, errorMessageContainThis));
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoProcessesPerApplicationTest(IISConfigUtility.AppPoolBitness appPoolBitness, int valueOfProcessesPerApplication)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoProcessesPerApplicationTest"))
            {
                if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                {
                    TestUtility.LogInformation("This test is not valid for Inprocess mode");
                    return;
                }

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(3000);

                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "processesPerApplication", valueOfProcessesPerApplication);

                    await StartIISExpress(testSite);

                    HashSet<int> processIDs = new HashSet<int>();

                    for (int i = 0; i < 20; i++)
                    {
                        string backendProcessId = await GetAspnetCoreAppProcessId(testSite, numberOfRetryCount:1, verifyRunning:false);
                        int id = Convert.ToInt32(backendProcessId);
                        if (!processIDs.Contains(id))
                        {
                            processIDs.Add(id);
                        }

                        if (i == (valueOfProcessesPerApplication - 1))
                        {
                            Assert.Equal(valueOfProcessesPerApplication, processIDs.Count);
                        }
                    }

                    Assert.Equal(valueOfProcessesPerApplication, processIDs.Count);

                    foreach (var id in processIDs)
                    {
                        var backendProcess = Process.GetProcessById(id);
                        Assert.Equal(backendProcess.ProcessName.ToLower().Replace(".exe", ""), testSite.AspNetCoreApp.GetProcessFileName().ToLower().Replace(".exe", ""));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, id.ToString()));
                    }

                    // reset the value with 1 again
                    processIDs = new HashSet<int>();
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "processesPerApplication", 1);
                    Thread.Sleep(3000);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();
                    Thread.Sleep(500);

                    for (int i = 0; i < 20; i++)
                    {
                        string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                        int id = Convert.ToInt32(backendProcessId);
                        if (!processIDs.Contains(id))
                        {
                            processIDs.Add(id);
                        }
                    }
                    Assert.Single(processIDs);
                }

                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoStartupTimeLimitTest(IISConfigUtility.AppPoolBitness appPoolBitness, int startupTimeLimit)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoStartupTimeLimitTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    int startupDelay = 3; //3 seconds
                    iisConfig.SetANCMConfig(
                        testSite.SiteName,
                        testSite.AspNetCoreApp.Name,
                        "environmentVariable",
                        new string[] { "ANCMTestStartUpDelay", (startupDelay * 1000).ToString() }
                        );

                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "requestTimeout", TimeSpan.Parse("00:01:00")); // 1 minute
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "startupTimeLimit", startupTimeLimit);

                    await StartIISExpress(testSite);

                    Thread.Sleep(500);
                    if (startupTimeLimit < startupDelay)
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep3000"), HttpStatusCode.BadGateway, timeout: 10);
                    }
                    else
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep3000"), expectedResponseBody: "Running", timeout: 10);
                    }
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoRequestTimeoutTest(IISConfigUtility.AppPoolBitness appPoolBitness, string requestTimeout)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRequestTimeoutTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "requestTimeout", TimeSpan.Parse(requestTimeout));

                    await StartIISExpress(testSite);

                    Thread.Sleep(500);

                    if (requestTimeout.ToString() == "00:02:00")
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep65000"), expectedResponseBody: "Running", timeout: 70);
                    }
                    else if (requestTimeout.ToString() == "00:01:00")
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep65000"), HttpStatusCode.BadGateway, timeout: 70);
                    }
                    else if (requestTimeout.ToString() == "00:00:20")
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep15000"), expectedResponseBody: "Running", timeout: 20);
                    }
                    else if (requestTimeout.ToString() == "00:00:10")
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep15000"), HttpStatusCode.BadGateway, timeout: 20);
                    }
                    else
                    {
                        throw new ApplicationException("wrong data");
                    }
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoShutdownTimeLimitTest(IISConfigUtility.AppPoolBitness appPoolBitness, int valueOfshutdownTimeLimit, int expectedClosingTime, bool isGraceFullShutdownEnabled)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoShutdownTimeLimitTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;

                    // Make shutdownDelay time with hard coded value such as 20 seconds and test vairious shutdonwTimeLimit, either less than 20 seconds or bigger then 20 seconds
                    int shutdownDelayTime = 20000;
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", valueOfshutdownTimeLimit);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", shutdownDelayTime.ToString() });
                    string expectedGracefulShutdownResponseStatusCode = "202";
                    if (!isGraceFullShutdownEnabled)
                    {
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "GracefulShutdown", "disabled" });
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestStartupClassName", "StartupWithShutdownDisabled" });
                        expectedGracefulShutdownResponseStatusCode = "200";
                    }
                    Thread.Sleep(1000);

                    await StartIISExpress(testSite);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));

                    // Set a new configuration value to make the backend process being recycled
                                        
                    DateTime startTime2 = DateTime.Now;

                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 100);
                    backendProcess.WaitForExit(30000);

                    DateTime endTime = DateTime.Now;
                    var difference = endTime - startTime2;

                    Thread.Sleep(500);

                    // Verify shutdown time
                    int tempExpectedClosingTime = expectedClosingTime;
                    int offSetSecond = 3;
                    if ((difference.Seconds < tempExpectedClosingTime + offSetSecond) == false)
                    {
                        if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                        {
                            // BugBug: ToDo: remove when the related issue is fixed
                            // Inprocess mode does not support shutdownTimeLimit
                            // So need to add 20 seconds for recycling worker process in inprocess mode
                            tempExpectedClosingTime += shutdownDelayTime / 1000;
                        }

                        if (tempExpectedClosingTime > 10)
                        {
                            // add 1 second to adjust expectedClosing time
                            tempExpectedClosingTime++;
                        }
                    }
                    Assert.True(difference.Seconds < tempExpectedClosingTime + offSetSecond, "Actual: " + difference.Seconds + ", Expected: " + tempExpectedClosingTime + 3);
                    Assert.True(difference.Seconds >= expectedClosingTime, "Actual: " + difference.Seconds + ", Expected: " + expectedClosingTime);

                    await StartIISExpress(testSite);

                    string newBackendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    Assert.True(backendProcessId != newBackendProcessId);

                    await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", numberOfRetryCount: 5);

                    // if expectedClosing time is less than the shutdownDelay time, gracefulshutdown is supposed to fail and failure event is expected
                    if (expectedClosingTime * 1000 + 1000 == shutdownDelayTime)
                    {
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownEvent(arg1, arg2), startTime, backendProcessId));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownEvent(arg1, arg2), startTime, expectedGracefulShutdownResponseStatusCode));
                    }
                    else
                    {
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownFailureEvent(arg1, arg2), startTime, backendProcessId));
                    }
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoShutdownTimeLimitAndAppOfflineTest(IISConfigUtility.AppPoolBitness appPoolBitness, int valueOfshutdownTimeLimit, int expectedClosingTime, bool isGraceFullShutdownEnabled)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoShutdownTimeLimitAndAppOfflineTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;

                    // Enable logging
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", true);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogFile", @".\stdout");
                    
                    // Make shutdownDelay time with hard coded value such as 10 seconds and test vairious shutdonwTimeLimit, either less than 10 seconds or bigger then 10 seconds
                    int shutdownDelayTime = (expectedClosingTime + 1) * 1000;
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", valueOfshutdownTimeLimit);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", shutdownDelayTime.ToString() });

                    string expectedGracefulShutdownResponseStatusCode = "202";
                    if (!isGraceFullShutdownEnabled)
                    {
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "GracefulShutdown", "disabled" });
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestStartupClassName", "StartupWithShutdownDisabled" });
                        expectedGracefulShutdownResponseStatusCode = "200";
                    }

                    await StartIISExpress(testSite);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    
                    // put app_offline.htm to make the backend process being recycled
                    string fileContent = "BackEndAppOffline";
                    testSite.AspNetCoreApp.CreateFile(new string[] { fileContent }, "App_Offline.Htm");

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId, restartIISExpres: false);
                    await StartIISExpress(testSite, verifyAppRunning: false);

                    Thread.Sleep(1000);
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), numberOfRetryCount: 5, expectedResponseBody: fileContent +"\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);
                    Thread.Sleep(1000);
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), numberOfRetryCount: 5, expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);
                    Thread.Sleep(1000);
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), numberOfRetryCount: 5, expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);

                    // remove app_offline
                    testSite.AspNetCoreApp.MoveFile("App_Offline.Htm", "_App_Offline.Htm");
                    Thread.Sleep(1000);
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), numberOfRetryCount: 5, expectedResponseBody: "Running");

                    backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                                        
                    // add back app_offline.htm
                    DateTime startTime2 = DateTime.Now;

                    testSite.AspNetCoreApp.MoveFile("_App_Offline.Htm", "App_Offline.Htm");

                    bool processExitOnTime = backendProcess.WaitForExit((expectedClosingTime + 5) * 1000);
                    
                    DateTime endTime = DateTime.Now;
                    var difference = endTime - startTime2;

                    Assert.True(processExitOnTime);

                    Thread.Sleep(500);

                    // Verify shutdown time
                    int tempExpectedClosingTime = expectedClosingTime;
                    int offSetSecond = 3;
                    if ((difference.Seconds < tempExpectedClosingTime + offSetSecond) == false)
                    {
                        if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                        {
                            // BugBug: ToDo: remove when the related issue is fixed
                            // Inprocess mode does not support shutdownTimeLimit
                            // So need to add 20 seconds for recycling worker process in inprocess mode
                            tempExpectedClosingTime += shutdownDelayTime / 1000;
                        }

                        if (tempExpectedClosingTime > 10)
                        {
                            // add 1 second to adjust expectedClosing time
                            tempExpectedClosingTime++;
                        }
                    }
                    Assert.True(difference.Seconds < tempExpectedClosingTime + offSetSecond, "Actual: " + difference.Seconds + ", Expected: " + tempExpectedClosingTime + 3);
                    Assert.True(difference.Seconds >= expectedClosingTime - 2, "Actual: " + difference.Seconds + ", Expected: " + expectedClosingTime);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId, restartIISExpres: false);
                    await StartIISExpress(testSite, verifyAppRunning:false);

                    await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);

                    string newBackendProcessId = "";

                    // remove app_offline
                    testSite.AspNetCoreApp.MoveFile("App_Offline.Htm", "_App_Offline.Htm");
                    Thread.Sleep(1000);

                    newBackendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    
                    Assert.True(backendProcessId != newBackendProcessId);
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), numberOfRetryCount: 5, expectedResponseBody: "Running");

                    // if expectedClosing time is less than the shutdownDelay time, gracefulshutdown is supposed to fail and failure event is expected
                    if (expectedClosingTime * 1000 + 1000 == shutdownDelayTime)
                    {
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownEvent(arg1, arg2), startTime, backendProcessId));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownEvent(arg1, arg2), startTime, expectedGracefulShutdownResponseStatusCode));
                    }
                    else
                    {
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMGracefulShutdownFailureEvent(arg1, arg2), startTime, backendProcessId));
                    }
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoStdoutLogEnabledTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoStdoutLogEnabledTest"))
            {
                testSite.AspNetCoreApp.DeleteDirectory("logs");
                string logPath = testSite.AspNetCoreApp.GetDirectoryPathWith("logs");
                Assert.False(Directory.Exists(logPath));

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(3000);

                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", true);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogFile", @".\logs\stdout");

                    await StartIISExpress(testSite);

                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    if (Directory.Exists(logPath))
                    {
                        bool fileLocked = false;
                        try
                        {
                            testSite.AspNetCoreApp.DeleteDirectory("logs");
                        }
                        catch
                        {
                            fileLocked = true;
                        }
                        Assert.True(fileLocked);
                        Assert.True(Directory.Exists(logPath));

                        // reset config to recyle app
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", false);
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", true);

                        await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId, restartIISExpres: false);

                        Thread.Sleep(2000);
                        startTime = DateTime.Now;
                        Thread.Sleep(1000);

                        // try deleting again
                        testSite.AspNetCoreApp.DeleteDirectory("logs");
                        Assert.False(Directory.Exists(logPath));

                        // create dummy file named logs
                        testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "logs");

                        await StartIISExpress(testSite);

                        backendProcessId = await GetAspnetCoreAppProcessId(testSite);

                        Thread.Sleep(2000);
                        testSite.AspNetCoreApp.DeleteFile("logs");

                        Assert.True(TestUtility.RetryHelper((arg1, arg2, arg3) => VerifyApplicationEventLog(arg1, arg2, arg3), 1004, startTime, @"stdoutLogFile"));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    }
                    else
                    {
                        //
                        // old behavior; this should be removed if we don't verify old ANCM version
                        //
                        Assert.False(Directory.Exists(logPath));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2, arg3) => VerifyApplicationEventLog(arg1, arg2, arg3), 1004, startTime, @"logs\stdout"));
                        Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                    }
                    testSite.AspNetCoreApp.CreateDirectory("logs");

                    // verify the log file is not created because backend process is not recycled
                    Assert.True(Directory.GetFiles(logPath).Length == 0);
                    Assert.True(backendProcessId == (await GetAspnetCoreAppProcessId(testSite)));

                    // reset web.config to recycle backend process and give write permission to the Users local group to which IIS workerprocess identity belongs
                    SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                    TestUtility.GiveWritePermissionTo(logPath, sid);

                    backendProcessId = await GetAspnetCoreAppProcessId(testSite);

                    startTime = DateTime.Now;
                    Thread.Sleep(500);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", false);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "stdoutLogEnabled", true);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);

                    Assert.True(backendProcessId != (await GetAspnetCoreAppProcessId(testSite)));

                    // Verify log file is created now after backend process is recycled
                    Assert.True(TestUtility.RetryHelper(p => { return Directory.GetFiles(p).Length > 0 ? true : false; }, logPath));

                    backendProcessId = await GetAspnetCoreAppProcessId(testSite);

                    // put app_offline and delete log directory
                    testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "App_Offline.Htm");
                    Thread.Sleep(1000);

                    if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess && testSite.IisServerType == ServerType.IIS)
                    {
                        // in order to delete Logs directory, worker process should be gone in inprocess mode
                        iisConfig.RecycleAppPool(testSite.AspNetCoreApp.AppPoolName);
                    }

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId, restartIISExpres: false);

                    testSite.AspNetCoreApp.DeleteDirectory("logs");
                    Assert.False(Directory.Exists(logPath));
                }

                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoProcessPathAndArgumentsTest(IISConfigUtility.AppPoolBitness appPoolBitness, string processPath, string argumentsPrefix)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoProcessPathAndArgumentsTest", copyAllPublishedFiles: true))
            {
                await StartIISExpress(testSite);

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string arguments = argumentsPrefix + testSite.AspNetCoreApp.GetArgumentFileName();
                    string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    var tempBackendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));

                    // replace $env with the actual test value
                    if (processPath == "$env")
                    {
                        string tempString = Environment.ExpandEnvironmentVariables("%systemdrive%").ToLower();
                        if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                        {
                            if (appPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
                            {
                                processPath = @"%ProgramFiles(x86)%\dotnet\dotnet.exe";
                            }
                            else
                            {
                                processPath = @"%ProgramFiles%\dotnet\dotnet.exe";
                            }
                        }
                        else
                        {
                            processPath = Path.Combine(tempBackendProcess.MainModule.FileName).ToLower().Replace(tempString, "%systemdrive%");
                        }
                        arguments = testSite.AspNetCoreApp.GetDirectoryPathWith(arguments).ToLower().Replace(tempString, "%systemdrive%");
                    }

                    DateTime startTime = DateTime.Now;
                    Thread.Sleep(500);

                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "processPath", processPath);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "arguments", arguments);

                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);

                    Thread.Sleep(500);
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();
                    Thread.Sleep(500);

                    backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                    Assert.True(TestUtility.RetryHelper((arg1, arg2) => VerifyANCMStartEvent(arg1, arg2), startTime, backendProcessId));
                }

                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoForwardWindowsAuthTokenTest(IISConfigUtility.AppPoolBitness appPoolBitness, bool enabledForwardWindowsAuthToken)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoForwardWindowsAuthTokenTest"))
            {
                if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                {
                    TestUtility.LogInformation("This test is not valid for Inprocess mode");
                    return;
                }

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string responseBody = string.Empty;
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "forwardWindowsAuthToken", enabledForwardWindowsAuthToken);
                    string requestHeaders = (await SendReceive(testSite.AspNetCoreApp.GetUri("DumpRequestHeaders"))).ResponseBody;
                    Assert.DoesNotContain("MS-ASPNETCORE-WINAUTHTOKEN", requestHeaders, StringComparison.InvariantCultureIgnoreCase);

                    iisConfig.EnableIISAuthentication(testSite.SiteName, windows: true, basic: false, anonymous: false);
                    
                    await StartIISExpress(testSite);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();
                    Thread.Sleep(500);

                    requestHeaders = (await SendReceive(testSite.AspNetCoreApp.GetUri("DumpRequestHeaders"))).ResponseBody;
                    if (enabledForwardWindowsAuthToken)
                    {
                        Assert.Contains("MS-ASPNETCORE-WINAUTHTOKEN", requestHeaders.ToUpper());

                        responseBody = (await SendReceive(testSite.AspNetCoreApp.GetUri("ImpersonateMiddleware"))).ResponseBody;
                        bool compare = false;

                        string expectedValue1 = "ImpersonateMiddleware-UserName = " + Environment.ExpandEnvironmentVariables("%USERDOMAIN%") + "\\" + Environment.ExpandEnvironmentVariables("%USERNAME%");
                        if (responseBody.ToLower().Contains(expectedValue1.ToLower()))
                        {
                            compare = true;
                        }

                        string expectedValue2 = "ImpersonateMiddleware-UserName = " + Environment.ExpandEnvironmentVariables("%USERNAME%");
                        if (responseBody.ToLower().Contains(expectedValue2.ToLower()))
                        {
                            compare = true;
                        }

                        Assert.True(compare);
                    }
                    else
                    {
                        Assert.DoesNotContain("MS-ASPNETCORE-WINAUTHTOKEN", requestHeaders, StringComparison.InvariantCultureIgnoreCase);

                        responseBody = (await SendReceive(testSite.AspNetCoreApp.GetUri("ImpersonateMiddleware"))).ResponseBody;
                        Assert.Contains("ImpersonateMiddleware-UserName = NoAuthentication", responseBody);
                    }
                }

                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoRecylingAppPoolTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoRecylingAppPoolTest"))
            {
                if (testSite.IisServerType == ServerType.IISExpress)
                {
                    TestUtility.LogInformation("This test is not valid for IISExpress server type");
                    return;
                }

                await StartIISExpress(testSite);

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    // allocating 128,000 KB
                    await SendReceive(testSite.AspNetCoreApp.GetUri("MemoryLeak128000"));

                    // get backend process id
                    string pocessIdBackendProcess = await GetAspnetCoreAppProcessId(testSite);

                    // get process id of IIS worker process (w3wp.exe)
                    string userName = testSite.RootAppContext.AppPoolName;
                    int processIdOfWorkerProcess = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));
                    var workerProcess = Process.GetProcessById(Convert.ToInt32(processIdOfWorkerProcess));
                    var backendProcess = Process.GetProcessById(Convert.ToInt32(pocessIdBackendProcess));

                    var privateMemoryKB = workerProcess.PrivateMemorySize64 / 1024;
                    var virtualMemoryKB = workerProcess.VirtualMemorySize64 / 1024;
                    var privateMemoryKBBackend = backendProcess.PrivateMemorySize64 / 1024;
                    var virtualMemoryKBBackend = backendProcess.VirtualMemorySize64 / 1024;
                    var totalPrivateMemoryKB = privateMemoryKB + privateMemoryKBBackend;
                    var totalVirtualMemoryKB = virtualMemoryKB + virtualMemoryKBBackend;

                    // terminate IIS worker process
                    if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess && testSite.IisServerType == ServerType.IIS)
                    {
                        iisConfig.RecycleAppPool(testSite.AspNetCoreApp.AppPoolName);
                        await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, pocessIdBackendProcess);
                    }
                    else
                    {
                        // terminate backend process
                        backendProcess.Kill();
                        backendProcess.Dispose();

                        workerProcess.Kill();
                        workerProcess.Dispose();
                    }
                    
                    Thread.Sleep(3000);

                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    iisConfig.SetAppPoolSetting(testSite.AspNetCoreApp.AppPoolName, "privateMemory", totalPrivateMemoryKB);

                    // set 100 for rapidFailProtection counter for both IIS worker process and aspnetcore backend process
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "rapidFailsPerMinute", 100);
                    Thread.Sleep(3000);

                    await SendReceive(testSite.RootAppContext.GetUri("small.htm"));
                    Thread.Sleep(1000);
                    int x = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));

                    // Verify that IIS recycling does not happen while there is no memory leak
                    bool foundVSJit = false;
                    for (int i = 0; i < 10; i++)
                    {
                        // check JitDebugger before continuing 
                        foundVSJit = CleanupVSJitDebuggerWindow();

                        await SendReceive(testSite.RootAppContext.GetUri("small.htm"));
                        Thread.Sleep(3000);
                    }

                    int y = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));
                    Assert.True(x == y && !foundVSJit, "worker process is not recycled after 30 seconds");

                    string backupPocessIdBackendProcess = await GetAspnetCoreAppProcessId(testSite);
                    string newPocessIdBackendProcess = backupPocessIdBackendProcess;

                    // Verify IIS recycling happens while there is memory leak
                    for (int i = 0; i < 10; i++)
                    {
                        // check JitDebugger before continuing 
                        foundVSJit = CleanupVSJitDebuggerWindow();

                        // allocating 256,000 KB
                        await SendReceive(testSite.AspNetCoreApp.GetUri("MemoryLeak256000"));

                        newPocessIdBackendProcess = await GetAspnetCoreAppProcessId(testSite);
                        if (foundVSJit || backupPocessIdBackendProcess != newPocessIdBackendProcess)
                        {
                            // worker process is recycled expectedly and backend process is recycled together
                            break;
                        }
                        Thread.Sleep(3000);
                    }
                    // check JitDebugger before continuing 
                    CleanupVSJitDebuggerWindow();

                    int z = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        z = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));
                        if (x != z)
                        {
                            break;
                        }
                        else
                        {
                            Thread.Sleep(1000);
                        }
                    }
                    z = Convert.ToInt32(TestUtility.GetProcessWMIAttributeValue("w3wp.exe", "Handle", userName));
                    Assert.True(x != z, "worker process is recycled");

                    newPocessIdBackendProcess = await GetAspnetCoreAppProcessId(testSite);
                    Assert.True(backupPocessIdBackendProcess != newPocessIdBackendProcess, "backend process is recycled");
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoCompressionTest(IISConfigUtility.AppPoolBitness appPoolBitness, bool useCompressionMiddleWare, bool enableIISCompression)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoCompressionTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string startupClass = "StartupCompressionCaching";
                    if (!useCompressionMiddleWare)
                    {
                        startupClass = "StartupNoCompressionCaching";
                    }

                    // set startup class
                    iisConfig.SetANCMConfig(
                        testSite.SiteName,
                        testSite.AspNetCoreApp.Name,
                        "environmentVariable",
                        new string[] { "ANCMTestStartupClassName", startupClass }
                        );

                    // enable or IIS compression
                    // Note: IIS compression, however, will be ignored if AspnetCore compression middleware is enabled.
                    iisConfig.SetCompression(testSite.SiteName, enableIISCompression);

                    // prepare static contents
                    testSite.AspNetCoreApp.CreateDirectory("wwwroot");
                    testSite.AspNetCoreApp.CreateDirectory(@"wwwroot\pdir");

                    testSite.AspNetCoreApp.CreateFile(new string[] { "foohtm" }, @"wwwroot\foo.htm");
                    testSite.AspNetCoreApp.CreateFile(new string[] { "barhtm" }, @"wwwroot\pdir\bar.htm");
                    testSite.AspNetCoreApp.CreateFile(new string[] { "defaulthtm" }, @"wwwroot\default.htm");

                    await StartIISExpress(testSite);

                    Thread.Sleep(500);

                    SendReceiveContext result = null;
                    if (!useCompressionMiddleWare && !enableIISCompression)
                    {
                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("foo.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("foohtm"), "verify response body");
                        Assert.False(result.ResponseHeader.Contains("Content-Encoding"), "verify response header");

                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("pdir/bar.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("barhtm"), "verify response body");
                        Assert.False(result.ResponseHeader.Contains("Content-Encoding"), "verify response header");

                        result = await SendReceive(testSite.AspNetCoreApp.GetUri(), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("defaulthtm"), "verify response body");
                        Assert.False(result.ResponseHeader.Contains("Content-Encoding"), "verify response header");
                    }
                    else
                    {
                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("foo.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("foohtm"), "verify response body");
                        Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));

                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("pdir/bar.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("barhtm"), "verify response body");
                        Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));

                        result = await SendReceive(testSite.AspNetCoreApp.GetUri(), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("defaulthtm"), "verify response body");
                        Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));
                    }
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoCachingTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoCachingTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string startupClass = "StartupCompressionCaching";

                    // set startup class
                    iisConfig.SetANCMConfig(
                        testSite.SiteName,
                        testSite.AspNetCoreApp.Name,
                        "environmentVariable",
                        new string[] { "ANCMTestStartupClassName", startupClass }
                        );

                    // enable IIS compression
                    // Note: IIS compression, however, will be ignored if AspnetCore compression middleware is enabled.
                    iisConfig.SetCompression(testSite.SiteName, true);

                    // prepare static contents
                    testSite.AspNetCoreApp.CreateDirectory("wwwroot");
                    testSite.AspNetCoreApp.CreateDirectory(@"wwwroot\pdir");

                    testSite.AspNetCoreApp.CreateFile(new string[] { "foohtm" }, @"wwwroot\foo.htm");
                    testSite.AspNetCoreApp.CreateFile(new string[] { "barhtm" }, @"wwwroot\pdir\bar.htm");
                    testSite.AspNetCoreApp.CreateFile(new string[] { "defaulthtm" }, @"wwwroot\default.htm");

                    await StartIISExpress(testSite);

                    const int retryCount = 3;
                    string headerValue = string.Empty;
                    string headerValue2 = string.Empty;
                    SendReceiveContext result = null;
                    for (int i = 0; i < retryCount; i++)
                    {
                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("foo.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        headerValue = GetHeaderValue(result.ResponseHeader, "MyCustomHeader");
                        Assert.True(result.ResponseBody.Contains("foohtm"), "verify response body");
                        Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));
                        Thread.Sleep(1500);

                        result = await SendReceive(testSite.AspNetCoreApp.GetUri("foo.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        headerValue2 = GetHeaderValue(result.ResponseHeader, "MyCustomHeader");
                        Assert.True(result.ResponseBody.Contains("foohtm"), "verify response body");
                        Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));
                        if (headerValue == headerValue2)
                        {
                            break;
                        }
                    }
                    Assert.Equal(headerValue, headerValue2);

                    Thread.Sleep(12000);
                    result = await SendReceive(testSite.AspNetCoreApp.GetUri("foo.htm"), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                    Assert.True(result.ResponseBody.Contains("foohtm"), "verify response body");
                    Assert.Equal("gzip", GetHeaderValue(result.ResponseHeader, "Content-Encoding"));
                    string headerValue3 = GetHeaderValue(result.ResponseHeader, "MyCustomHeader");
                    Assert.NotEqual(headerValue2, headerValue3);
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoSendHTTPSRequestTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoSendHTTPSRequestTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string hostName = "";
                    string subjectName = "localhost";
                    string ipAddress = "*";
                    string hexIPAddress = "0x00";
                    int sslPort = InitializeTestMachine.SiteId + 6300;

                    // Add https binding and get https uri information
                    iisConfig.AddBindingToSite(testSite.SiteName, ipAddress, sslPort, hostName, "https");

                    // Create a self signed certificate
                    string thumbPrint = iisConfig.CreateSelfSignedCertificate(subjectName);

                    // Export the self signed certificate to rootCA
                    iisConfig.ExportCertificateTo(thumbPrint, sslStoreTo: @"Cert:\LocalMachine\Root");

                    // Configure http.sys ssl certificate mapping to IP:Port endpoint with the newly created self signed certificage
                    iisConfig.SetSSLCertificate(sslPort, hexIPAddress, thumbPrint);

                    await StartIISExpress(testSite);

                    // Verify http request
                    var result = await SendReceive(testSite.AspNetCoreApp.GetUri(), requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                    Assert.True(result.ResponseBody.Contains("Running"), "verify response body");

                    // Verify https request
                    Uri targetHttpsUri = testSite.AspNetCoreApp.GetUri(null, sslPort, protocol: "https");
                    result = await SendReceive(targetHttpsUri, requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                    Assert.True(result.ResponseBody.Contains("Running"), "verify response body");

                    // Remove the SSL Certificate mapping
                    iisConfig.RemoveSSLCertificate(sslPort, hexIPAddress);

                    // Remove the newly created self signed certificate
                    iisConfig.DeleteCertificate(thumbPrint);

                    // Remove the exported self signed certificate on rootCA
                    iisConfig.DeleteCertificate(thumbPrint, @"Cert:\LocalMachine\Root");
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoFilterOutMSRequestHeadersTest(IISConfigUtility.AppPoolBitness appPoolBitness, string requestHeader, string requestHeaderValue)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoSendHTTPSRequestTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string hostName = "";
                    string subjectName = "localhost";
                    string ipAddress = "*";
                    string hexIPAddress = "0x00";
                    int sslPort = InitializeTestMachine.SiteId + 6300;

                    // Add https binding and get https uri information
                    iisConfig.AddBindingToSite(testSite.SiteName, ipAddress, sslPort, hostName, "https");

                    // Create a self signed certificate
                    string thumbPrint = iisConfig.CreateSelfSignedCertificate(subjectName);

                    // Export the self signed certificate to rootCA
                    iisConfig.ExportCertificateTo(thumbPrint, sslStoreTo: @"Cert:\LocalMachine\Root");

                    // Configure http.sys ssl certificate mapping to IP:Port endpoint with the newly created self signed certificage
                    iisConfig.SetSSLCertificate(sslPort, hexIPAddress, thumbPrint);

                    await StartIISExpress(testSite);

                    // Verify http request
                    var result = await SendReceive(testSite.AspNetCoreApp.GetUri("DumpRequestHeaders"), requestHeaders: new string[] { "Accept-Encoding", "gzip", requestHeader, requestHeaderValue });
                    string requestHeaders = result.ResponseHeader.Replace(" ", "");
                    Assert.DoesNotContain(requestHeader + ":", requestHeaders, StringComparison.InvariantCultureIgnoreCase);

                    // Verify https request
                    Uri targetHttpsUri = testSite.AspNetCoreApp.GetUri(null, sslPort, protocol: "https");
                    result = await SendReceive(targetHttpsUri, requestHeaders: new string[] { "Accept-Encoding", "gzip", requestHeader, requestHeaderValue });
                    requestHeaders = result.ResponseHeader.Replace(" ", "");
                    Assert.DoesNotContain(requestHeader + ":", requestHeaders, StringComparison.InvariantCultureIgnoreCase);

                    // Remove the SSL Certificate mapping
                    iisConfig.RemoveSSLCertificate(sslPort, hexIPAddress);

                    // Remove the newly created self signed certificate
                    iisConfig.DeleteCertificate(thumbPrint);

                    // Remove the exported self signed certificate on rootCA
                    iisConfig.DeleteCertificate(thumbPrint, @"Cert:\LocalMachine\Root");
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoClientCertificateMappingTest(IISConfigUtility.AppPoolBitness appPoolBitness, bool useHTTPSMiddleWare)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoClientCertificateMappingTest"))
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    string hostName = "";
                    string rootCN = "ANCMTest" + testSite.PostFix;
                    string webServerCN = "localhost";
                    string kestrelServerCN = "localhost";
                    string clientCN = "ANCMClient-" + testSite.PostFix;

                    string ipAddress = "*";
                    string hexIPAddress = "0x00";
                    int sslPort = InitializeTestMachine.SiteId + 6300;

                    // Add https binding and get https uri information
                    iisConfig.AddBindingToSite(testSite.SiteName, ipAddress, sslPort, hostName, "https");
                    
                    // Create a root certificate
                    string thumbPrintForRoot = iisConfig.CreateSelfSignedCertificateWithMakeCert(rootCN);

                    // Create a certificate for web server setting its issuer with the root certificate subject name
                    string thumbPrintForWebServer = iisConfig.CreateSelfSignedCertificateWithMakeCert(webServerCN, rootCN, extendedKeyUsage: "1.3.6.1.5.5.7.3.1");
                    string thumbPrintForKestrel = null;

                    // Create a certificate for client authentication setting its issuer with the root certificate subject name
                    string thumbPrintForClientAuthentication = iisConfig.CreateSelfSignedCertificateWithMakeCert(clientCN, rootCN, extendedKeyUsage: "1.3.6.1.5.5.7.3.2");

                    // Configure http.sys ssl certificate mapping to IP:Port endpoint with the newly created self signed certificage
                    iisConfig.SetSSLCertificate(sslPort, hexIPAddress, thumbPrintForWebServer);

                    // Create a new local administrator user
                    string userName = "tempuser" + TestUtility.RandomString(5);
                    string password = "AncmTest123!";
                    string temp;
                    temp = TestUtility.RunPowershellScript("net localgroup IIS_IUSRS /Delete " + userName);
                    temp = TestUtility.RunPowershellScript("net user " + userName + " /Delete");
                    temp = TestUtility.RunPowershellScript("net user " + userName + " " + password + " /ADD");
                    temp = TestUtility.RunPowershellScript("net localgroup IIS_IUSRS /Add " + userName);

                    // Get public key of the client certificate and Configure OnetToOneClientCertificateMapping the public key and disable anonymous authentication and set SSL flags for Client certificate authentication
                    string publicKey = iisConfig.GetCertificatePublicKey(thumbPrintForClientAuthentication, @"Cert:\CurrentUser\My");

                    bool setPasswordSeperately = false;
                    if (testSite.IisServerType == ServerType.IISExpress)
                    {
                        setPasswordSeperately = true;
                        iisConfig.EnableOneToOneClientCertificateMapping(testSite.SiteName, ".\\" + userName, null, publicKey);
                    }
                    else
                    {
                        iisConfig.EnableOneToOneClientCertificateMapping(testSite.SiteName, ".\\" + userName, password, publicKey);
                    }

                    // IISExpress uses a differnt encryption from full IIS version's and it is not easy to override the encryption methong with MWA. 
                    // As a work-around, password is set with updating the config file directly.
                    if (setPasswordSeperately)
                    {
                        // Search userName property and replace it with userName + password
                        string text = File.ReadAllText(testSite.IisExpressConfigPath);
                        text = text.Replace(userName + "\"", userName + "\"" + " " + "password=" + "\"" + password + "\"");
                        File.WriteAllText(testSite.IisExpressConfigPath, text);
                    }

                    // Configure kestrel SSL test environment
                    if (useHTTPSMiddleWare)
                    {
                        // set startup class
                        string startupClass = "StartupHTTPS";
                        iisConfig.SetANCMConfig(
                            testSite.SiteName,
                            testSite.AspNetCoreApp.Name,
                            "environmentVariable",
                            new string[] { "ANCMTestStartupClassName", startupClass }
                            );

                        // Create a certificate for Kestrel web server and export to TestResources\testcert.pfx
                        // NOTE: directory name "TestResources", file name "testcert.pfx" and password "testPassword" should be matched to AspnetCoreModule.TestSites.Standard web application
                        thumbPrintForKestrel = iisConfig.CreateSelfSignedCertificateWithMakeCert(kestrelServerCN, rootCN, extendedKeyUsage: "1.3.6.1.5.5.7.3.1");
                        testSite.AspNetCoreApp.CreateDirectory("TestResources");
                        string pfxFilePath = Path.Combine(testSite.AspNetCoreApp.GetDirectoryPathWith("TestResources"), "testcert.pfx");
                        iisConfig.ExportCertificateTo(thumbPrintForKestrel, sslStoreFrom: "Cert:\\LocalMachine\\My", sslStoreTo: pfxFilePath, pfxPassword: "testPassword");
                        Assert.True(File.Exists(pfxFilePath));
                    }

                    await StartIISExpress(testSite);

                    Uri rootHttpsUri = testSite.RootAppContext.GetUri(null, sslPort, protocol: "https");
                    TestUtility.RunPowershellScript("( invoke-webrequest " + rootHttpsUri.OriginalString + " -CertificateThumbprint " + thumbPrintForClientAuthentication + ").StatusCode", "200");

                    // Verify http request with using client certificate
                    Uri targetHttpsUri = testSite.AspNetCoreApp.GetUri(null, sslPort, protocol: "https");
                    string statusCode = TestUtility.RunPowershellScript("( invoke-webrequest " + targetHttpsUri.OriginalString + " -CertificateThumbprint " + thumbPrintForClientAuthentication + ").StatusCode");
                    Assert.Equal("200", statusCode);

                    // Verify https request with client certificate includes the certificate header "MS-ASPNETCORE-CLIENTCERT"
                    Uri targetHttpsUriForDumpRequestHeaders = testSite.AspNetCoreApp.GetUri("DumpRequestHeaders", sslPort, protocol: "https");
                    string outputRawContent = TestUtility.RunPowershellScript("( invoke-webrequest " + targetHttpsUriForDumpRequestHeaders.OriginalString + " -CertificateThumbprint " + thumbPrintForClientAuthentication + ").RawContent.ToString()");

                    // in inprocess mode, there is no additinal request header
                    if (testSite.AspNetCoreApp.HostingModel != TestWebApplication.HostingModelValue.Inprocess)
                    {
                        Assert.Contains("MS-ASPNETCORE-CLIENTCERT", outputRawContent);
                    
                        // Get the value of MS-ASPNETCORE-CLIENTCERT request header again and verify it is matched to its configured public key
                        Uri targetHttpsUriForCLIENTCERTRequestHeader = testSite.AspNetCoreApp.GetUri("GetRequestHeaderValueMS-ASPNETCORE-CLIENTCERT", sslPort, protocol: "https");
                        outputRawContent = TestUtility.RunPowershellScript("( invoke-webrequest " + targetHttpsUriForCLIENTCERTRequestHeader.OriginalString + " -CertificateThumbprint " + thumbPrintForClientAuthentication + ").RawContent.ToString()");
                        Assert.Contains(publicKey, outputRawContent);
                    }

                    // Verify non-https request returns 403.4 error
                    var result = await SendReceive(testSite.AspNetCoreApp.GetUri(), requestHeaders: new string[] { "Accept-Encoding", "gzip" }, expectedResponseStatus: HttpStatusCode.Forbidden);
                    Assert.Contains("403.4", result.ResponseBody);

                    // Verify https request without using client certificate returns 403.7
                    result = await SendReceive(targetHttpsUri, requestHeaders: new string[] { "Accept-Encoding", "gzip" }, expectedResponseStatus: HttpStatusCode.Forbidden);
                    Assert.Contains("403.7", result.ResponseBody);

                    // Clean up user
                    temp = TestUtility.RunPowershellScript("net localgroup IIS_IUSRS /Delete " + userName);
                    temp = TestUtility.RunPowershellScript("net user " + userName + " /Delete");

                    // Remove the SSL Certificate mapping
                    iisConfig.RemoveSSLCertificate(sslPort, hexIPAddress);

                    // Clean up certificates
                    iisConfig.DeleteCertificate(thumbPrintForRoot, @"Cert:\LocalMachine\Root");
                    iisConfig.DeleteCertificate(thumbPrintForWebServer, @"Cert:\LocalMachine\My");
                    if (useHTTPSMiddleWare)
                    {
                        iisConfig.DeleteCertificate(thumbPrintForKestrel, @"Cert:\LocalMachine\My");
                    }
                    iisConfig.DeleteCertificate(thumbPrintForClientAuthentication, @"Cert:\CurrentUser\My");
                }
                testSite.AspNetCoreApp.RestoreFile("web.config");
            }
        }

        public static async Task DoWebSocketTest(IISConfigUtility.AppPoolBitness appPoolBitness, string testData)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoWebSocketTest"))
            {
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 10);
                }

                await StartIISExpress(testSite);

                DateTime startTime = DateTime.Now;

                // Get Process ID
                string backendProcessId_old = await GetAspnetCoreAppProcessId(testSite);

                //echo.aspx has hard coded path for the websocket server; commented out until the hard - coded path issue is fixed
                /* 
                // Verify WebSocket without setting subprotocol
                await SendReceive(testSite.WebSocketApp.GetUri("echo.aspx"), expectedStringsInResponseBody: new string[] { "Socket Open" }); 

                // Verify WebSocket subprotocol
                await SendReceive(testSite.WebSocketApp.GetUri("echoSubProtocol.aspx"), expectedStringsInResponseBody: new string[] { "Socket Open", "mywebsocketsubprotocol" }); // echoSubProtocol.aspx has hard coded path for the websocket server
                */

                // Verify websocket 
                using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                {
                    var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                    Assert.Contains("Connection: Upgrade", frameReturned.Content);
                    Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                    Thread.Sleep(500);

                    VerifySendingWebSocketData(websocketClient, testData);
                    Thread.Sleep(500);

                    frameReturned = websocketClient.Close();
                    Thread.Sleep(500);

                    Assert.True(frameReturned.FrameType == FrameType.Close, "Closing Handshake");
                }

                // send a simple request and verify the response body
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running");

                Thread.Sleep(500);
                string backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                Assert.Equal(backendProcessId_old, backendProcessId);

                // Verify server side websocket disconnection
                using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                {
                    for (int jj = 0; jj < 3; jj++)
                    {
                        var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                        Assert.Contains("Connection: Upgrade", frameReturned.Content);
                        Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                        Thread.Sleep(500);

                        Assert.True(websocketClient.IsOpened, "Check active connection before starting");

                        // Send a special string to initiate the server side connection closing
                        websocketClient.SendTextData("CloseFromServer");
                        bool connectionClosedFromServer = websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed);

                        // Verify server side connection closing is done successfully
                        Assert.True(connectionClosedFromServer, "Closing Handshake initiated from Server");

                        // extract text data from the last frame, which is the close frame
                        int lastIndex = websocketClient.Connection.DataReceived.Count - 1;

                        // Verify text data is matched to the string sent by server
                        Assert.Contains("ClosingFromServer", websocketClient.Connection.DataReceived[lastIndex].TextData);
                    }
                }

                // send a simple request and verify the response body
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running");

                Thread.Sleep(500);
                backendProcessId = await GetAspnetCoreAppProcessId(testSite);
                Assert.Equal(backendProcessId_old, backendProcessId);

                // send a simple request and verify the response body
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running");
            }
        }

        public static async Task DoWebSocketAppOfflineTest(IISConfigUtility.AppPoolBitness appPoolBitness, string testData)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoWebSocketAppOfflineTest"))
            {
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 10);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", "3000" });
                }

                await StartIISExpress(testSite);

                DateTime startTime = DateTime.Now;

                // Get Process ID
                Thread.Sleep(500);

                // Verify websocket with app_offline.htm
                using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                {
                    //int failureCount = 0;
                    string fileContent = "WebSocketApp_offline";
                    for (int jj = 0; jj < 5; jj++)
                    {
                        testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                        Thread.Sleep(1000);

                        await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseBody: "Running", numberOfRetryCount: 10);
                        string recycledProcessId = await GetAspnetCoreAppProcessId(testSite);

                        var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                        Assert.Contains("Connection: Upgrade", frameReturned.Content);
                        Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                        Thread.Sleep(500);

                        VerifySendingWebSocketData(websocketClient, testData);
                        Thread.Sleep(500);

                        // put app_offline
                        testSite.AspNetCoreApp.CreateFile(new string[] { fileContent }, "App_Offline.Htm");

                        bool connectionClosedFromServer = websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed);
                        Assert.True(connectionClosedFromServer, "Closing Handshake initiated from Server");

                        // extract text data from the last frame, which is the close frame
                        int lastIndex = websocketClient.Connection.DataReceived.Count - 1;

                        // Verify text data is matched to the string sent by server
                        Assert.Contains("ClosingFromServer", websocketClient.Connection.DataReceived[lastIndex].TextData);
                        
                        // Verify the application file can be removed under app_offline mode
                        testSite.AspNetCoreApp.BackupFile(appDllFileName);
                        testSite.AspNetCoreApp.DeleteFile(appDllFileName);
                        testSite.AspNetCoreApp.RestoreFile(appDllFileName);

                        await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, recycledProcessId, restartIISExpres: false);
                        await StartIISExpress(testSite, expectedResponseStatus: HttpStatusCode.InternalServerError, expectedResponseBody: fileContent);

                        // verify app_offline.htm
                        await SendReceive(testSite.RootAppContext.GetUri(), expectedResponseBody: fileContent + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable);
                    }

                    //Assert.True(failureCount < 2, "Failure count : " + failureCount);
                }

                // remove app_offline.htm
                testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                Thread.Sleep(1000);

                // send a simple request and verify the response body
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running");
            }
        }

        public static async Task DoWebSocketRecycledWithConfigChangeTest(IISConfigUtility.AppPoolBitness appPoolBitness, string testData)
        {
            using (var testSite = new TestWebSite(appPoolBitness, "DoWebSocketRecycledWithConfigChangeTest"))
            {
                string appDllFileName = testSite.AspNetCoreApp.GetArgumentFileName();

                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 10);
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", "3000" });
                }

                await StartIISExpress(testSite);

                // Verify websocket with configuration change notification
                using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                {
                    for (int jj = 0; jj < 3; jj++)
                    {
                        await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseBody: "Running", numberOfRetryCount: 10);

                        string recycledProcessId = await GetAspnetCoreAppProcessId(testSite);

                        var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                        Assert.Contains("Connection: Upgrade", frameReturned.Content);
                        Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                        Thread.Sleep(500);

                        VerifySendingWebSocketData(websocketClient, testData);
                        Thread.Sleep(1000);

                        using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                        {
                            iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 11 + jj);
                        }
                        
                        bool connectionClosedFromServer = websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed); 

                        // Verify server side connection closing is done successfully
                        Assert.True(connectionClosedFromServer, "Closing Handshake initiated from Server");

                        // extract text data from the last frame, which is the close frame
                        int lastIndex = websocketClient.Connection.DataReceived.Count - 1;

                        // Verify text data is matched to the string sent by server
                        Assert.Contains("ClosingFromServer", websocketClient.Connection.DataReceived[lastIndex].TextData);

                        await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, recycledProcessId);

                        Thread.Sleep(1000);
                        await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseBody: "Running", numberOfRetryCount: 10);
                    }
                }
            }
        }
        
        public static async Task DoWebSocketErrorhandlingTest(IISConfigUtility.AppPoolBitness appPoolBitness)
        {
            Exception saved_ex = null;
            try
            {
                using (var testSite = new TestWebSite(appPoolBitness, "DoWebSocketErrorhandlingTest"))
                {
                    // Verify websocket returns 404 when websocket module is not registered
                    using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                    {
                        // Remove websocketModule
                        IISConfigUtility.BackupAppHostConfig("DoWebSocketErrorhandlingTest", true);
                        iisConfig.RemoveModule("WebSocketModule");

                        await StartIISExpress(testSite);

                        Thread.Sleep(500);
                        using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                        {
                            var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true, waitForConnectionOpen:false);
                            Assert.DoesNotContain("Connection: Upgrade", frameReturned.Content, StringComparison.InvariantCultureIgnoreCase);

                            //BugBug: Currently we returns 101 here.
                            //Assert.DoesNotContain("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                        }
                    }

                    // send a simple request again and verify the response body
                    await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running");
                }
            }
            catch (Exception ex)
            {
                saved_ex = ex;
            }

            Assert.Null(saved_ex);

            // roback configuration 
            IISConfigUtility.RestoreAppHostConfig("DoWebSocketErrorhandlingTest", true);

            // check JitDebugger before continuing 
            Thread.Sleep(1000);
            CleanupVSJitDebuggerWindow(@"https://github.com/aspnet/IISIntegration/issues/662");

        }

        public enum DoAppVerifierTest_ShutDownMode
        {
            RecycleAppPool,
            CreateAppOfflineHtm,
            StopAndStartAppPool,
            RestartW3SVC,
            ConfigurationChangeNotification
        }

        public enum DoAppVerifierTest_StartUpMode
        {
            UseGracefulShutdown,
            DontUseGracefulShutdown
        }

        public static async Task DoAppVerifierTest(IISConfigUtility.AppPoolBitness appPoolBitness, bool verifyTimeout, DoAppVerifierTest_StartUpMode startUpMode, DoAppVerifierTest_ShutDownMode shutDownMode, int repeatCount = 2, bool enableAppVerifier = true)
        {
            TestWebSite testSite = null;
            bool testResult = false;

            testSite = new TestWebSite(appPoolBitness, "DoAppVerifierTest");
            if (testSite.IisServerType == ServerType.IISExpress)
            {
                TestUtility.LogInformation("This test is not valid for IISExpress server type because of IISExpress bug; Once it is resolved, we should activate this test for IISExpress as well");
                return;
            }

            if (enableAppVerifier)
            {
                // enable AppVerifier 
                testSite.AttachAppverifier();
            }

            // add try finally module here to cleanup Appverifier incase testing fialed in run. 
            try
            {
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    // Prepare https binding
                    string hostName = "";
                    string subjectName = "localhost";
                    string ipAddress = "*";
                    string hexIPAddress = "0x00";
                    int sslPort = InitializeTestMachine.SiteId + 6300;

                    // Add https binding and get https uri information
                    iisConfig.AddBindingToSite(testSite.SiteName, ipAddress, sslPort, hostName, "https");

                    // Create a self signed certificate
                    string thumbPrint = iisConfig.CreateSelfSignedCertificate(subjectName);

                    // Export the self signed certificate to rootCA
                    iisConfig.ExportCertificateTo(thumbPrint, sslStoreTo: @"Cert:\LocalMachine\Root");

                    // Configure http.sys ssl certificate mapping to IP:Port endpoint with the newly created self signed certificage
                    iisConfig.SetSSLCertificate(sslPort, hexIPAddress, thumbPrint);

                    // Set shutdownTimeLimit with 3 seconds and use 5 seconds for delay time to make the shutdownTimeout happen
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", 3);

                    int timeoutValue = 3;
                    if (verifyTimeout)
                    {
                        // set requestTimeout
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "requestTimeout", TimeSpan.Parse("00:01:00")); // 1 minute

                        // set startupTimeout
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "startupTimeLimit", timeoutValue);

                        // Set shutdownTimeLimit with 3 seconds and use 5 seconds for delay time to make the shutdownTimeout happen
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", timeoutValue);
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", "10" });
                    }

                    await StartIISExpress(testSite);

                    if (verifyTimeout)
                    {
                        Thread.Sleep(500);

                        // initial request which requires more than startup timeout should fails
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep5000"), HttpStatusCode.BadGateway, timeout: 10);
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep5000"), expectedResponseBody: "Running", timeout: 10);

                        // request which requires more than request timeout should fails
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep65000"), HttpStatusCode.BadGateway, timeout: 70);
                        await SendReceive(testSite.AspNetCoreApp.GetUri("DoSleep50000"), expectedResponseBody: "Running", timeout: 70);
                    }

                    ///////////////////////////////////
                    // Start test sceanrio
                    ///////////////////////////////////
                    if (startUpMode == DoAppVerifierTest_StartUpMode.DontUseGracefulShutdown)
                    {
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "GracefulShutdown", "disabled" });
                    }

                    // reset existing worker process process
                    TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);

                    // verify w3wp.exe process is gone, which means there was no unexpected error
                    TestUtility.RunPowershellScript("(get-process -name w3wp 2> $null).count", "0", retryCount: 3);
                    Thread.Sleep(1000);

                    for (int i = 0; i < repeatCount; i++)
                    {
                        // reset worker process id to refresh
                        testSite.WorkerProcessID = 0;

                        // send a startup request to start a new worker process
                        await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);
                        Thread.Sleep(3000);

                        if (enableAppVerifier)
                        {
                            // attach debugger to the worker process
                            testSite.AttachWinDbg(testSite.WorkerProcessID, "sxi 80000003;g");
                            Thread.Sleep(5000);

                            TestUtility.RunPowershellScript("( invoke-webrequest http://localhost:" + testSite.TcpPort + " ).StatusCode", "200", retryCount: 30);

                            // verify windbg process is started
                            TestUtility.RunPowershellScript("(get-process -name windbg 2> $null).count", "1", retryCount: 5);
                        }

                        DateTime startTime = DateTime.Now;

                        // Verify http request
                        await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);

                        // Get Process ID
                        string backendProcessId_old = await GetAspnetCoreAppProcessId(testSite);

                        // Verify WebSocket without setting subprotocol
                        await SendReceive(testSite.WebSocketApp.GetUri("echo.aspx"), expectedStringsInResponseBody: new string[] { "Socket Open" }, timeout: 10); // echo.aspx has hard coded path for the websocket server

                        // Verify WebSocket subprotocol
                        await SendReceive(testSite.WebSocketApp.GetUri("echoSubProtocol.aspx"), expectedStringsInResponseBody: new string[] { "Socket Open", "mywebsocketsubprotocol" }, timeout: 10); // echoSubProtocol.aspx has hard coded path for the websocket server

                        string testData = "test";

                        // Verify websocket 
                        using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                        {
                            var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                            Assert.Contains("Connection: Upgrade", frameReturned.Content);
                            Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                            Thread.Sleep(500);

                            VerifySendingWebSocketData(websocketClient, testData);
                            Thread.Sleep(500);

                            frameReturned = websocketClient.Close();
                            Thread.Sleep(500);

                            Assert.True(frameReturned.FrameType == FrameType.Close, "Closing Handshake");
                        }

                        // send a simple request and verify the response body
                        await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);

                        Thread.Sleep(500);
                        string backendProcessId = (await GetAspnetCoreAppProcessId(testSite));
                        Assert.Equal(backendProcessId_old, backendProcessId);

                        // Verify server side websocket disconnection
                        using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                        {
                            var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                            Assert.Contains("Connection: Upgrade", frameReturned.Content);
                            Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                            Thread.Sleep(500);

                            Assert.True(websocketClient.IsOpened, "Check active connection before starting");

                            // Send a special string to initiate the server side connection closing
                            websocketClient.SendTextData("CloseFromServer");
                            bool connectionClosedFromServer = websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed);

                            // Verify server side connection closing is done successfully
                            Assert.True(connectionClosedFromServer, "Closing Handshake initiated from Server");

                            // extract text data from the last frame, which is the close frame
                            int lastIndex = websocketClient.Connection.DataReceived.Count - 1;

                            // Verify text data is matched to the string sent by server
                            Assert.Contains("ClosingFromServer", websocketClient.Connection.DataReceived[lastIndex].TextData);
                        }

                        // send a simple request and verify the response body
                        await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);

                        Thread.Sleep(500);
                        backendProcessId = (await GetAspnetCoreAppProcessId(testSite, timeout: 10));

                        Assert.Equal(backendProcessId_old, backendProcessId);

                        // Set Shutdown delay time to give more time for the backend program to do the gracefulshutdown
                        int shutdownDelayTime = 5000;
                        iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", shutdownDelayTime.ToString() });

                        if (startUpMode != DoAppVerifierTest_StartUpMode.DontUseGracefulShutdown)
                        {
                            // Verify websocket with app_offline.htm
                            using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                            {
                                // Test issue. Closing connection does not work reliably because of timing issue. 
                                // Temporarily, let's ignore the noise with this special flag
                                //websocketClient.ExpectedDisposedConnection = true;

                                for (int jj = 0; jj < 10; jj++)
                                {
                                    testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                                    Thread.Sleep(1000);

                                    backendProcessId = (await GetAspnetCoreAppProcessId(testSite, timeout: 10));

                                    var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                                    Assert.Contains("Connection: Upgrade", frameReturned.Content);
                                    Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                                    Thread.Sleep(500);

                                    VerifySendingWebSocketData(websocketClient, testData);
                                    Thread.Sleep(500);

                                    // put app_offline
                                    testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "App_Offline.Htm");
                                    Thread.Sleep(1000);

                                    // wait for the gracefulshutdown finished
                                    Thread.Sleep(shutdownDelayTime);

                                    bool connectionClosedFromServer = websocketClient.WaitForWebSocketState(WebSocketState.ConnectionClosed);

                                    // Verify server side connection closing is done successfully
                                    Assert.True(connectionClosedFromServer, "Closing Handshake initiated from Server");

                                    await VerifyWorkerProcessRecycledUnderInprocessMode(testSite, backendProcessId);
                                }
                            }

                            // remove app_offline.htm
                            testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                            Thread.Sleep(500);
                        }

                        // reset shutdownDelayTime
                        shutdownDelayTime = 0;
                        iisConfig.SetANCMConfig(
                            testSite.SiteName,
                            testSite.AspNetCoreApp.Name,
                            "environmentVariable",
                            new string[] { "ANCMTestShutdownDelay", shutdownDelayTime.ToString() },
                            removeExisting: true);  // reset existing correction item

                        // Verify websocket again
                        using (WebSocketClientHelper websocketClient = new WebSocketClientHelper())
                        {
                            var frameReturned = websocketClient.Connect(testSite.AspNetCoreApp.GetUri("websocket"), true, true);
                            Assert.Contains("Connection: Upgrade", frameReturned.Content);
                            Assert.Contains("HTTP/1.1 101 Switching Protocols", frameReturned.Content);
                            Thread.Sleep(500);

                            VerifySendingWebSocketData(websocketClient, testData);
                            Thread.Sleep(1000);

                            frameReturned = websocketClient.Close();
                            Thread.Sleep(1000);

                            Assert.True(frameReturned.FrameType == FrameType.Close, "Closing Handshake");
                        }

                        // send a simple request and verify the response body
                        await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);

                        // Verify https request
                        Uri targetHttpsUri = testSite.AspNetCoreApp.GetUri(null, sslPort, protocol: "https");
                        var result = await SendReceive(targetHttpsUri, requestHeaders: new string[] { "Accept-Encoding", "gzip" });
                        Assert.True(result.ResponseBody.Contains("Running"), "verify response body");

                        switch (shutDownMode)
                        {
                            case DoAppVerifierTest_ShutDownMode.StopAndStartAppPool:
                                iisConfig.StopAppPool(testSite.AspNetCoreApp.AppPoolName);
                                Thread.Sleep(5000);
                                iisConfig.StartAppPool(testSite.AspNetCoreApp.AppPoolName);
                                break;
                            case DoAppVerifierTest_ShutDownMode.RestartW3SVC:
                                TestUtility.ResetHelper(ResetHelperMode.StopWasStartW3svc);
                                break;
                            case DoAppVerifierTest_ShutDownMode.CreateAppOfflineHtm:
                                testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                                testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "App_Offline.Htm");
                                break;
                            case DoAppVerifierTest_ShutDownMode.ConfigurationChangeNotification:
                                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "startupTimeLimit", timeoutValue + 1);
                                iisConfig.RecycleAppPool(testSite.AspNetCoreApp.AppPoolName);
                                break;
                            case DoAppVerifierTest_ShutDownMode.RecycleAppPool:
                                iisConfig.RecycleAppPool(testSite.AspNetCoreApp.AppPoolName);
                                break;
                        }
                        Thread.Sleep(2000);

                        if (verifyTimeout)
                        {
                            // Wait for shutdown delay additionally
                            Thread.Sleep(timeoutValue * 1000);
                        }

                        switch (shutDownMode)
                        {
                            case DoAppVerifierTest_ShutDownMode.CreateAppOfflineHtm:
                                // verify app_offline.htm file works
                                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "test" + "\r\n", expectedResponseStatus: HttpStatusCode.ServiceUnavailable, timeout: 10);

                                // remove app_offline.htm file and then recycle apppool
                                testSite.AspNetCoreApp.MoveFile("App_Offline.Htm", "_App_Offline.Htm");
                                iisConfig.RecycleAppPool(testSite.AspNetCoreApp.AppPoolName);
                                Thread.Sleep(2000);
                                break;
                        }

                        if (enableAppVerifier)
                        {
                            // verify windbg process is gone, which means there was no unexpected error
                            TestUtility.RunPowershellScript("(get-process -name windbg 2> $null).count", "0", retryCount: 5);
                        }
                    }


                    // clean up https test environment

                    // Remove the SSL Certificate mapping
                    iisConfig.RemoveSSLCertificate(sslPort, hexIPAddress);

                    // Remove the newly created self signed certificate
                    iisConfig.DeleteCertificate(thumbPrint);

                    // Remove the exported self signed certificate on rootCA
                    iisConfig.DeleteCertificate(thumbPrint, @"Cert:\LocalMachine\Root");
                }
            }
            finally
            {
                // cleanup Appverifier
                if (testSite != null)
                {
                    if (enableAppVerifier)
                    {
                        testSite.DetachAppverifier();
                    }
                }
            }
            TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);

            // cleanup windbg process incase it is still running
            if (!testResult)
            {
                TestUtility.RunPowershellScript("stop-process -Name windbg -Force -Confirm:$false 2> $null");
            }
        }

        public static async Task DoStressTest(bool enableAppVerifier)
        {
            if (!File.Exists(Environment.ExpandEnvironmentVariables("%systemdrive%\\ANCMStressTest.TXT")))
            { 
                TestUtility.LogInformation("Skipping stress test");
                return;
            }

            //
            // While running this test, start stressing with running below command in a seperate powershell window
            //
            // (1..1000) | foreach { (Invoke-WebRequest http://localhost:1234/aspnetcoreapp/getprocessid).StatusCode; }
            //

            int timeoutValue = 5;
            int numberOfSite = 0;

            int tcpPort = 1234 + numberOfSite;
            TestWebSite testSite = new TestWebSite(IISConfigUtility.AppPoolBitness.noChange, "DoStressTest", publishing: false, tcpPort: tcpPort);
            InitializeSite(testSite, timeoutValue : timeoutValue);
            await StartIISExpress(testSite);

            numberOfSite++;

            tcpPort = 1234 + numberOfSite;
            TestWebSite testSite2 = new TestWebSite(IISConfigUtility.AppPoolBitness.noChange, "DoStressTest2", publishing: false, tcpPort: 1234 + numberOfSite);
            InitializeSite(testSite2, timeoutValue: timeoutValue, disableGracefulShutdown: true);
            await StartIISExpress(testSite2);
            numberOfSite++;

            tcpPort = 1234 + numberOfSite;
            TestWebSite testSite3 = new TestWebSite(IISConfigUtility.AppPoolBitness.enable32Bit, "DoStressTest3", publishing: false, tcpPort: 1234 + numberOfSite);
            InitializeSite(testSite3, timeoutValue: timeoutValue, disableGracefulShutdown: true);
            await StartIISExpress(testSite3);
            numberOfSite++;

            if (enableAppVerifier)
            {
                // enable AppVerifier 
                testSite.AttachAppverifier();
            }

            // reset existing worker process process
            TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);
            Thread.Sleep(1000);

            try
            {
                ///////////////////////////////////
                // Start test sceanrio
                ///////////////////////////////////
                using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
                {
                    bool attachDebugger = true;
                    for (int i=0; i<10; i++)
                    {
                        if (enableAppVerifier)
                        {
                            if (attachDebugger)
                            {
                                // send a startup request to start a new worker process
                                TestUtility.RunPowershellScript("( invoke-webrequest http://localhost:" + testSite.TcpPort + " ).StatusCode", "200", retryCount: 5);
                                Thread.Sleep(1000);

                                // attach debugger to the worker process
                                testSite.WorkerProcessID = 0;

                                // attach debugger
                                testSite.AttachWinDbg(testSite.WorkerProcessID, "sxi 80000003;g");

                                // verify windbg process is started
                                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", timeout: 10);
                                TestUtility.RunPowershellScript("(get-process -name windbg 2> $null).count", "1", retryCount: 5);
                         
                                attachDebugger = false;
                            }

                            // verify debugger is running
                            TestUtility.RunPowershellScript("(get-process -name windbg 2> $null).count", "1", retryCount: 1);
                        }

                        // put delay time for each iteration
                        Thread.Sleep(3000);

                        // reset worker process id to refresh
                        testSite.WorkerProcessID = 0;
                        
                        switch (i % 7)
                        {
                            case 0:
                                
                                // StopAndStartAppPool:
                                iisConfig.StopAppPool(testSite.AspNetCoreApp.AppPoolName);
                                Thread.Sleep((timeoutValue + 1) * 1000);
                                iisConfig.StartAppPool(testSite.AspNetCoreApp.AppPoolName);
                                Thread.Sleep((timeoutValue + 1) * 1000);

                                attachDebugger = true;
                                break;

                            case 1: 
                                
                                // CreateAppOfflineHtm
                                testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                                testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "App_Offline.Htm");
                                break;

                            case 2:

                                // Re-create AppOfflineHtm
                                testSite.AspNetCoreApp.DeleteFile("App_Offline.Htm");
                                testSite.AspNetCoreApp.CreateFile(new string[] { "test" }, "App_Offline.Htm");
                                break;

                            case 3:

                                // Rename appOfflineHtm
                                testSite.AspNetCoreApp.MoveFile("App_Offline.Htm", "_App_Offline.Htm");
                                break;

                            case 4:

                                // ConfigurationChangeNotification
                                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", timeoutValue + 1);
                                Thread.Sleep(1000);
                                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", timeoutValue - 1);
                                Thread.Sleep(1000);

                                if (testSite.AspNetCoreApp.HostingModel == TestWebApplication.HostingModelValue.Inprocess)
                                {
                                    attachDebugger = true;
                                }
                                break;

                            case 5:
                                iisConfig.SetAppPoolSetting(testSite.AspNetCoreApp.AppPoolName, "enable32BitAppOnWin64", true);
                                break;

                            case 6:
                                iisConfig.SetAppPoolSetting(testSite.AspNetCoreApp.AppPoolName, "enable32BitAppOnWin64", false);
                                break;

                            default:
                                throw new Exception("Not supported value");
                        }
                    }

                    InitializeSite(testSite, cleanup: true);
                    InitializeSite(testSite2, cleanup: true);
                    InitializeSite(testSite3, cleanup: true);
                }
            }
            finally
            {
                if (enableAppVerifier)
                {
                    // cleanup Appverifier
                    if (testSite != null)
                    {
                        testSite.DetachAppverifier();
                    }
                }
            }

            TestUtility.ResetHelper(ResetHelperMode.KillWorkerProcess);

            if (enableAppVerifier)
            {
                // cleanup windbg process incase it is still running
                TestUtility.RunPowershellScript("stop-process -Name windbg -Force -Confirm:$false 2> $null");
            }
        }

        private static void InitializeSite(TestWebSite testSite, int timeoutValue = 0, bool disableGracefulShutdown = false, bool cleanup = false)
        {
            using (var iisConfig = new IISConfigUtility(testSite.IisServerType, testSite.IisExpressConfigPath))
            {
                string hexIPAddress = "0x00";
                int sslPort = testSite.TcpPort + 6300;

                if (cleanup)
                {
                    // Remove the SSL Certificate mapping
                    iisConfig.RemoveSSLCertificate(sslPort, hexIPAddress);

                    // Remove the newly created self signed certificate
                    iisConfig.DeleteCertificate(testSite.ThumbPrint);

                    // Remove the exported self signed certificate on rootCA
                    iisConfig.DeleteCertificate(testSite.ThumbPrint, @"Cert:\LocalMachine\Root");
                    return;
                }

                string hostName = "";
                string subjectName = "localhost";
                string ipAddress = "*";
                string thumbPrint = null;
                if (disableGracefulShutdown)
                {
                    // disable graceful shutdown for the second site
                    iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "GracefulShutdown", "disabled" });
                }

                // Add https binding and get https uri information
                iisConfig.AddBindingToSite(testSite.SiteName, ipAddress, sslPort, hostName, "https");

                // Create a self signed certificate
                thumbPrint = iisConfig.CreateSelfSignedCertificate(subjectName);

                // Export the self signed certificate to rootCA
                iisConfig.ExportCertificateTo(thumbPrint, sslStoreTo: @"Cert:\LocalMachine\Root");

                // Configure http.sys ssl certificate mapping to IP:Port endpoint with the newly created self signed certificage
                iisConfig.SetSSLCertificate(sslPort, hexIPAddress, thumbPrint);
                testSite.ThumbPrint = thumbPrint;

                // enable preloadEnabled for the site
                iisConfig.SetSiteRooAppConfig(testSite.SiteName, "preloadEnabled", true);
                iisConfig.SetWarmUpConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "skipManagedModules", true);
                iisConfig.SetWarmUpConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "doAppInitAfterRestart", true);
                iisConfig.SetWarmUpConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "initializationPage", "DoSleep1000");

                // Set shutdownTimeLimit with 3 seconds and use 5 seconds for delay time to make the shutdownTimeout happen
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", timeoutValue);

                // set requestTimeout
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "requestTimeout", TimeSpan.Parse("00:00:5")); // 5 seconds

                // set startupTimeout
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "startupTimeLimit", timeoutValue);

                // Set shutdownTimeLimit 
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "shutdownTimeLimit", timeoutValue);

                // Set starupTimeLimit and shutdownTimeLimit for test app
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestShutdownDelay", "1000" });
                iisConfig.SetANCMConfig(testSite.SiteName, testSite.AspNetCoreApp.Name, "environmentVariable", new string[] { "ANCMTestStartupDelay", "1000" });
            }
        }

        private static bool CleanupVSJitDebuggerWindow(string bugNumber = null)
        {
            bool result = TestUtility.ResetHelper(ResetHelperMode.KillVSJitDebugger);
            if (bugNumber == null)
            {
                Assert.False(result, "There should be VSJitDebugger window");
            }
            else
            {
                TestUtility.LogInformation("There is a bug: " + bugNumber);
            }
            return result;
        }

        private static string GetHeaderValue(string inputData, string headerName)
        {
            string result = string.Empty;
            foreach (string item in inputData.Split(new char[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (item.Contains(headerName))
                {
                    var tokens = item.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 2)
                    {
                        result = tokens[1].Trim();
                        break;
                    }
                }
            }
            return result;
        }

        private static bool VerifySendingWebSocketData(WebSocketClientHelper websocketClient, string testData)
        {
            bool result = false;

            //
            // send complete or partial text data and ping multiple times
            //
            websocketClient.SendTextData(testData);
            websocketClient.SendPing();
            websocketClient.SendTextData(testData);
            websocketClient.SendPing();
            websocketClient.SendPing();
            websocketClient.SendTextData(testData, 0x01);  // 0x01: start of sending partial data
            websocketClient.SendPing();
            websocketClient.SendTextData(testData, 0x80);  // 0x80: end of sending partial data
            websocketClient.SendPing();
            websocketClient.SendPing();
            websocketClient.SendTextData(testData);
            websocketClient.SendTextData(testData);
            websocketClient.SendTextData(testData);
            websocketClient.SendPing();
            Thread.Sleep(3000);

            // Verify test result
            for (int i = 0; i < 3; i++)
            {
                if (!DoVerifyDataSentAndReceived(websocketClient))
                {
                    // retrying after 1 second sleeping
                    Thread.Sleep(1000);
                }
                else
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        private static bool DoVerifyDataSentAndReceived(WebSocketClientHelper websocketClient)
        {
            var result = true;
            var sentString = new StringBuilder();
            var recString = new StringBuilder();
            var pingString = new StringBuilder();
            var pongString = new StringBuilder();

            foreach (Frame frame in websocketClient.Connection.DataSent.ToArray())
            {
                if (frame.FrameType == FrameType.Continuation
                    || frame.FrameType == FrameType.SegmentedText
                        || frame.FrameType == FrameType.Text
                            || frame.FrameType == FrameType.ContinuationFrameEnd)
                {
                    sentString.Append(frame.Content);
                }

                if (frame.FrameType == FrameType.Ping)
                {
                    pingString.Append(frame.Content);
                }
            }

            foreach (Frame frame in websocketClient.Connection.DataReceived.ToArray())
            {
                if (frame.FrameType == FrameType.Continuation
                    || frame.FrameType == FrameType.SegmentedText
                        || frame.FrameType == FrameType.Text
                            || frame.FrameType == FrameType.ContinuationFrameEnd)
                {
                    recString.Append(frame.Content);
                }

                if (frame.FrameType == FrameType.Pong)
                {
                    pongString.Append(frame.Content);
                }
            }

            if (sentString.Length == recString.Length && pongString.Length == pingString.Length)
            {
                if (sentString.Length != recString.Length)
                {
                    result = false;
                    TestUtility.LogInformation("Same size of data sent(" + sentString.Length + ") and received(" + recString.Length + ")");
                }

                if (sentString.ToString() != recString.ToString())
                {
                    result = false;
                    TestUtility.LogInformation("Not matched string in sent and received");
                }
                if (pongString.Length != pingString.Length)
                {
                    result = false;
                    TestUtility.LogInformation("Ping received; Ping (" + pingString.Length + ") and Pong (" + pongString.Length + ")");
                }
                websocketClient.Connection.DataSent.Clear();
                websocketClient.Connection.DataReceived.Clear();
            }
            else
            {
                TestUtility.LogInformation("Retrying...  so far data sent(" + sentString.Length + ") and received(" + recString.Length + ")");
                result = false;
            }
            return result;
        }

        private static async Task CheckChunkedAsync(HttpClient client, TestWebApplication webApp)
        {
            var response = await client.GetAsync(webApp.GetUri("chunked"));
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Chunked", responseText);
                Assert.True(response.Headers.TransferEncodingChunked, "/chunked, chunked?");
                Assert.Null(response.Headers.ConnectionClose);
                Assert.Null(GetContentLength(response));
            }
            catch (XunitException ex)
            {
                TestUtility.LogInformation(response.ToString());
                TestUtility.LogInformation(responseText);
                throw ex;
            }
        }
        
        private static string GetContentLength(HttpResponseMessage response)
        {
            // Don't use response.Content.Headers.ContentLength, it will dynamically calculate the value if it can.
            IEnumerable<string> values;
            return response.Content.Headers.TryGetValues(HeaderNames.ContentLength, out values) ? values.FirstOrDefault() : null;
        }

        private static bool VerifyANCMStartEvent(DateTime startFrom, string includeThis)
        {
            return VerifyEventLog(1001, startFrom, includeThis);
        }

        private static bool VerifyANCMGracefulShutdownEvent(DateTime startFrom, string includeThis)
        {
            return VerifyEventLog(1006, startFrom, includeThis);
        }

        private static bool VerifyANCMGracefulShutdownFailureEvent(DateTime startFrom, string includeThis)
        {
            return VerifyEventLog(1005, startFrom, includeThis);
        }

        private static bool VerifyApplicationEventLog(int eventID, DateTime startFrom, string includeThis)
        {
            return VerifyEventLog(eventID, startFrom, includeThis);
        }

        private static bool VerifyEventLog(int eventId, DateTime startFrom, string includeThis = null)
        {
            //bugbug
            if (IISConfigUtility.ANCMInprocessMode)
            {
                // event verification fails in Inprocess mode
                return true;
            }

            var events = TestUtility.GetApplicationEvent(eventId, startFrom);
            Assert.True(events.Count > 0, "Verfiy expected event logs");
            bool findEvent = false;
            foreach (string item in events)
            {
                if (item.Contains(includeThis))
                {
                    findEvent = true;
                    break;
                }
            }
            return findEvent;
        }

        public class SendReceiveContext : IDisposable
        {
            public Uri Uri = null;
            public string[] RequestHeaders = null;
            public string ExpectedResponseBody = null;
            public string[] ExpectedStringsInResponseBody = null;
            public HttpStatusCode ExpectedResponseStatus;
            public int NumberOfRetryCount = 2;
            public bool VerifyResponseFlag = true;
            public KeyValuePair<string, string>[] PostData = null;
            public int Timeout = 5;  // second

            // output variables
            public string ResponseBody = null;
            public string ResponseHeader = null;
            public string ResponseStatus = null;

            public string ResponseHeaderBody
            {
                get { return ResponseBody + ", " + ResponseHeader; }
            }

            public void Dispose()
            {
            }
        }

        private static async Task<string> GetAspnetCoreAppProcessId(TestWebSite testSite, Uri uri = null, int timeout = 5, int numberOfRetryCount = 2, bool verifyRunning = true)
        {
            Uri tempUri = uri;
            if (uri == null)
            {
                tempUri = testSite.AspNetCoreApp.GetUri("GetProcessId");
            }

            if (testSite.IisServerType == ServerType.IIS && testSite.AspNetCoreApp.HostingModel != TestWebApplication.HostingModelValue.Inprocess)
            {
                // bugbug: Inprocess mode requires more time because recycling worker process is slow
                // https://github.com/aspnet/IISIntegration/issues/664
                // this line should be removed when the bug is fixed
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", numberOfRetryCount: 20);
            }
            else
            {
                await SendReceive(testSite.AspNetCoreApp.GetUri(), expectedResponseBody: "Running", numberOfRetryCount: 10);
            }

            string processId = (await SendReceive(tempUri, timeout: timeout, numberOfRetryCount: numberOfRetryCount)).ResponseBody;
            if (processId == null)
            {
                throw new Exception("Failed to get process ID with " + tempUri);
            }
            if (Convert.ToInt32(processId) <= 0)
            {
                throw new Exception("Get invalid processId returned: " + processId);
            }
            return processId;
        }

        private static async Task StartIISExpress(TestWebSite testSite, bool verifyAppRunning = true, string expectedResponseBody = "Running", HttpStatusCode expectedResponseStatus = HttpStatusCode.OK)
        {
            if (testSite.IisServerType == ServerType.IISExpress)
            {
                // clean up IISExpress before starting a new instance
                TestUtility.KillIISExpressProcess();

                // reset workerProcessID
                testSite.WorkerProcessID = 0;

                string cmdline;
                string argument = "/siteid:" + testSite.SiteId + " /config:" + testSite.IisExpressConfigPath;

                if (Directory.Exists(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%"))
                    && testSite.AppPoolBitness == IISConfigUtility.AppPoolBitness.enable32Bit)
                {
                    cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%"), "IIS Express", "iisexpress.exe");
                }
                else
                {
                    cmdline = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "IIS Express", "iisexpress.exe");
                }
                TestUtility.LogInformation("TestWebSite::TestWebSite() Start IISExpress: " + cmdline + " " + argument);
                testSite.IisExpressPidBackup = TestUtility.RunCommand(cmdline, argument, false, false);
                System.Threading.Thread.Sleep(1000);
            }

            if (verifyAppRunning)
            {
                await SendReceive(testSite.AspNetCoreApp.GetUri(""), expectedResponseStatus: expectedResponseStatus, expectedResponseBody: expectedResponseBody, numberOfRetryCount: 10);
            }
        }

        private static async Task VerifyWorkerProcessRecycledUnderInprocessMode(TestWebSite testSite, string backendProcessId, int timeout = 10000, bool restartIISExpres = true)
        {
            if (testSite.AspNetCoreApp.HostingModel != TestWebApplication.HostingModelValue.Inprocess)
            {
                return; // do nothing for outofprocess
            }

            bool succeeded = false;
            for (int i = 0; i < (timeout / 1000); i++)
            {
                Process backendProcess = null;
                try
                {
                    backendProcess = Process.GetProcessById(Convert.ToInt32(backendProcessId));
                }
                catch
                {
                    succeeded = true;
                    TestUtility.LogInformation("Process not found.");
                    break;
                }

                if (backendProcess == null)
                {
                    succeeded = true;
                    break;
                }

                if (backendProcess.WaitForExit(1000))
                {
                    succeeded = true;
                    break;
                }

                if (testSite.IisServerType == ServerType.IISExpress && i == 3)
                {
                    // exit after 3 seconds for IISExpress case
                    break;
                }
            }

            if (succeeded == false)
            {
                if (testSite.IisServerType == ServerType.IIS)
                {
                    throw new Exception("Failed to recycle IIS worker process");
                }
                else
                {
                    // IISExpress should be killed if it can't be recycled
                    TestUtility.LogInformation("BugBug: Restart IISExpress...");
                    TestUtility.ResetHelper(ResetHelperMode.KillIISExpress);
                }
            }

            if (restartIISExpres)
            {
                if (testSite.IisServerType == ServerType.IISExpress)
                {
                    // restart IISExpress
                    await StartIISExpress(testSite);
                }
            }
        }

        private static async Task<SendReceiveContext> SendReceive(Uri uri, HttpStatusCode expectedResponseStatus = HttpStatusCode.OK, string[] requestHeaders = null, string expectedResponseBody = null, string[] expectedStringsInResponseBody = null, int timeout = 5, int numberOfRetryCount = 2, bool verifyResponseFlag = true, KeyValuePair<string, string>[] postData = null)
        {
            using (SendReceiveContext context = new SendReceiveContext())
            {
                context.Uri = uri;
                context.RequestHeaders = requestHeaders;
                context.ExpectedResponseBody = expectedResponseBody;
                context.ExpectedStringsInResponseBody = expectedStringsInResponseBody;
                context.ExpectedResponseStatus = expectedResponseStatus;
                context.NumberOfRetryCount = numberOfRetryCount;
                context.VerifyResponseFlag = verifyResponseFlag;
                context.PostData = postData;
                context.Timeout = timeout;

                SendReceiveContext result = null;
                bool success = false;
                for (int i = 0; i < numberOfRetryCount; i++)
                {
                    try
                    {
                        result = await SendReceive(context);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        TestUtility.LogInformation("Retrying... SendReceive failed : " + ex.Message);
                        success = false;
                    }

                    if (result == null || (result.ExpectedResponseStatus == HttpStatusCode.OK && result.ExpectedResponseBody != null && result.ResponseBody == null))
                    {
                        TestUtility.LogInformation("Retrying... SendReceive received null value for ResponseBody");
                        success = false;
                    }

                    if (success)
                    {
                        break;
                    }

                    TestUtility.LogInformation(i + ": SendReceive() retrying...");
                    Thread.Sleep(1000);
                }
                if (!success)
                {
                    throw new Exception("SendReceive failed");
                }
                return result; 
            }
        }

        private static async Task<string> ReadContent(HttpResponseMessage response)
        {
            bool unZipContent = false;
            string result = String.Empty;

            IEnumerable<string> values;
            if (response.Headers.TryGetValues("Vary", out values))                    
            {
                unZipContent = true;
            }

            if (unZipContent)
            {
                var inputStream = await response.Content.ReadAsStreamAsync();

                // for debugging purpose
                //byte[] temp = new byte[inputStream.Length];
                //inputStream.Read(temp, 0, (int) inputStream.Length);
                //inputStream.Position = 0;

                using (var gzip = new GZipStream(inputStream, CompressionMode.Decompress))
                {
                    var outputStream = new MemoryStream();
                    try
                    {
                        await gzip.CopyToAsync(outputStream);
                    }
                    catch (Exception ex)
                    {
                        // Even though "Vary" response header exists, the content is not actually compressed.
                        // We should ignore this execption until we find a proper way to determine if the body is compressed or not.
                        if (ex.Message.IndexOf("gzip", StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            result = await response.Content.ReadAsStringAsync();
                            return result;
                        }
                        throw ex;
                    }
                    gzip.Close();
                    inputStream.Close();
                    outputStream.Position = 0;
                    using (StreamReader reader = new StreamReader(outputStream, Encoding.UTF8))
                    {
                        result = reader.ReadToEnd();
                        outputStream.Close();
                    }
                }
            }
            else
            {
                result = await response.Content.ReadAsStringAsync();
            }
            return result;
        }

        private static async Task<SendReceiveContext> SendReceive(SendReceiveContext context)
        {
            Uri uri = context.Uri;
            string[] requestHeaders = context.RequestHeaders;
            string expectedResponseBody = context.ExpectedResponseBody;
            string[] expectedStringsInResponseBody = context.ExpectedStringsInResponseBody;
            HttpStatusCode expectedResponseStatus = context.ExpectedResponseStatus;
            int numberOfRetryCount = context.NumberOfRetryCount;
            bool verifyResponseFlag = context.VerifyResponseFlag;
            KeyValuePair<string, string>[] postData = context.PostData;
            int timeout = context.Timeout;
            
            string responseText = "NotInitialized";
            string responseStatus = "NotInitialized";

            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.UseDefaultCredentials = true;
            httpClientHandler.AutomaticDecompression = DecompressionMethods.None;

            var httpClient = new HttpClient(httpClientHandler)
            {
                BaseAddress = uri,
                Timeout = TimeSpan.FromSeconds(timeout),                
            };
            
            if (requestHeaders != null)
            {
                for (int i = 0; i < requestHeaders.Length; i=i+2)
                {
                    httpClient.DefaultRequestHeaders.Add(requestHeaders[i], requestHeaders[i+1]);
                }
            }

            HttpResponseMessage response = null;
            try
            {
                FormUrlEncodedContent postHttpContent = null;
                if (postData != null)
                {
                    postHttpContent = new FormUrlEncodedContent(postData);
                }
                
                if (numberOfRetryCount > 1 && expectedResponseStatus == HttpStatusCode.OK)
                {
                    if (postData == null)
                    {
                        response = await TestUtility.RetryRequest(() =>
                        {
                            return httpClient.GetAsync(string.Empty);
                        }, TestUtility.Logger, retryCount: numberOfRetryCount);
                    }
                    else
                    {
                        response = await TestUtility.RetryRequest(() =>
                        {                            
                            return httpClient.PostAsync(string.Empty, postHttpContent);
                        }, TestUtility.Logger, retryCount: numberOfRetryCount);
                    }
                }
                else
                {
                    if (postData == null)
                    {
                        response = await httpClient.GetAsync(string.Empty);
                    }
                    else
                    {
                        response = await httpClient.PostAsync(string.Empty, postHttpContent);
                    }
                }

                if (response != null)
                {
                    responseStatus = response.StatusCode.ToString();
                    if (verifyResponseFlag)
                    {
                        if (expectedResponseBody != null)
                        {
                            if (responseText == "NotInitialized")
                            {
                                responseText = await ReadContent(response);
                            }
                            Assert.Equal(expectedResponseBody, responseText.TrimEnd(new char[] { '\r', '\n' }));
                        }

                        if (expectedStringsInResponseBody != null)
                        {
                            if (responseText == "NotInitialized")
                            {
                                responseText = await ReadContent(response);
                            }
                            foreach (string item in expectedStringsInResponseBody)
                            {
                                Assert.Contains(item, responseText);
                            }
                        }
                        Assert.Equal(expectedResponseStatus, response.StatusCode);
                    }

                    if (responseText == "NotInitialized")
                    {
                        responseText = await ReadContent(response);
                    }
                    context.ResponseBody = responseText;
                    context.ResponseHeader = response.ToString();
                    context.ResponseStatus = response.StatusCode.ToString();
                }
            }
            catch (XunitException)
            {
                if (response != null)
                {
                    TestUtility.LogInformation(response.ToString());
                }
                TestUtility.LogInformation(responseText);
                TestUtility.LogInformation(responseStatus);
            }
            return context;
        }
    }
    
}
