param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Native", "Snap", "Snap-Installer", "Snapx", "Run-Native-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [string] $DockerImagePrefix,
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [bool] $DockerImageNoCache,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [string] $CIBuild,
    [Parameter(Position = 4, ValueFromPipeline = $true)]
    [string] $VisualStudioVersionStr = "16",
    [Parameter(Position = 5, ValueFromPipeline = $true)]
    [ValidateSet("netcoreapp3.0")]
    [string] $NetCoreAppVersion = "netcoreapp3.0"
)

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
$ToolsDir = Join-Path $WorkingDir tools
. $WorkingDir\common.ps1

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]

# Ref: https://github.com/Microsoft/azure-pipelines-tasks/issues/836
$env:SNAPX_CI_BUILD = Get-Is-String-True $CIBuild
[int] $VisualStudioVersion = 0
if($false -eq [int]::TryParse($VisualStudioVersionStr, [ref] $VisualStudioVersion))
{
    Write-Error "Invalid Visual Studio Version: $VisualStudioVersionStr"
    exit 1
}

$CommandDocker = $null
$CommandSigntool = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandDocker = "docker.exe"
        $CommandSigntool = Join-Path $ToolsDir signtool-win-x64.exe
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

$SummaryStopwatch = $Stopwatch::StartNew()
$SummaryStopwatch.Restart()

function Invoke-Build-Native {
    if ($OSPlatform -eq "Windows") {
            
        if($env:SNAPX_CI_BUILD -eq $false) {
            .\bootstrap.ps1 -Target Native -Configuration Debug -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        return
    }
    
    if ($OSPlatform -eq "Unix") {

        if($env:SNAPX_CI_BUILD -eq $false) {
            .\bootstrap.ps1 -Target Native -Configuration Debug -NetCoreAppVersion $NetCoreAppVersion
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }

            .\bootstrap.ps1 -Target Native -Configuration Debug -Cross 1 -NetCoreAppVersion $NetCoreAppVersion
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 -NetCoreAppVersion $NetCoreAppVersion
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Cross 1 -Lto 1 -NetCoreAppVersion $NetCoreAppVersion
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        return	
    }	

    Write-Error "Unsupported OS platform: $OSVersion"
}

function Invoke-Build-Snap-Installer {
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid linux-x64 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid win-x64 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion

    if($env:SNAPX_CI_BUILD -eq $false -and ($OSPlatform -eq "Windows")) {
        $DotnetBuildDir = Join-Path $WorkingDir build\dotnet\win-x64\Snap.Installer\netcoreapp2.2

        $Executables = @(
            "$DotnetBuildDir/Release/publish/Snap.Installer.exe"
        ) -join " "

        Invoke-Command-Colored $CommandSigntool @("sign /a /fd sha256 /t http://timestamp.digicert.com /sha1 F57A7A2A8DFBDA7D7211B81C755150A8D994CD48 /a $Executables")

        if(0 -ne $LASTEXITCODE) {
            return
        }
    }

}

function Invoke-Build-Snap {
    .\bootstrap.ps1 -Target Snap -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
}

function Invoke-Summary {
    $SummaryStopwatch.Stop()		
    $Elapsed = $SummaryStopwatch.Elapsed
    Write-Output-Header "Operation completed in: $Elapsed ($OSVersion)"
}

function Invoke-Native-UnitTests
{
    .\bootstrap.ps1 -Target Run-Native-UnitTests -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
}

function Invoke-Dotnet-Unit-Tests
{
    .\bootstrap.ps1 -Target Run-Dotnet-UnitTests -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
}

function Invoke-Docker 
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("Native", "Run-Native-UnitTests")]
        [string] $Entrypoint
    )

    Write-Output-Header "Docker entrypoint: $Entrypoint"
    
    $DockerContainerName = "snapx"
    if($DockerImagePrefix)
    {
        $DockerContainerName += "-$DockerImagePrefix"
    }

    $env:SNAPX_DOCKER_USERNAME = [Environment]::UserName
    $env:SNAPX_DOCKER_WORKING_DIR = "/build/$DockerContainerName"
    $env:SNAPX_DOCKER_BUILD=1

    if ($OSPlatform -eq "Unix") {
        $env:SNAPX_DOCKER_USER_ID = (id -u $Username | Out-String) -replace [System.Environment]::NewLine, ""
        $env:SNAPX_DOCKER_GROUP_ID = (id -g $Username | Out-String) -replace [System.Environment]::NewLine, ""
    } else {
        $env:SNAPX_DOCKER_USER_ID = 0
        $env:SNAPX_DOCKER_GROUP_ID = 0
    }

    Resolve-Shell-Dependency $CommandDocker

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
        "-e ""SNAPX_DOCKER_CI_BUILD=${env:SNAPX_CI_BUILD}"""               
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

function Invoke-Build-Native-And-Run-Native-UnitTests
{
    Invoke-Build-Native 
    if(0 -ne $LASTEXITCODE) {
        return
    }
      
    Invoke-Native-UnitTests
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

function Invoke-Build-Docker-Entrypoint
{
    Invoke-Build-Native  
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

function Invoke-Build-Snapx
{
    .\install_snapx.ps1 -Bootstrap $true -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Invoke-Build-Snap-Installer
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Write-Output-Header "Building snapx"

    if($env:SNAPX_CI_BUILD -eq $false -and ($OSPlatform -eq "Windows")) {
        $NativeBuildDir = Join-Path $WorkingDir build\native

        $Executables = @(
            "$NativeBuildDir/Unix/x86_64-w64-mingw32-gcc/Release/Snap.CoreRun/corerun.exe"
            "$NativeBuildDir/Windows/win-msvs-16-x64/Release/Snap.CoreRun/Release/corerun.exe"
        ) -join " "

        Invoke-Command-Colored $CommandSigntool @("sign /a /fd sha256 /t http://timestamp.digicert.com /sha1 F57A7A2A8DFBDA7D7211B81C755150A8D994CD48 /a $Executables")

        if(0 -ne $LASTEXITCODE) {
            return
        }
    }

    .\install_snapx.ps1 -Bootstrap $false -NetCoreAppVersion $NetCoreAppVersion
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Invoke-Build-Snap
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

switch ($Target) {
    "Bootstrap"{
        Invoke-Docker -Entrypoint "Native"
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        if($OSPlatform -eq "Windows")
        {
            Invoke-Build-Native 
            if(0 -ne $LASTEXITCODE) {
                exit $LASTEXITCODE
            }       
            Invoke-Native-UnitTests
            if(0 -ne $LASTEXITCODE) {
                exit $LASTEXITCODE
            }       
        }    
 
        Invoke-Build-Snapx
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }        

        Invoke-Build-Snap
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        } 

        Invoke-Dotnet-Unit-Tests
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }    

        Invoke-Summary
        exit 0 
    }    
    "Native" {
        if($env:SNAPX_DOCKER_BUILD -eq $false)
        {
            Invoke-Docker -Entrypoint Native
            return
        }

        Invoke-Build-Native
        Invoke-Native-UnitTests
        Invoke-Summary
    }
    "Snap" {
        Invoke-Build-Snap
        Invoke-Summary
    }
    "Snap-Installer" {        
        Invoke-Build-Snap-Installer
        Invoke-Summary
    }
    "Snapx" {
        Invoke-Build-Snapx
        Invoke-Summary
    }
    "Run-Native-UnitTests"
    {
        Invoke-Native-UnitTests
        if($OSPlatform -eq "Windows")
        {
            Invoke-Docker -Entrypoint "Run-Native-UnitTests"
        }
        Invoke-Summary
    }
    "Run-Dotnet-UnitTests"
    {
        Invoke-Dotnet-Unit-Tests
        Invoke-Summary
    }
}
