param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Bootstrap-Docker", "Bootstrap-Docker-After", "Native", "Native-Docker", "Snap", "Snap-Installer", "Run-Native-UnitTests")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [string] $DockerAzureImageName,
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [bool] $DockerImageNoCache,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [string] $DockerAzurePipelineBuildStr
)

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]

# Ref: https://github.com/Microsoft/azure-pipelines-tasks/issues/836
$DockerAzurePipelineBuild = $DockerAzurePipelineBuildStr -eq "YESIAMABOOLEANVALUEAZUREPIPELINEBUG"

$CommandDocker = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandDocker = "docker.exe"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandDocker = "docker"
    }	
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

# Configuration

if($false -eq (Test-Path "env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE"))
{
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
}
if($false -eq (Test-Path "env:DOTNET_CLI_TELEMETRY_OPTOUT"))
{
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
}
if($false -eq (Test-Path "env:NUGET_XMLDOC_MODE"))
{
    $env:NUGET_XMLDOC_MODE = "skip"
}

$env:SNAPX_WAIT_DEBUGGER=0 

if($false -eq (Test-Path 'env:SNAPX_DOCKER_BUILD'))
{
    $env:SNAPX_DOCKER_BUILD = 0
}

# Actions

$BuildTime = $StopWatch::StartNew()

function Build-Native {
    if ($OSPlatform -eq "Windows") {

        $UbuntuExe = @("Ubuntu1804.exe", "Ubuntu1604.exe") | 
                        ForEach-Object { Get-Command $_ } | 
                        Select-Object -First 1 | 
                        Select-Object -ExpandProperty Source
		if($UbuntuExe)
		{
            . $UbuntuExe run pwsh -f build.ps1 -Target Native
		} else {
			Write-Error "Unable to find a working ubuntu installation on this computer. Please install Ubuntu 1804 LTS in the Microsoft Store"
        }
		
        if ($LASTEXITCODE -ne 0) {
            exit 0
        }

        Invoke-Native-UnitTests
    
        .\bootstrap.ps1 -Target Native -Configuration Debug 
        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 	

        return
    }
    
    if ($OSPlatform -eq "Unix") {
        .\bootstrap.ps1 -Target Native -Configuration Debug
        .\bootstrap.ps1 -Target Native -Configuration Debug -Cross 1
        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1		
        .\bootstrap.ps1 -Target Native -Configuration Release -Cross 1 -Lto 1	
        return	
    }	

    Write-Error "Unsupported OS platform: $OSVersion"
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

function Invoke-Native-UnitTests
{
    .\bootstrap.ps1 -Target Run-Native-UnitTests
}

function Build-Docker {
    $env:SNAPX_DOCKER_USERNAME = (whoami | Out-String) -replace [System.Environment]::NewLine, ""
    $env:SNAPX_DOCKER_WORKING_DIR = "/build/snapx"

    if ($OSPlatform -eq "Unix") {
        $env:SNAPX_DOCKER_USER_ID = (id -u $Username | Out-String) -replace [System.Environment]::NewLine, ""
        $env:SNAPX_DOCKER_GROUP_ID = (id -g $Username | Out-String) -replace [System.Environment]::NewLine, ""
    } else {
        $env:SNAPX_DOCKER_USER_ID = 0
        $env:SNAPX_DOCKER_GROUP_ID = 0
    }

    Resolve-Shell-Dependency $CommandDocker

    $DockerContainerName = "snapx{0}" -f $DockerAzureImageName
    $DockerBuildNoCache = ""
    $DockerRunFlags = "-it"

    if($DockerImageNoCache)
    {
        $DockerBuildNoCache = "--no-cache"
    }

    if($DockerAzurePipelineBuild) {
        $DockerRunFlags = "-i"
    }
    
    Invoke-Command-Colored $CommandDocker @(
        "build $DockerBuildNoCache -t $DockerContainerName {0}" -f (Join-Path $WorkingDir docker)
    )

    Invoke-Command-Colored $CommandDocker @( 
        "run"
        "--rm $DockerRunFlags --name $DockerContainerName"
        "-e ""SNAPX_DOCKER_USERNAME=${env:SNAPX_DOCKER_USERNAME}"""
        "-e ""SNAPX_DOCKER_USER_ID=${env:SNAPX_DOCKER_USER_ID}"""
        "-e ""SNAPX_DOCKER_GROUP_ID=${env:SNAPX_DOCKER_GROUP_ID}"""
        "-e ""SNAPX_DOCKER_WORKING_DIR=${env:SNAPX_DOCKER_WORKING_DIR}"""
        "-v ${WorkingDir}:${env:SNAPX_DOCKER_WORKING_DIR}"
        "$DockerContainerName"
    )

    Write-Output-Header "Docker build finished"
}

function Build-Docker-Native-Dependencies
{
    Build-Native
    if(0 -ne $LASTEXITCODE) {
        return
    }
      
    Invoke-Native-UnitTests
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

function Build-Docker-Snapx
{
    .\install_snapx.ps1 -Bootstrap $true
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Build-Snap-Installer
    if(0 -ne $LASTEXITCODE) {
        return
    }

    .\install_snapx.ps1 -Bootstrap $false
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Build-Snap
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

switch ($Target) {
    "Bootstrap" {        
        if(0 -eq $env:SNAPX_DOCKER_BUILD) {

            Build-Docker
            if(0 -ne $LASTEXITCODE) {
                Write-Error "Docker build failed unexpectedly"
                exit 1
            }

            if($OSPlatform -eq "Windows") {
                Build-Docker-Native-Dependencies
                if(0 -ne $LASTEXITCODE) {
                    Write-Error "Unknown error bootstrapping msvs build"
                    exit 1
                }

                Build-Docker-Snapx
                if(0 -ne $LASTEXITCODE) {
                    Write-Error "Unknown error bootstrapping snapx after native build"
                    exit 1
                }
            }
            
            Build-Summary
            exit 0 
        }        

        Write-Output-Header "Building using docker"

        if($OSPlatform -eq "Windows")
        {
            Write-Error "Fatal error! Expected to be 'inside' docker container by now."
            exit 1
        }

        Build-Docker-Native-Dependencies
        if(0 -ne $LASTEXITCODE) {
            Write-Error "Docker bootstrap failed unexpectedly"
            exit 1
        }

        Build-Docker-Snapx
        if(0 -ne $LASTEXITCODE) {
            Write-Error "Unknown error bootstrapping snapx after native build"
            exit 1
        }

        Build-Summary
        exit 0 
    }
    "Native" {
        Build-Native
        Build-Summary
    }
    "Run-Native-UnitTests"
    {
        Invoke-Native-UnitTests
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
