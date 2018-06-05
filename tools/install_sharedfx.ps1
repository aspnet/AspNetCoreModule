#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

<#
.SYNOPSIS
    Install sharefx for the current ASP.NET core referred by build/dependencies.props
.DESCRIPTION
    The script ensure that the ASP.NET shared framework is in place
.PARAMETER dropLocation
    The location of where the sharefx build drops can be found
#>


param(
    [Parameter(Mandatory = $true)]
    [string]
    $dropLocation,

    [Alias('d')]
    [string]
    $dotnetHome = (Join-Path $env:USERPROFILE ".dotnet")
)
$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path $PSScriptRoot versions.psm1) -Force

function InstallDotnet([string] $arch) {
    $archiveFile = Join-Path $dropLocation 'aspnetcore-runtime-${netCoreAppVersion}-win-${arch}.zip'.Replace('${netCoreAppVersion}',$netCoreAppVersion).Replace('${arch}',$arch)
    $dotnetInstallLocation = Join-Path $dotnetHome $arch
    Write-Debug "Installing $archiveFile to $dotnetInstallLocation"
    Expand-Archive -LiteralPath $archiveFile -DestinationPath $dotnetInstallLocation -Force
    Write-Debug "Done installing SharedFX for $arch"
}

$netCoreAppVersion = GetASPNETVersionUsedByBuild

$buildNumber = ($netCoreAppVersion -split "-")[-1]
$dropLocation = $dropLocation.Replace('${netCoreAppVersion}',$netCoreAppVersion).Replace('${buildNumber}',$buildNumber)

InstallDotnet "x86"
InstallDotnet "x64"
