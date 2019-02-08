param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap")]
    [string] $Target = "Bootstrap"
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


switch ($Target) {
    "Bootstrap" {
		
        if ($OSVersion -match "^Microsoft Windows") {
            # NB! Install from Microsoft Store. Afterwards run install.sh
            Ubuntu1804 run pwsh -f build.ps1 -Target Bootstrap

            .\bootstrap.ps1 -Configuration Debug
            .\bootstrap.ps1 -Configuration Release -Lto 1
			
        }
		
        if ($OSVersion -match "^Unix") {
            .\bootstrap.ps1 -Configuration Debug
            .\bootstrap.ps1 -Configuration Debug -Cross 1
            .\bootstrap.ps1 -Configuration Release -Lto 1		
            .\bootstrap.ps1 -Configuration Release -Cross 1 -Lto 1		
        }	
		
        $BuildTime.Stop()		
        $Elapsed = $BuildTime.Elapsed
        Write-Output-Header "Build elapsed: $Elapsed ($OSVersion)"
    }
}
