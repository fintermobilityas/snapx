param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Native", "Snap", "Snap-Installer", "Snapx")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [string] $DockerImagePrefix,
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [bool] $DockerImageNoCache,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [string] $CIBuildStr,
    [Parameter(Position = 4, ValueFromPipeline = $true)]
    [string] $VisualStudioVersionStr = "15"
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
$env:SNAPX_CI_BUILD = $CIBuildStr -eq "YESIAMABOOLEANVALUEAZUREPIPELINEBUG"

# Ref: https://github.com/Microsoft/azure-pipelines-tasks/issues/836
[int] $VisualStudioVersion = 0
if($false -eq [int]::TryParse($VisualStudioVersionStr, [ref] $VisualStudioVersion))
{
    Write-Error "Invalid Visual Studio Version: $VisualStudioVersionStr"
    exit 1
}

$CommandDocker = $null
$CommandDotnet = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandDocker = "docker.exe"
        $CommandDotnet = "dotnet.exe"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandDocker = "docker"
        $CommandDotnet = "dotnet"
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
            
        .\bootstrap.ps1 -Target Native -Configuration Debug -VisualStudioVersion $VisualStudioVersion
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 -VisualStudioVersion $VisualStudioVersion
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        return
    }
    
    if ($OSPlatform -eq "Unix") {

        .\bootstrap.ps1 -Target Native -Configuration Debug 
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Debug -Cross 1
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Cross 1 -Lto 1
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        return	
    }	

    Write-Error "Unsupported OS platform: $OSVersion"
}

function Build-Snap-Installer {
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid linux-x64 -VisualStudioVersion $VisualStudioVersion
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid win-x64 -VisualStudioVersion $VisualStudioVersion
}

function Build-Snap {
    .\bootstrap.ps1 -Target Snap -VisualStudioVersion $VisualStudioVersion
}

function Build-Summary {
    $BuildTime.Stop()		
    $Elapsed = $BuildTime.Elapsed
    Write-Output-Header "Build elapsed: $Elapsed ($OSVersion)"
}

function Invoke-Native-UnitTests
{
    .\bootstrap.ps1 -Target Run-Native-UnitTests -VisualStudioVersion $VisualStudioVersion
}

function Invoke-Docker 
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("Native")]
        [string] $Entrypoint
    )

    Write-Output-Header "Docker entrypoint: $Entrypoint"
    
    $env:SNAPX_DOCKER_USERNAME = [Environment]::UserName
    $env:SNAPX_DOCKER_WORKING_DIR = "/build/snapx"
    $env:SNAPX_DOCKER_BUILD=1

    if ($OSPlatform -eq "Unix") {
        $env:SNAPX_DOCKER_USER_ID = (id -u $Username | Out-String) -replace [System.Environment]::NewLine, ""
        $env:SNAPX_DOCKER_GROUP_ID = (id -g $Username | Out-String) -replace [System.Environment]::NewLine, ""
    } else {
        $env:SNAPX_DOCKER_USER_ID = 0
        $env:SNAPX_DOCKER_GROUP_ID = 0
    }

    Resolve-Shell-Dependency $CommandDocker

    $DockerContainerName = "snapx"
    if($DockerImagePrefix)
    {
        $DockerContainerName += "-$DockerImagePrefix"
    }

    $DockerRunFlags = "-it"

    if($env:SNAPX_CI_BUILD -eq $true) {
        $DockerRunFlags = "-i"
    }
    
    if($Entrypoint -eq "Native")
    {
        $DockerBuildNoCache = ""
        if($DockerImageNoCache)
        {
            $DockerBuildNoCache = "--no-cache"
        }

        Invoke-Command-Colored $CommandDocker @(
            "build $DockerBuildNoCache -t $DockerContainerName {0}" -f (Join-Path $WorkingDir docker)
        )    
    }

    Invoke-Command-Colored $CommandDocker @( 
        "run"
        "--rm $DockerRunFlags --name $DockerContainerName"
        "-e ""SNAPX_DOCKER_USERNAME=${env:SNAPX_DOCKER_USERNAME}"""
        "-e ""SNAPX_DOCKER_USER_ID=${env:SNAPX_DOCKER_USER_ID}"""
        "-e ""SNAPX_DOCKER_GROUP_ID=${env:SNAPX_DOCKER_GROUP_ID}"""
        "-e ""SNAPX_DOCKER_WORKING_DIR=${env:SNAPX_DOCKER_WORKING_DIR}"""               
        "-e ""SNAPX_DOCKER_ENTRYPOINT=$Entrypoint"""               
        "-e ""SNAPX_DOCKER_HOST_OS=$OSPlatform"""               
        "-e ""SNAPX_DOCKER_VISUAL_STUDIO_VERSION=$VisualStudioVersion"""               
        "-v ${WorkingDir}:${env:SNAPX_DOCKER_WORKING_DIR}"
        "$DockerContainerName"
    )

    $env:SNAPX_DOCKER_BUILD=0

    Write-Output-Header "Docker container entrypoint ($Entrypoint) finished"

    if($LASTEXITCODE -ne 0)
    {
        Write-Error "Docker entrypoint finished with errors. Exit code: $LASTEXITCODE"
        exit $DockerRunExitCode
    }
}

function Build-Native-And-Run-Native-UnitTests
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

function Build-Docker-Entrypoint
{
    Build-Native  
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

function Build-Snapx
{
    Write-Output-Header "Building snapx"

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

function Invoke-Dotnet-Unit-Tests
{
    Write-Output-Header "Running dotnet tests"

    Push-Location $WorkingDir

    Resolve-Shell-Dependency $CommandDotnet
    Invoke-Command-Colored $CommandDotnet @("test src --logger trx")
}

switch ($Target) {
    "Bootstrap"{
        Invoke-Docker -Entrypoint "Native"
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        if($OSPlatform -eq "Windows")
        {
            Build-Native 
        }

        Invoke-Native-UnitTests    
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }        

        Build-Snapx
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }        

        Invoke-Dotnet-Unit-Tests
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }    

        Build-Summary
        exit 0 
    }    
    "Native" {
        Build-Native
        Build-Summary
    }
    "Native-UnitTests"
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
    "Snapx" {
        Build-Snapx
        Build-Summary
    }
}
