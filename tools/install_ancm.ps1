#Requires -RunAsAdministrator

#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

<#
.SYNOPSIS
    ANCM install script
.DESCRIPTION
    Installs/Updates ANCM

.PARAMETER RollbackToTimeStamp
#>

[cmdletbinding()]
param(
    [Alias("r","RollbackTo")]
    [string]$RollbackToTimeStamp
)

Set-StrictMode -Version Latest
$ErrorActionPreference="Stop"
$ProgressPreference="SilentlyContinue"

$inetSrvDirs = @{
    "x64" = [System.IO.Path]::Combine( $env:WinDir, "System32", "inetsrv");
    "x86" = [System.IO.Path]::Combine( $env:WinDir, "SysWOW64", "inetsrv")
}

## copyTargets.Keys = set of files to copy to
## copyTargets.Values = given the target file, where to find it. The path is relative to the folder extracted from nupkg
$copyTargets = @{
    [System.IO.Path]::Combine($inetSrvDirs.x64, "Config", "Schema", "aspnetcore_schema_v2.xml") = "aspnetcore_schema_v2.xml"
}

foreach ($arch in $inetSrvDirs.Keys) {
    foreach ($module in ("aspnetcorev2.dll", "aspnetcorev2_inprocess.dll")) {
        $copyTargets[(Join-Path $inetSrvDirs.$arch $module)] = [System.IO.Path]::Combine("contentFiles", "any", "any", $arch, $module)
    }
}

## empty, in case we have files that are not longer copy targets but we still want to be able to invoke backup on it
$legacyTargets = @()

$appHostConfigLocation = [System.IO.Path]::Combine($inetSrvDirs.x64, "Config", "applicationHost.config")

function GetBackupFileName($path, $timestamp) {
    return "${path}.ancm-intall.${timestamp}.bak"
}  

function Invoke-BackupFile($path)
{
    if (Test-Path $path) {
        $backupPath = GetBackupFileName $path $scriptTime
        Copy-Item $path -Destination $backupPath
        Write-Output "Backed up $path to $backupPath"
    } else {
        Write-Debug "$path does not exist, no backup of this file will be created"
    }
}

function Invoke-RestoreFile($path)
{
    $restorePath = GetBackupFileName $path $RollbackToTimeStamp
    if (Test-Path $restorePath) {
        Move-Item $restorePath -Destination $path -Force
        Write-Output "Restored $restorePath to $path"
    } else {
        Write-Debug "$restorePath does not exist, backup will continue without restoring this file"
    }
}

function Invoke-UpdateAppHostConfig {
    if (!(Test-Path $appHostConfigLocation)) {
        Throw "$appHostConfigLocation is expected to exist"
    }
    Invoke-BackupFile $appHostConfigLocation
    Import-Module IISAdministration

    ## Trick: if the worker process was running 32 bit modes, %windir%\System32 is actually redirected to %windir%\SysWOW64
    ## Hence we would always point to module to $inetSrvDirs.x64
    $aspNetCoreHandlerFilePath = Join-Path $inetSrvDirs.x64 "aspnetcorev2.dll"
    Start-IISCommitDelay
    $sm = Get-IISServerManager

    # Add AppSettings section
    # $sm.GetApplicationHostConfiguration().RootSectionGroup.Sections.Add("appSettings")

    # Set Allow for handlers section
    $appHostconfig = $sm.GetApplicationHostConfiguration()
    $section = $appHostconfig.GetSection("system.webServer/handlers")
    $section.OverrideMode="Allow"

    # Add aspNetCore section to system.webServer
    $sectionaspNetCore = $appHostConfig.RootSectionGroup.SectionGroups["system.webServer"].Sections["aspNetCore"]
    if (!$sectionaspNetCore) {
        $sectionaspNetCore = $appHostConfig.RootSectionGroup.SectionGroups["system.webServer"].Sections.Add("aspNetCore")
    }
    $sectionaspNetCore.OverrideModeDefault = "Allow"
    $sm.CommitChanges()
    
    # Configure module
    $modules = Get-IISConfigSection "system.webServer/modules" | Get-IISConfigCollection
    if (!($modules | Where-Object { $_.Attributes["name"].Value -eq "AspNetCoreModuleV2" })) {
        New-IISConfigCollectionElement -ConfigCollection $modules -ConfigAttribute @{"name"="AspNetCoreModuleV2"}
    }
    # Configure globalModule
    $globalModules = Get-IISConfigSection "system.webServer/globalModules" | Get-IISConfigCollection
    $existingGlobalModule = $globalModules | Where-Object { $_.Attributes["name"].Value -eq "AspNetCoreModuleV2" }
    if (!$existingGlobalModule) {
        $existingGlobalModule = New-IISConfigCollectionElement -ConfigCollection $globalModules -ConfigAttribute @{"name"="AspNetCoreModuleV2"} -PassThru
    }
    Set-IISConfigAttributeValue -ConfigElement $existingGlobalModule -AttributeName "image" -AttributeValue "$aspNetCoreHandlerFilePath"
    Stop-IISCommitDelay
    Write-Debug "Finished updating $appHostConfigLocation"

}

function Invoke-UpdateANCM {
    $nupkgPath = "https://dotnet.myget.org/F/aspnetcore-dev/api/v2/package/Microsoft.AspNetCore.AspNetCoreModuleV2/${aspNetVersion}"
    $tempFile = "$([System.IO.Path]::GetTempFileName()).zip"
    Write-Debug "Fetching $nupkgPath and extract to $tempFile"
    Invoke-WebRequest -Uri $nupkgPath -OutFile $tempFile
    $tempDir = "${tempFile}.extracted"
    Expand-Archive -LiteralPath $tempFile -DestinationPath $tempDir -Force
    foreach ($target in $copyTargets.Keys) {
        $source = Join-Path -Path $tempDir -ChildPath $copyTargets[$target]
        if (!(Test-Path $source)) {
            Throw "$source does not exist"
        }
    }
    foreach ($target in $copyTargets.Keys) {
        $source = Join-Path -Path $tempDir -ChildPath $copyTargets[$target]
        Invoke-BackupFile $target
        Write-Debug "Copying from $source"
        Copy-Item -Path $source -Destination $target -Force
        Write-Output "$target is installed"
    }
}

Import-Module -Name (Join-Path $PSScriptRoot versions.psm1) -Force
$aspNetVersion = GetASPNETVersionUsedByBuild
$scriptTime = Get-Date -f MM-dd-yyyy_HH_mm_ss

Write-Host "Stopping IIS"
Stop-Service -Name WAS -Force

if ($RollbackToTimeStamp) {
    foreach ($target in $copyTargets.Keys + $legacyTargets + $appHostConfigLocation) {
        Invoke-RestoreFile $target
    }
}
else {
    Invoke-UpdateANCM
    Invoke-UpdateAppHostConfig
}

Write-Host "Starting IIS"
Start-Service -Name W3SVC
