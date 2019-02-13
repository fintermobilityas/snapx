param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Native", "Snap-Installer")]
    [string] $Target = "Native"
)

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

function Build-Native
{
    if ($OSVersion -match "^Microsoft Windows") {
        # NB! Install from Microsoft Store. Afterwards run install.sh
        Ubuntu1804 run pwsh -f build.ps1 -Target Native

        if($LASTEXITCODE -ne 0)
        {
            exit 0
        }

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

function Build-Snap-Installer 
{
    if ($OSVersion -match "^Microsoft Windows") {
        Ubuntu1804 run pwsh -f build.ps1 -Target Snap-Installer

        if($LASTEXITCODE -ne 0)
        {
            exit 0
        }
   }
           
    .\bootstrap.ps1 -Target Snap-Installer -Configuration Debug
    .\bootstrap.ps1 -Target Snap-Installer -Configuration Release 
}

function Build-Summary 
{
    $BuildTime.Stop()		
    $Elapsed = $BuildTime.Elapsed
    Write-Output-Header "Build elapsed: $Elapsed ($OSVersion)"
}

switch ($Target) {
    "Bootstrap" {
        Build-Native
        Build-Snap-Installer
        Build-Summary
    }
    "Native" {
        Build-Native				
        Build-Summary
    }
	"Snap-Installer" {        
        Build-Snap-Installer
        Build-Summary
	}
}
