param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Bootstrap-Unix", "Bootstrap-Windows", "Snap", "Snap-Installer", "Snapx", "Snapx-Nupkg", "Run-Native-UnitTests", "Publish-Docker-Image")]
    [string] $Target = "Bootstrap",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
	[string] $DockerImageName = "snapx",
	[Parameter(ValueFromPipelineByPropertyName = $true)]
	[string] $DockerVersion = "2.5.4",
	[Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerLocal,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
	[switch] $DockerContext,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $VisualStudioVersionStr = "16",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("netcoreapp3.1")]
    [string] $NetCoreAppVersion = "netcoreapp3.1",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $Version = "0.0.0",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $DotnetRid = "any"
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
$DockerBuild = $env:BUILD_IS_DOCKER -eq 1

if($false -eq (Test-Path "env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE"))
{
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
}

if($false -eq (Test-Path "env:DOTNET_CLI_TELEMETRY_OPTOUT"))
{
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
}

[int] $VisualStudioVersion = 0
if($false -eq [int]::TryParse($VisualStudioVersionStr, [ref] $VisualStudioVersion))
{
    Invoke-Exit "Invalid Visual Studio Version: $VisualStudioVersionStr"
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

$SummaryStopwatch = $Stopwatch::StartNew()
$SummaryStopwatch.Restart()

function Invoke-Bootstrap-Ps1 {
    param(
        [string] $Target,
        [string[]] $Arguments
    )

    $DefaultArguments = @(
        $Target,
        "-VisualStudioVersion $VisualStudioVersion"
        "-NetCoreAppVersion $NetCoreAppVersion"
        "-Version $Version"
        "-CIBuild:$CIBuild"
        "-DockerBuild:" + ($DockerBuild ? "True" : "False")
        $Arguments
    )

    Invoke-Command-Colored pwsh @(
        "bootstrap.ps1"
        $DefaultArguments
    )
}

function Invoke-Install-Snapx-Ps1 {
    param(
        [string] $Target,
        [string[]] $Arguments
    )

    $DefaultArguments = @(
        $Target,
        "-VisualStudioVersion $VisualStudioVersion"
        "-NetCoreAppVersion $NetCoreAppVersion"
        "-Version $Version"
        "-CIBuild:$CIBuild"
        "-DockerBuild:" + ($DockerBuild ? "True" : "False")
        $Arguments
    )

    Invoke-Command-Colored pwsh @(
        "install_snapx.ps1"
        $DefaultArguments
    )
}

function Invoke-Build-Native {
    switch($OSPlatform) {
        "Windows" {
            $WindowsArguments = @("-Configuration $Configuration")
            if($Configuration -eq "Release") {
                $WindowsArguments += "-Lto"
            }
            Invoke-Bootstrap-Ps1 Native $WindowsArguments
        }
        "Unix" {
            $UnixArguments = @("-Configuration $Configuration")
            if($Configuration -eq "Release") {
                $UnixArguments += "-Lto"
            }
            Invoke-Bootstrap-Ps1 Native $UnixArguments
            Invoke-Bootstrap-Ps1 Native @("-Cross", $UnixArguments)
        }
        Default {
            Write-Error "Unsupported OS platform: $OSVersion"
        }
    }
}

function Invoke-Build-Snap-Installer {

    $Rids = @()
    if($CIBuild) {
        switch ($OSPlatform) {
            "Unix" {
                $Rids += "linux-x64"
            }
            "Windows" {
                $Rids += "win-x64"
            }
            Default {
                Write-Error "Unsupported os: $OSPlatform"
            }
        }
    } else {
        $Rids += "linux-x64"
        $Rids += "win-x64"
    }

    $Rids | ForEach-Object {
        $Rid = $_
        Invoke-Bootstrap-Ps1 Snap-Installer @("-Configuration $Configuration -DotNetRid $Rid")
    }

}

function Invoke-Build-Snap {
    Invoke-Bootstrap-Ps1 Snap @("-Configuration $Configuration")
}

function Invoke-Native-UnitTests
{
    Invoke-Bootstrap-Ps1 Run-Native-UnitTests @("-Configuration $Configuration")
}

function Invoke-Dotnet-Unit-Tests
{
    Invoke-Bootstrap-Ps1 Run-Dotnet-UnitTests @("-Configuration $Configuration")
}

function Invoke-Build-Snapx
{
    Invoke-Install-Snapx-Ps1 @("-DotnetRid:pack -Configuration $Configuration")
}

function Invoke-Build-Snapx-Nupkg
{
    Invoke-Install-Snapx-Ps1 @("-DotnetRid:pack", "-Configuration $Configuration")
}

function Invoke-Bootstrap-Unix
{
    if($env:BUILD_IS_DOCKER -ne 1)
    {
        Invoke-Docker -Entrypoint "Bootstrap-Unix"
        return
    }

    Invoke-Build-Native
    Invoke-Native-UnitTests
    Invoke-Build-Snap-Installer
}

function Invoke-Bootstrap-Windows {
    Invoke-Build-Native
    if($CIBuild -eq $false) {
        Invoke-Native-UnitTests
    }
    Invoke-Build-Snap-Installer
}

function Invoke-Summary {
    $SummaryStopwatch.Stop()
    $Elapsed = $SummaryStopwatch.Elapsed
    Write-Output-Header "Operation completed in: $Elapsed ($OSVersion)"
}

function Invoke-Docker
{
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true, Mandatory = $true)]
        [string] $Entrypoint
    )

    Write-Output-Header "Docker entrypoint: $Entrypoint"

    Resolve-Shell-Dependency $CommandDocker

    if($OSPlatform -eq "Windows") {
        Invoke-Command-Colored "& '$CommandDockerCli'" @("-SwitchLinuxEngine")
    }

    $DockerParameters = @(
		"-Target ${Target}"
        "-Configuration $Configuration"
		"-DockerImageName ${DockerImageName}"
		"-DockerVersion ${DockerVersion}"
		"-DockerLocal:" + ($DockerLocal ? "True" : "False")
        "-CIBuild:" + ($CIBuild ? "True" : "False")
        "-VisualStudioVersionStr $VisualStudioVersionStr"
        "-NetCoreAppVersion $NetCoreAppVersion"
        "-Version $Version"
        "-DotnetRid $DotnetRid"
    )

    $EnvironmentVariables = @(
		@{
			Key = "BUILD_HOST_OS"
			Value = $OSPlatform
		}
		@{
			Key = "BUILD_IS_DOCKER"
			Value = 1
		}
		@{
			Key = "BUILD_PS_PARAMETERS"
			Value = """{0}""" -f ($DockerParameters -Join " ")
		}
	)

    $DockerEnvironmentVariables = @()
	foreach($EnvironmentVariable in $EnvironmentVariables) {
		$Key = $EnvironmentVariable.Key
		$Value = $EnvironmentVariable.Value

		$EnvironmentVariables += $Key + "=" + $Value
		$DockerEnvironmentVariables += "-e $Key=$Value"
	}

    $DockerImageSrc = $null

    if($DockerLocal -eq $false) {
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

    Invoke-Command-Colored $CommandDocker @(
        "run --rm=true"
        $DockerEnvironmentVariables
        "-v ${WorkingDir}:/build/snapx"
        "$DockerImageSrc"
    )

    Write-Output-Header "Docker container entrypoint ($Entrypoint) finished"
}

function Invoke-Git-Restore {
    Invoke-Command-Colored $CommandGit ("submodule update --init --recursive")
}

switch ($Target) {
    "Bootstrap-Unix"
    {
        Invoke-Bootstrap-Unix
        Invoke-Summary
    }
    "Bootstrap-Windows"
    {
        Invoke-Bootstrap-Windows
        Invoke-Summary
    }
    "Bootstrap" {
        $Configuration = "Debug"
        Invoke-Bootstrap-Unix
        Invoke-Bootstrap-Windows
        $Configuration = "Release"
        Invoke-Bootstrap-Unix
        Invoke-Bootstrap-Windows
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
    "Snapx-Nupkg" {
        Invoke-Build-Snapx-Nupkg
        Invoke-Summary
    }
    "Run-Native-UnitTests" {
        Invoke-Native-UnitTests
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
