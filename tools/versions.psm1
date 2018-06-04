#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

<#
.SYNOPSIS
    Determine current ASP.NET core version
.DESCRIPTION
    Determine current ASP.NET core referred by build/dependencies.props
#>
function GetASPNETVersionUsedByBuild() {
    $dependenciesPropFile = [System.IO.Path]::Combine($PSScriptRoot, "..", "build", "dependencies.props")
    [xml]$xmldata = Get-Content $dependenciesPropFile
    return (Select-Xml -Xml $xmlData -XPath "/Project/PropertyGroup[@Label='Package Versions']/MicrosoftAspNetCoreAllPackageVersion/text()").Node.Value
}
