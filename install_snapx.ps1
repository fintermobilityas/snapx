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
$ToolInstallDir = Join-Path $WorkingDir snapx_ci_install

$CommandSnapx = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandSnapx = "snapx.exe"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandSnapx = "snapx"
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
        Invoke-Command-Colored dotnet @("tool uninstall --tool-path $ToolInstallDir snapx")
    }
} else {
    Invoke-Command-Colored dotnet @("tool uninstall -g snapx")
}

Invoke-Command-Colored dotnet @("clean src/Snapx")
Invoke-Command-Colored dotnet @("build -c $Configuration src/Snapx -f netcoreapp2.2 $Properties")
Invoke-Command-Colored dotnet @("pack -c $Configuration src/Snapx --no-build")

$CommandSnapx = $CommandSnapx
if($env:SNAPX_CI_BUILD -eq $true) {
    Invoke-Command-Colored dotnet @("tool install --tool-path $ToolInstallDir --add-source ./nupkgs snapx")
    $CommandSnapx = Join-Path $ToolInstallDir $CommandSnapx
} else {
    Invoke-Command-Colored dotnet @("tool install --global --add-source ./nupkgs snapx")
}

# Prove that executable is working and able to start. 
# Added bonus is that it will break the CI pipeline if snapx crashses.
Invoke-Command-Colored $CommandSnapx @()
