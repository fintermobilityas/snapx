param(
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerBuild,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [Validateset(16)]
    [int] $VisualStudioVersion = 16,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("netcoreapp3.1")]
    [string] $NetCoreAppVersion = "netcoreapp3.1",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $Version = "0.0.0",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $Rid = "any"
)

$ErrorActionPreference = "Stop";
$ConfirmPreference = "None";

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion

$Properties = @(
    "/p:SnapRid=$Rid"
)

$SnapxSrcDir = Join-Path $WorkingDir src/Snapx
$NupkgsDir = Join-Path $WorkingDir nupkgs

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

Invoke-Command-Clean-Dotnet-Directory $SnapxSrcDir

Invoke-Command-Colored $CommandDotnet @(
    "tool",
    "uninstall",
    "--global",
    "snapx"
) -IgnoreExitCode

Invoke-Command-Colored $CommandDotnet @(
    "build"
    "/p:Version=$Version"
    "--configuration $Configuration"
    "$SnapxSrcDir"
    "-f ${NetCoreAppVersion} {0}" -f ($Properties -join " ")
)

Invoke-Command-Colored $CommandDotnet @(
    "pack",
    "/p:PackageVersion=$Version"
    "--no-build",
    "--output ${NupkgsDir}"
    "--configuration $Configuration"
    "$SnapxSrcDir"
)

Invoke-Command-Colored $CommandDotnet @(
    "tool"
    "update"
    "snapx"
    "--global"
    "--add-source $NupkgsDir"
    "--version $Version"
)

Resolve-Shell-Dependency $CommandSnapx