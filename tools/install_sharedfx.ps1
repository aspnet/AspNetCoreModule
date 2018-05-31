param(
    [string]
    $netCoreAppVersion,

    [Parameter(Mandatory = $true)]
    [string]
    $dropLocation
)

function InstallDotnet([string] $arch) {
    $archiveFile = Join-Path $dropLocation 'aspnetcore-runtime-${netCoreAppVersion}-win-${arch}.zip'.Replace('${netCoreAppVersion}',$netCoreAppVersion).Replace('${arch}',$arch)
    $dotnetInstallLocation = [System.IO.Path]::Combine($env:USERPROFILE, ".dotnet", $arch)
    Write-Debug "Installing $archiveFile to $dotnetInstallLocation"
    Expand-Archive -LiteralPath $archiveFile -DestinationPath $dotnetInstallLocation -Force
    Write-Debug "Done installing SharedFX for $arch"
}

if (!$netCoreAppVersion) {
    $dependenciesPropFile = [System.IO.Path]::Combine($PSScriptRoot, "..", "build", "dependencies.props")
    [xml]$xmldata = Get-Content $dependenciesPropFile
    $netCoreAppVersion = (Select-Xml -Xml $xmlData -XPath "/Project/PropertyGroup[@Label='Package Versions']/MicrosoftAspNetCoreAllPackageVersion/text()").Node.Value
}

$buildNumber = ($netCoreAppVersion -split "-")[-1]
$dropLocation = $dropLocation.Replace('${netCoreAppVersion}',$netCoreAppVersion).Replace('${buildNumber}',$buildNumber)

InstallDotnet "x86"
InstallDotnet "x64"
