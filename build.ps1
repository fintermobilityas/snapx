param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Native", "Snap", "Snap-Installer", "Run-Native-Mingw-UnitTests-Windows")]
    [string] $Target = "Bootstrap"
)

# Configuration
$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 
$WorkingDir = Split-Path $MyInvocation.MyCommand.Path -Parent

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:NUGET_XMLDOC_MODE = "skip"

# Global variables

$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]

# Global functions
function Write-Output-Header {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Green
    Write-Host
}

# Actions

$BuildTime = $StopWatch::StartNew()

function Build-Native {
    if ($OSVersion -match "^Microsoft Windows") {
        # NB! Install from Microsoft Store. Afterwards run install.sh
        Ubuntu1804 run pwsh -f build.ps1 -Target Native

        if ($LASTEXITCODE -ne 0) {
            exit 0
        }

        Run-Native-Mingw-UnitTests-Windows
    
        .\bootstrap.ps1 -Target Native -Configuration Debug 
        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 	
    }
    
    if ($OSVersion -match "^Unix") {
        .\bootstrap.ps1 -Target Native -Configuration Debug
        .\bootstrap.ps1 -Target Native -Configuration Debug -Cross 1
        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1		
        .\bootstrap.ps1 -Target Native -Configuration Release -Cross 1 -Lto 1		
    }	
}

function Build-Snap-Installer {
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid linux-x64
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid win-x64
}

function Build-Snap {
    .\bootstrap.ps1 -Target Snap
}

function Build-Summary {
    $BuildTime.Stop()		
    $Elapsed = $BuildTime.Elapsed
    Write-Output-Header "Build elapsed: $Elapsed ($OSVersion)"
}

function Run-Native-Mingw-UnitTests-Windows
{
    .\bootstrap.ps1 -Target Run-Native-Mingw-UnitTests-Windows -Configuration Debug 
    .\bootstrap.ps1 -Target Run-Native-Mingw-UnitTests-Windows -Configuration Release 
}

switch ($Target) {
    "Bootstrap" {
        Build-Native
        .\install_snapx.ps1 -Bootstrap $true
        Build-Snap-Installer
        .\install_snapx.ps1 -Bootstrap $false
        Build-Snap
        Build-Summary
    }
    "Native" {
        Build-Native				
        Build-Summary
    }
    "Run-Native-Mingw-UnitTests-Windows"
    {
        Run-Native-Mingw-UnitTests-Windows
        Build-Summary
    }
    "Snap" {
        Build-Snap
        Build-Summary
    }
    "Snap-Installer" {        
        Build-Snap-Installer
        Build-Summary
    }
}
