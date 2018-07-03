#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

<#
.SYNOPSIS
    Install Windows Hosting Bundle referred by build/dependencies.props
.DESCRIPTION
    The script ensure that the ASP.NET shared framework is in place
.PARAMETER dropLocation
    The location of where the sharefx build drops can be found
#>

param(
    [Parameter(Mandatory = $true)]
    [string]
    $dropLocation,

    [switch]
    $uninstall
)


$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path $PSScriptRoot versions.psm1) -Force

$netCoreAppVersion = GetASPNETVersionUsedByBuild
$buildNumber = ($netCoreAppVersion -split "-")[-1]
$dropLocation = $dropLocation.Replace('${netCoreAppVersion}',$netCoreAppVersion).Replace('${buildNumber}',$buildNumber)

if ($uninstall) {
    & $dropLocation /uninstall /quiet
} else {
    & $dropLocation /install /quiet
}
