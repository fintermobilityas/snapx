param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Bootstrap-Unix", "Bootstrap-Windows", "Snap", "Snap-Installer", "Snapx", "Run-Dotnet-UnitTests", "Run-Native-UnitTests", "Publish-Docker-Image")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
	[string] $DockerImageName = "snapx",
	[Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
	[string] $DockerVersion = "2.5.4",
	[Parameter(Position = 4, ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerLocal,
    [Parameter(Position = 5, ValueFromPipelineByPropertyName = $true)]
	[switch] $DockerContext,
    [Parameter(Position = 6, ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(Position = 7, ValueFromPipelineByPropertyName = $true)]
    [string] $VisualStudioVersion = "16",
    [Parameter(Position = 8, ValueFromPipelineByPropertyName = $true)]
    [string] $NetCoreAppVersion = "netcoreapp3.1",
    [Parameter(Position = 9, ValueFromPipelineByPropertyName = $true)]
    [string] $Version = "0.0.0",
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true)]
    [string] $DotnetRid = "any"
)

# Init

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$NupkgsDir = Join-Path $WorkingDir nupkgs
$SnapSrcDir = Join-Path $WorkingDir src/Snap
$SnapxSrcDir = Join-Path $WorkingDir src/Snapx

$SnapCsProjPath = Join-Path $SnapSrcDir Snap.csproj
$SnapxCsProjPath = Join-Path $SnapxSrcDir Snapx.csproj

# Environment variables

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]
$DotnetVersion = Get-Content global.json | ConvertFrom-Json | Select-Object -Expand sdk | Select-Object -Expand version
$DockerBuild = $env:BUILD_IS_DOCKER -eq 1

$CommandDocker = $null
$CommandGit = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CommandDocker = "docker.exe"
        $CommandGit = "git.exe"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CommandDocker = "docker"
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

function Invoke-Install-Snapx 
{
    Invoke-Command-Colored dotnet @(
        "tool",
        "uninstall",
        "--global",
        "snapx"
    ) -IgnoreExitCode

    Invoke-Command-Colored dotnet @(
        "build"
        "/p:Version=$Version"
        "/p:SnapRid=pack"
        "/p:GeneratePackageOnBuild=true"
        "--configuration $Configuration"
        $SnapCsProjPath
    )

    Invoke-Command-Colored dotnet @(
        "build"
        "/p:Version=$Version"
        "/p:SnapRid=pack"
        "/p:GeneratePackageOnBuild=true"
        "--configuration $Configuration"
        $SnapxCsProjPath
    )

    Invoke-Command-Colored dotnet @(
        "tool"
        "update"
        "snapx"
        "--global"
        "--add-source $NupkgsDir"
        "--version $Version"
    )

    Resolve-Shell-Dependency snapx
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
        }
        Default {
            Write-Error "Unsupported OS platform: $OSVersion"
        }
    }
}

function Invoke-Build-Snap-Installer 
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $DotnetRid
    )
    Invoke-Bootstrap-Ps1 Snap-Installer @("-Configuration $Configuration -DotNetRid $DotnetRid")
}

function Invoke-Build-Snap {
    Invoke-Bootstrap-Ps1 Snap @("-Configuration $Configuration")
}

function Invoke-Native-UnitTests
{
    Invoke-Bootstrap-Ps1 Run-Native-UnitTests @("-Configuration $Configuration")
}

function Invoke-Dotnet-UnitTests
{
    Invoke-Bootstrap-Ps1 Run-Dotnet-UnitTests @("-Configuration $Configuration")
}

function Invoke-Bootstrap-Unix
{
    if($env:BUILD_IS_DOCKER -ne 1)
    {
        Invoke-Docker -Entrypoint "Bootstrap-Unix"
        return
    }

    Invoke-Build-Native
    Invoke-Build-Snap-Installer -DotnetRid linux-x64
}

function Invoke-Bootstrap-Windows {
    Invoke-Build-Native
    Invoke-Build-Snap-Installer -DotnetRid win-x64
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

    $DockerParameters = @(
		"-Target ${Target}"
        "-Configuration $Configuration"
		"-DockerImageName ${DockerImageName}"
		"-DockerVersion ${DockerVersion}"
		"-DockerLocal:" + ($DockerLocal ? "True" : "False")
        "-CIBuild:" + ($CIBuild ? "True" : "False")
        "-VisualStudioVersion $VisualStudioVersion"
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
        Invoke-Bootstrap-Unix
        Invoke-Bootstrap-Windows
        Invoke-Summary
    }
    "Snap" {
        Invoke-Build-Snap
        Invoke-Summary
    }
    "Snap-Installer" {
        Invoke-Build-Snap-Installer -DotnetRid $DotnetRid
        Invoke-Summary
    }
    "Snapx" {
        Invoke-Install-Snapx
        Invoke-Summary
    }
    "Run-Native-UnitTests" {
        switch($OSPlatform) {
            "Windows" {
                if($CIBuild) {
                    Invoke-Native-UnitTests
                    Invoke-Summary
                    return
                }

                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Native-UnitTests"
                    Invoke-Native-UnitTests
                    Invoke-Summary
                    return
                }

                Invoke-Native-UnitTests
            }
            "Unix" {
                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Native-UnitTests"
                    return
                }
    
                Invoke-Native-UnitTests
                Invoke-Summary
            }
        }
    }
    "Run-Dotnet-UnitTests" {
        switch($OSPlatform) {
            "Windows" {
                if($CIBuild) {
                    Invoke-Dotnet-UnitTests
                    Invoke-Summary
                    return
                }

                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Dotnet-UnitTests"
                    Invoke-Dotnet-UnitTests
                    Invoke-Summary
                    return
                }

                Invoke-Dotnet-UnitTests
            }
            "Unix" {
                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Dotnet-UnitTests"
                    return
                }
    
                Invoke-Dotnet-UnitTests
                Invoke-Summary
            }
        }
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
