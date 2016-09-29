<#
.SYNOPSIS
    Installs asnetcore to IISExpress and IIS directory
.DESCRIPTION
    Installs asnetcore to IISExpress and IIS directory
.PARAMETER PackagePath
    Default: E:\temp
    Root path where ANCM nuget package is placed
.PARAMETER ExtractFilesTo
    Default: E:\temp\output"
    Output path where aspentcore.dll file is extracted
#>
[cmdletbinding()]
param(
   [string]$PackagePath="$PSScriptRoot\..\..\artifacts",
   [string]$ExtractFilesTo="$PSScriptRoot\..\..\artifacts"
)

function Get-ANCMNugetFilePath() { 

    $NugetFilePath = Get-ChildItem $PackagePath -Recurse -Filter Microsoft.AspNetCore.AspNetCoreModule*.nupkg | Select-Object -Last 1
    return ($NugetFilePath.FullName)
}

function Check-TargetFiles() { 
    $functionName = "Check-TargetFiles"
    $LogHeader = "[$ScriptFileName::$functionName]"
    $result = $true

    if (-not (Test-Path $aspnetCorex64To))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCorex64To")
        $result = $false
    }
    if (-not (Test-Path $aspnetCoreSchemax64To))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreSchemax64To")
        $result = $false
    }
    if ($is64BitMachine)
    {
        if (-not (Test-Path $aspnetCoreWin32To))
        {
            Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreWin32To")
            $result = $false
        }    
        if (-not (Test-Path $aspnetCoreSchemaWin32To))
        {
            Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreSchemaWin32To")
            $result = $false
        }  
    }

    if ($isIISInstalled)
    {
        if (-not (Test-Path $aspnetCorex64IISTo))
        {
            Say ("$LogHeader Error!!! Failed to find the file $aspnetCorex64IISTo")
            $result = $false
        }
        if (-not (Test-Path $aspnetCoreSchemax64IISTo))
        {
            Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreSchemax64IISTo")
            $result = $false
        }
        if ($is64BitMachine)
        {
            if (-not (Test-Path $aspnetCoreWin32IISTo))
            {
                Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreWin32IISTo")
                $result = $false
            }
        }
    }

    return $result
}

function Check-ExtractedFiles() { 
    $functionName = "Check-ExtractedFiles"
    $LogHeader = "[$ScriptFileName::$functionName]"
    $result = $true

    if (-not (Test-Path $aspnetCorex64From))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCorex64From")
        $result = $false
    }
    if (-not (Test-Path $aspnetCoreWin32From))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreWin32From")
        $result = $false
    }
    if (-not (Test-Path $aspnetCoreSchemax64From))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreSchemax64From")
        $result = $false
    }
    if (-not (Test-Path $aspnetCoreSchemaWin32From))
    {
        Say ("$LogHeader Error!!! Failed to find the file $aspnetCoreSchemaWin32From")
        $result = $false
    }
    return $result
}

function Extract-ANCMFromNugetPackage() { 
    $result = $true

    $functionName = "Extract-ANCMFromNugetPackage"
    $LogHeader = "[$ScriptFileName::$functionName]"

    $backupAncmNugetFilePath = Join-Path $TempExtractFilesTo (get-item $ancmNugetFilePath).Name
    if (Test-Path $backupAncmNugetFilePath)
    {
        Say ("$LogHeader Found backup file at $backupAncmNugetFilePath")
        if ((get-item $ancmNugetFilePath).LastWriteTime -eq (get-item $backupAncmNugetFilePath).LastWriteTime)
        {
            if (Check-ExtractedFiles)
            {
                Say ("$LogHeader Skip to extract ANCM files because $ancmNugetFilePath is matched to the backup file $backupAncmNugetFilePath.")
                return $result
            }
        }
    }

    Add-Type -Assembly System.IO.Compression.FileSystem
    if (Test-Path $TempExtractFilesTo)
    {
        remove-item $TempExtractFilesTo -Force -Recurse -Confirm:$false | out-null
    }
    if (Test-Path $TempExtractFilesTo)
    {
        Say ("$LogHeader Error!!! Failed to delete $TempExtractFilesTo")
        $result = $false
        return $result
    }
    else
    {
        new-item -Type directory $TempExtractFilesTo | out-null
    }
    if (-not (Test-Path $TempExtractFilesTo))
    {
        Say ("$LogHeader Error!!! Failed to create $TempExtractFilesTo")
        $result = $false
        return $result
    }

    # 
    Say ("$LogHeader Extract the ancm nuget file $ancmNugetFilePath to $TempExtractFilesTo ...")
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ancmNugetFilePath, $TempExtractFilesTo) 

    Say ("$LogHeader Create the backup file of the nuget file to $backupAncmNugetFilePath")
    copy-item $ancmNugetFilePath $backupAncmNugetFilePath

    return $result
}

function Update-ANCM() { 

    $functionName = "Update-ANCM"
    $LogHeader = "[$ScriptFileName::$functionName]"

    if ($is64BitMachine)
    {
        Say ("$LogHeader Start updating ANCM files for IISExpress for amd64 machine...")
        Update-File $aspnetCorex64From $aspnetCorex64To
        Update-File $aspnetCoreWin32From $aspnetCoreWin32To
        Update-File $aspnetCoreSchemax64From $aspnetCoreSchemax64To
        Update-File $aspnetCoreSchemaWin32From $aspnetCoreSchemaWin32To
    }
    else
    {
        Say ("$LogHeader Start updating ANCM files for IISExpress for x86 machine...")
        Update-File $aspnetCoreWin32From $aspnetCorex64To
        Update-File $aspnetCoreSchemaWin32From $aspnetCoreSchemax64To
    }

    if ($isIISInstalled)
    {
        if ($is64BitMachine)
        {
            Say ("$LogHeader Start updating ANCM files for IIS for amd64 machine...")
            Update-File $aspnetCorex64From $aspnetCorex64IISTo
            Update-File $aspnetCoreWin32From $aspnetCoreWin32IISTo
            Update-File $aspnetCoreSchemax64From $aspnetCoreSchemax64IISTo
        }
        else
        {
            Say ("$LogHeader Start updating ANCM files for IIS for x86 machine...")
            Update-File $aspnetCoreWin32IISFrom $aspnetCorex64IISTo                
            Update-File $aspnetCoreSchemaWin32From $aspnetCoreSchemax64IISTo
        }
    }
}

function Update-File([string]$Source, [string]$Destine) { 

    $functionName = "Update-File"
    $LogHeader = "[$ScriptFileName::$functionName]"

    if (-Not (Test-Path $Source))
    {
        throw ("$LogHeader Can't find $Source")
    }

    if (-Not (Test-Path $Destine))
    {
        throw ("$LogHeader Can't find $Destine")
    }

    $fileMatched = $null -eq (Compare-Object -ReferenceObject $(Get-Content $Source) -DifferenceObject $(Get-Content $Destine))
    if (-not $fileMatched)
    {
        Say ("    Copying $Source to $Desting...")
        Copy-Item $Source $Destine -Force
    }
    else
    {
        Say ("    Skipping the file $Destine that is already identical to $Source ")
    }

    # check file is correctly copied
    $fileMatched = $null -eq (Compare-Object -ReferenceObject $(Get-Content $Source) -DifferenceObject $(Get-Content $Destine))
    if (-not $fileMatched)
    {
        throw ("$LogHeader File not matched!!! $Source $Destine")
    }
    else
    {
        Say-Verbose ("$LogHeader File matched!!! $Source to $Destine")
    }
}

function Say($str) {
    Write-Host $str
}

function Say-Verbose($str) {
    Write-Verbose $str
}

#######################################################
# Start execution point
#######################################################

$ScriptFileName = "installancm.ps1"
$LogHeader = "[$ScriptFileName]"

if (-not (Test-Path $PackagePath))
{
    Say ("$LogHeader Error!!! Failed to find the directory $PackagePath")
    exit 1
}
if (-not (Test-Path $ExtractFilesTo))
{
    Say ("$LogHeader Error!!! Failed to find the directory $ExtractFilesTo")
    exit 1
}

$ancmNugetFilePath = Get-ANCMNugetFilePath
if (-not (Test-Path $ancmNugetFilePath))
{
    Say ("$LogHeader Error!!! Failed to find AspNetCoreModule nupkg file under $PackagePath nor its child directories")
    exit 1
}

$TempExtractFilesTo = $ExtractFilesTo + "\.ancm"
$ExtractFilesRootPath = $TempExtractFilesTo + "\ancm\Debug"
$aspnetCorex64From = $ExtractFilesRootPath + "\x64\aspnetcore.dll"
$aspnetCoreWin32From = $ExtractFilesRootPath + "\Win32\aspnetcore.dll"
$aspnetCoreSchemax64From = $ExtractFilesRootPath + "\x64\aspnetcore_schema.xml"
$aspnetCoreSchemaWin32From = $ExtractFilesRootPath + "\Win32\aspnetcore_schema.xml"

$aspnetCorex64To = "$env:ProgramFiles\IIS Express\aspnetcore.dll"
$aspnetCoreWin32To = "${env:ProgramFiles(x86)}\IIS Express\aspnetcore.dll"
$aspnetCoreSchemax64To = "$env:ProgramFiles\IIS Express\config\schema\aspnetcore_schema.xml"
$aspnetCoreSchemaWin32To = "${env:ProgramFiles(x86)}\IIS Express\config\schema\aspnetcore_schema.xml"

$aspnetCorex64IISTo = "$env:windir\system32\inetsrv\aspnetcore.dll"
$aspnetCoreWin32IISTo = "$env:windir\syswow64\inetsrv\aspnetcore.dll"
$aspnetCoreSchemax64IISTo = "$env:windir\system32\inetsrv\config\schema\aspnetcore_schema.xml"

$is64BitMachine = $env:PROCESSOR_ARCHITECTURE.ToLower() -eq "amd64"
$isIISInstalled = Test-Path $aspnetCorex64IISTo

# Check expected files are available on IIS/IISExpress directory
if (-not (Check-TargetFiles))
{
    Say ("$LogHeader Error!!! Failed to update ANCM files because AspnetCore.dll is not installed on IIS/IISExpress directory.")
    exit 1
}

# Extrack nuget package
if (-not (Extract-ANCMFromNugetPackage))
{
    Say ("$LogHeader Error!!! Failed to update ANCM files")
    exit 1
}

# clean up IIS and IISExpress worker processes
Say ("$LogHeader Stopping w3wp.exe process")
Stop-Process -Name w3wp -ErrorAction Ignore

Say ("$LogHeader Stopping iisexpress.exe process")
Stop-Process -Name iisexpress -ErrorAction Ignore

# Update ANCM files for IIS and IISExpress
Update-ANCM 

Say ("$LogHeader Installation finished")
exit 0
