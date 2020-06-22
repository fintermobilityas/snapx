param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Bootstrap-CI-Unix", "Bootstrap-CI-Windows", "Native", "Snap", "Snap-Installer", "Snapx", "Run-Native-UnitTests", "Run-Dotnet-UnitTests", "Publish-Docker-Image")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
	[string] $DockerImageName = "snapx",
	[Parameter(Position = 2, ValueFromPipeline = $true)]
	[string] $DockerVersion = "1.0",
	[Parameter(Position = 3, ValueFromPipeline = $true)]
	[bool] $DockerUseGithubRegistry = $true,
    [Parameter(Position = 4, ValueFromPipeline = $true)]
    [string] $CIBuild,
    [Parameter(Position = 5, ValueFromPipeline = $true)]
    [string] $VisualStudioVersionStr = "16",
    [Parameter(Position = 6, ValueFromPipeline = $true)]
    [ValidateSet("netcoreapp3.1")]
    [string] $NetCoreAppVersion = "netcoreapp3.1",
    [Parameter(Position = 7, ValueFromPipeline = $true)]
    [string] $Version = "0.0.0"
)

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$ErrorActionPreference = "Stop";
$ConfirmPreference = "None";

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]
$DotnetVersion = Get-Content global.json | ConvertFrom-Json | Select-Object -Expand sdk | Select-Object -Expand version

# Ref: https://github.com/Microsoft/azure-pipelines-tasks/issues/836
$env:SNAPX_CI_BUILD = Get-Is-String-True $CIBuild
[int] $VisualStudioVersion = 0
if($false -eq [int]::TryParse($VisualStudioVersionStr, [ref] $VisualStudioVersion))
{
    Write-Error "Invalid Visual Studio Version: $VisualStudioVersionStr"
    exit 1
}

$CommandDocker = $null
$CommandDockerCli = $null
$CommandGit = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandDocker = "docker.exe"
        $CommandDockerCli = Join-Path $env:ProgramFiles docker\docker\dockercli.exe
        $CommandGit = "git.exe"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandDocker = "docker"
        $CommandDockerCli = "dockercli"
        $CommandGit = "git"
    }
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

$DockerFilenamePath = Join-Path $WorkingDir docker\Dockerfile
$DockerGithubRegistryUrl = "docker.pkg.github.com/fintermobilityas/snapx"
$DockerContainerUrl = "mcr.microsoft.com/dotnet/core/sdk:${DotnetVersion}-focal"
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False

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
            .\bootstrap.ps1 -Target Native -Configuration Debug -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        return
    }

    Write-output $OSPlatform

    if ($OSPlatform -eq "Unix") {

        if($env:SNAPX_CI_BUILD -eq $false) {
            .\bootstrap.ps1 -Target Native -Configuration Debug -NetCoreAppVersion $NetCoreAppVersion -Version $Version
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }

            .\bootstrap.ps1 -Target Native -Configuration Debug -Cross 1 -NetCoreAppVersion $NetCoreAppVersion -Version $Version
            if($LASTEXITCODE -ne 0)
            {
                Write-Error "Native build failed"
                exit $LASTEXITCODE
            }
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Lto 1 -NetCoreAppVersion $NetCoreAppVersion -Version $Version
        if($LASTEXITCODE -ne 0)
        {
            Write-Error "Native build failed"
            exit $LASTEXITCODE
        }

        .\bootstrap.ps1 -Target Native -Configuration Release -Cross 1 -Lto 1 -NetCoreAppVersion $NetCoreAppVersion -Version $Version
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
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid linux-x64 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
    .\bootstrap.ps1 -Target Snap-Installer -DotNetRid win-x64 -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
}

function Invoke-Build-Snap {
    .\bootstrap.ps1 -Target Snap -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
}

function Invoke-Summary {
    $SummaryStopwatch.Stop()
    $Elapsed = $SummaryStopwatch.Elapsed
    Write-Output-Header "Operation completed in: $Elapsed ($OSVersion)"
}

function Invoke-Native-UnitTests
{
    .\bootstrap.ps1 -Target Run-Native-UnitTests -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
}

function Invoke-Dotnet-Unit-Tests
{
    .\bootstrap.ps1 -Target Run-Dotnet-UnitTests -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
}

function Invoke-Docker
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("Native", "Run-Native-UnitTests")]
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

    if($OSPlatform -eq "Windows") {
        Invoke-Command-Colored "& '$CommandDockerCli'" @("-SwitchLinuxEngine")
    }

    $DockerImageSrc = $null

    if($Entrypoint -eq "Native")
    {
        if($DockerUseGithubRegistry -eq $true) {
            $DockerImageSrc = "${DockerGithubRegistryUrl}/${DockerImageName}:${DockerVersion}"
            Invoke-Command-Colored docker @(
                "pull $DockerImageSrc"
            )
        } else {
            $DockerImageSrc = $DockerImageName
            Invoke-Command-Colored $CommandDocker @(
                "build -f ""${DockerFilenamePath}"" -t $DockerImageName docker"
            )
        }
    }

    Invoke-Command-Colored $CommandDocker @(
        "run"
        "--rm=true"
        "-e ""SNAPX_DOCKER_USERNAME=${env:SNAPX_DOCKER_USERNAME}"""
        "-e ""SNAPX_DOCKER_USER_ID=${env:SNAPX_DOCKER_USER_ID}"""
        "-e ""SNAPX_DOCKER_GROUP_ID=${env:SNAPX_DOCKER_GROUP_ID}"""
        "-e ""SNAPX_DOCKER_WORKING_DIR=${env:SNAPX_DOCKER_WORKING_DIR}"""
        "-e ""SNAPX_DOCKER_ENTRYPOINT=$Entrypoint"""
        "-e ""SNAPX_DOCKER_HOST_OS=$OSPlatform"""
        "-e ""SNAPX_DOCKER_CI_BUILD=${env:SNAPX_CI_BUILD}"""
        "-e ""SNAPX_DOCKER_VISUAL_STUDIO_VERSION=$VisualStudioVersion"""
        "-v ${WorkingDir}:${env:SNAPX_DOCKER_WORKING_DIR}"
        "--name $DockerImageName"
        "$DockerImageSrc"
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

function Invoke-Git-Restore {
    Invoke-Command-Colored $CommandGit ("submodule update --init --recursive")
}

function Invoke-Build-Snapx
{
    .\install_snapx.ps1 -Bootstrap $true -VisualStudioVersion $VisualStudioVersion -NetCoreAppVersion $NetCoreAppVersion -Version $Version
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Invoke-Build-Snap-Installer
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Write-Output-Header "Building snapx"

    .\install_snapx.ps1 -Bootstrap $false -NetCoreAppVersion $NetCoreAppVersion -Version $Version
    if(0 -ne $LASTEXITCODE) {
        return
    }

    Invoke-Build-Snap
    if(0 -ne $LASTEXITCODE) {
        return
    }
}

switch ($Target) {
    "Bootstrap-CI-Unix"
    {
        Invoke-Build-Native
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        Invoke-Native-UnitTests
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        Invoke-Build-Snapx
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        Invoke-Build-Snap
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        # TODO: ENABLE ME
        #Invoke-Dotnet-Unit-Tests
        #if(0 -ne $LASTEXITCODE) {
        #    exit $LASTEXITCODE
        #}

        Invoke-Summary
    }
    "Bootstrap-CI-Windows"
    {
        Invoke-Build-Native
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }
        Invoke-Native-UnitTests
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        Invoke-Build-Snapx
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        Invoke-Build-Snap
        if(0 -ne $LASTEXITCODE) {
            exit $LASTEXITCODE
        }

        #Invoke-Dotnet-Unit-Tests
        #if(0 -ne $LASTEXITCODE) {
        #    exit $LASTEXITCODE
        #}

        Invoke-Summary
    }
    "Bootstrap" {
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
    "Publish-Docker-Image" {
		$DockerFileContent = Get-Content $DockerFilenamePath
		$DockerFileContent[0] = "FROM $DockerContainerUrl as env-build"
		$DockerFileContent | Out-File $DockerFilenamePath -Encoding $Utf8NoBomEncoding

		Invoke-Command-Colored $CommandDocker @(
			"build -f ""$DockerFilenamePath"" -t ${DockerGithubRegistryUrl}/${DockerImageName}:${DockerVersion} docker"
        )

		Invoke-Command-Colored $CommandDocker @(
			"push ${DockerGithubRegistryUrl}/${DockerImageName}:${DockerVersion}"
        )

		exit 0
	}
}
