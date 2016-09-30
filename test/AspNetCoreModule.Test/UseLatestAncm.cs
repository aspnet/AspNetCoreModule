using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AspNetCoreModule.FunctionalTests
{
    public class UseLatestAncm : IDisposable
    {
        private readonly string _extractDirectory = null;
        

        public UseLatestAncm()
        {
            string aspnetCoreModulePackagePath = GetLatestAncmPackage();
            _extractDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ZipFile.ExtractToDirectory(aspnetCoreModulePackagePath, _extractDirectory);
            InvokeInstallScript();
        }

        private void InvokeInstallScript()
        {
            var solutionRoot = GetSolutionDirectory();
            string outputPath = Path.Combine(_extractDirectory, "artifacts", "ancm", "Debug");
            //string outputPath = Path.Combine(solutionRoot, "artifacts", "build", "AspNetCore", "bin", "Debug");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"/c {_extractDirectory}/ancm/installancm.ps1 " + outputPath
            }).WaitForExit();
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


        private static string GetLatestAncmPackage()
        {
            var solutionRoot = GetSolutionDirectory();
            var buildDir = Path.Combine(solutionRoot, "artifacts", "build");
            var nupkg = Directory.EnumerateFiles(buildDir, "*.nupkg").OrderByDescending(p => p).FirstOrDefault();
            
            if (nupkg == null)
            {
                throw new Exception("Cannot find Ancm package");
            }

            return nupkg;
        }

        public void Dispose()
        {
            InvokeUninstallScript();
        }

        private void InvokeUninstallScript()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"/c {_extractDirectory}/installancm.ps1 -Rollback",
            }).WaitForExit();
        }
    }
}