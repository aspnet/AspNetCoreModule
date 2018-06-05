#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

<#
.SYNOPSIS
    This script is run before each CI run to ensure things are properly cleaned up
.DESCRIPTION
    This script is run before each CI run to ensure things are properly cleaned up
.PARAMETER dropLocation

#>

# If two tests are run too closely, VBCSCompiler process might still be alive for previous test preventing dotnet and packages to be cleand up
function KillVBCSCompilers() {
    $procs = Get-WmiObject Win32_Process | Where-Object { $_.Name.ToLower().StartsWith("dotnet") -and $_.CommandLine.ToLower().Contains("vbcscompiler") }
    foreach ($proc in $procs) {
        Stop-Process -Id $proc.ProcessId
    }
}

# Some failed test would actually cause appHost config file to become corrupted and w3svc would not be able to start
function EnsureAppHostConfig() {
    $appConfigLocation = [System.IO.Path]::Combine($env:WinDir, "System32", "inetsrv", "config", "applicationHost.config")
    if (!(Test-Path $appConfigLocation) -or ((Get-Item $appConfigLocation).Length -eq 0)) {
        $appConfigBackupLocation = "${appConfigLocation}.ancmtest.masterbackup"
        Copy-Item -Path $appConfigBackupLocation -Destination $appConfigLocation -Force
    }

}

KillVBCSCompilers

EnsureAppHostConfig
