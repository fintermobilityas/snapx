param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    $Bootstrap = $false,
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    $Configuration = "Release"
)

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion

$Properties = @()
$Properties = $Properties -join " "

$ToolInstallDir = Join-Path $WorkingDir tools\snapx

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
        Invoke-Command-Colored $CommandDotnet @("tool uninstall --tool-path $ToolInstallDir snapx")
    }
} else {
    Invoke-Command-Colored $CommandDotnet @("tool uninstall -g snapx")
}

Invoke-Command-Colored $CommandDotnet @("clean src/Snapx")
Invoke-Command-Colored $CommandDotnet @("build -c $Configuration src/Snapx -f netcoreapp2.2 $Properties")
Invoke-Command-Colored $CommandDotnet @("pack -c $Configuration src/Snapx --no-build")

$CommandSnapx = $CommandSnapx
if($env:SNAPX_CI_BUILD -eq $true) {
    Invoke-Command-Colored $CommandDotnet @("tool install --tool-path $ToolInstallDir --add-source $NupkgsDir snapx")
    $CommandSnapx = Join-Path $ToolInstallDir $CommandSnapx
} else {
    Invoke-Command-Colored $CommandDotnet @("tool install --global --add-source $NupkgsDir snapx")
}

# Prove that executable is working and able to start. 
# Added bonus is that it will break the CI pipeline if snapx crashses.
Invoke-Command-Colored $CommandSnapx @()
