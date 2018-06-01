
function GetASPNETVersionUsedByBuild() {
    $dependenciesPropFile = [System.IO.Path]::Combine($PSScriptRoot, "..", "build", "dependencies.props")
    [xml]$xmldata = Get-Content $dependenciesPropFile
    $netCoreAppVersion = (Select-Xml -Xml $xmlData -XPath "/Project/PropertyGroup[@Label='Package Versions']/MicrosoftAspNetCoreAllPackageVersion/text()").Node.Value
    return $netCoreAppVersion
}
