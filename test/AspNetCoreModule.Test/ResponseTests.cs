// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.IntegrationTesting;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Xunit;
using Xunit.Sdk;

namespace AspNetCoreModule.FunctionalTests
{
    // Uses ports ranging 5080 - 5099.
    public class ResponseTests : IClassFixture<UseLatestAncm>
    {
        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        [InlineData(ServerType.IISExpress, RuntimeFlavor.Clr, RuntimeArchitecture.x64, "http://localhost:5090/", ApplicationType.Portable)]
        [InlineData(ServerType.IISExpress, RuntimeFlavor.Clr, RuntimeArchitecture.x64, "http://localhost:5091/", ApplicationType.Portable)]
        //[InlineData(ServerType.IISExpress, RuntimeFlavor.Clr, RuntimeArchitecture.x86, "http://localhost:5090/", ApplicationType.Portable)]
        public Task BasicTest(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, ApplicationType applicationType)
        {
            return ResponseFormats(serverType, runtimeFlavor, architecture, applicationBaseUrl, CheckChunkedAsync, applicationType);
        }

        public async Task ResponseFormats(ServerType serverType, RuntimeFlavor runtimeFlavor, RuntimeArchitecture architecture, string applicationBaseUrl, Func<HttpClient, ILogger, Task> scenario, ApplicationType applicationType)
        {
            var logger = new LoggerFactory()
                            .AddConsole()
                            .CreateLogger(string.Format("ResponseFormats:{0}:{1}:{2}:{3}", serverType, runtimeFlavor, architecture, applicationType));

            using (logger.BeginScope("ResponseFormatsTest"))
            {
                var deploymentParameters = new DeploymentParameters(Helpers.GetApplicationPath(applicationType), serverType, runtimeFlavor, architecture)
                {
                    ApplicationBaseUriHint = applicationBaseUrl,
                    EnvironmentName = "Responses",
                    ServerConfigTemplateContent = Helpers.GetConfigContent(serverType, "Http.config", "nginx.conf"),
                    SiteName = "HttpTestSite", // This is configured in the Http.config
                    TargetFramework = runtimeFlavor == RuntimeFlavor.Clr ? "net451" : "netcoreapp1.0",
                    ApplicationType = applicationType
                };

                using (var deployer = ApplicationDeployerFactory.Create(deploymentParameters, logger))
                {
                    var deploymentResult = deployer.Deploy();
                    var httpClientHandler = new HttpClientHandler();
                    var httpClient = new HttpClient(httpClientHandler) { BaseAddress = new Uri(deploymentResult.ApplicationBaseUri) };

                    // Request to base address and check if various parts of the body are rendered & measure the cold startup time.
                    var response = await RetryHelper.RetryRequest(() =>
                    {
                        return httpClient.GetAsync(string.Empty);
                    }, logger, deploymentResult.HostShutdownToken);

                    var responseText = await response.Content.ReadAsStringAsync();
                    try
                    {
                        Assert.Equal("Running", responseText);
                    }
                    catch (XunitException)
                    {
                        logger.LogWarning(response.ToString());
                        logger.LogWarning(responseText);
                        throw;
                    }

                    await scenario(httpClient, logger);
                }
            }
        }

        private static async Task CheckContentLengthAsync(HttpClient client, ILogger logger)
        {
            var response = await client.GetAsync("contentlength");
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Content Length", responseText);
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Null(response.Headers.ConnectionClose);
                Assert.Equal("14", GetContentLength(response));
            }
            catch (XunitException)
            {
                logger.LogWarning(response.ToString());
                logger.LogWarning(responseText);
                throw;
            }
        }

        private static async Task CheckConnectionCloseAsync(HttpClient client, ILogger logger)
        {
            var response = await client.GetAsync("connectionclose");
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Connnection Close", responseText);
                Assert.True(response.Headers.ConnectionClose, "/connectionclose, closed?");
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Null(GetContentLength(response));
            }
            catch (XunitException)
            {
                logger.LogWarning(response.ToString());
                logger.LogWarning(responseText);
                throw;
            }
        }

        private static async Task CheckChunkedAsync(HttpClient client, ILogger logger)
        {
            var response = await client.GetAsync("chunked");
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Chunked", responseText);
                Assert.True(response.Headers.TransferEncodingChunked, "/chunked, chunked?");
                Assert.Null(response.Headers.ConnectionClose);
                Assert.Null(GetContentLength(response));
            }
            catch (XunitException)
            {
                logger.LogWarning(response.ToString());
                logger.LogWarning(responseText);
                throw;
            }
        }

        private static async Task CheckManuallyChunkedAsync(HttpClient client, ILogger logger)
        {
            var response = await client.GetAsync("manuallychunked");
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Manually Chunked", responseText);
                Assert.True(response.Headers.TransferEncodingChunked, "/manuallychunked, chunked?");
                Assert.Null(response.Headers.ConnectionClose);
                Assert.Null(GetContentLength(response));
            }
            catch (XunitException)
            {
                logger.LogWarning(response.ToString());
                logger.LogWarning(responseText);
                throw;
            }
        }

        private static async Task CheckManuallyChunkedAndCloseAsync(HttpClient client, ILogger logger)
        {
            var response = await client.GetAsync("manuallychunkedandclose");
            var responseText = await response.Content.ReadAsStringAsync();
            try
            {
                Assert.Equal("Manually Chunked and Close", responseText);
                Assert.True(response.Headers.TransferEncodingChunked, "/manuallychunkedandclose, chunked?");
                Assert.True(response.Headers.ConnectionClose, "/manuallychunkedandclose, closed?");
                Assert.Null(GetContentLength(response));
            }
            catch (XunitException)
            {
                logger.LogWarning(response.ToString());
                logger.LogWarning(responseText);
                throw;
            }
        }

        private static string GetContentLength(HttpResponseMessage response)
        {
            // Don't use response.Content.Headers.ContentLength, it will dynamically calculate the value if it can.
            IEnumerable<string> values;
            return response.Content.Headers.TryGetValues(HeaderNames.ContentLength, out values) ? values.FirstOrDefault() : null;
        }
    }
}
