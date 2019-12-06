param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [bool] $Bootstrap = $false,
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [Validateset(16)]
    [int] $VisualStudioVersion = 16,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [ValidateSet("netcoreapp3.1")]
    [string] $NetCoreAppVersion = "netcoreapp3.1"
)

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion

$Properties = @()

$ToolInstallDir = Join-Path $WorkingDir tools\snapx
$SnapxSrcDir = Join-Path $WorkingDir src/Snapx

$NupkgsDir = Join-Path $WorkingDir nupkgs
if($env:BUILD_ARTIFACTSTAGINGDIRECTORY)
{
    $NupkgsDir = $env:BUILD_ARTIFACTSTAGINGDIRECTORY
}

# Commands

$CommandSnapx = $null
$CommandDotnet = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandSnapx = "snapx.exe"
        $CommandDotnet = "dotnet.exe"

        $Properties += "/p:SnapMsvsToolsetVersion=${VisualStudioVersion}"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandSnapx = "snapx"
        $CommandDotnet = "dotnet"
    }	
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

if($Bootstrap)
{
    $Properties += "/p:SnapBootstrap=true"
}

if($env:SNAPX_CI_BUILD -eq $true) {
    if(Test-Path $ToolInstallDir) {
        Invoke-Command-Colored $CommandDotnet @(
            "tool",
            "uninstall",
            "--tool-path",
            "$ToolInstallDir",
            "snapx"
        )
    }
} else {
    Invoke-Command-Colored $CommandDotnet @(
        "tool",
        "uninstall",
        "--global",
        "snapx"
    )
}

Invoke-Command-Clean-Dotnet-Directory $SnapxSrcDir
Invoke-Command-Colored $CommandDotnet @(
    "build"
    "--configuration $Configuration"
    "$SnapxSrcDir"
    "-f ${NetCoreAppVersion} {0}" -f ($Properties -join " ")
)
Invoke-Command-Colored $CommandDotnet @(
    "pack",
    "--no-build",
    "--output ${NupkgsDir}",
    "--configuration $Configuration",
    "$SnapxSrcDir"
)

$CommandSnapx = $CommandSnapx
if($env:SNAPX_CI_BUILD -eq $true) {
    Invoke-Command-Colored $CommandDotnet @(
        "tool",
        "install", 
        "--tool-path $ToolInstallDir",
        "--add-source $NupkgsDir",
        "snapx"
    )
    $CommandSnapx = Join-Path $ToolInstallDir $CommandSnapx
} else {
    Invoke-Command-Colored $CommandDotnet @(
        "tool",
        "install",
        "--global",
        "--add-source $NupkgsDir",
        "snapx"
    )
}

# Prove that executable is working and able to start. 
# Added bonus is that it will break the CI pipeline if snapx crashses.
Invoke-Command-Colored $CommandSnapx @()
