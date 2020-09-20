param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Bootstrap", "Bootstrap-Unix", "Bootstrap-Windows", "Snap", "Snap-Installer", "Snapx", "Build-Dotnet-UnitTests", "Run-Dotnet-UnitTests", "Run-Native-UnitTests", "Publish-Docker-Image")]
    [string] $Target = "Bootstrap",
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
	[string] $DockerImageName = "snapx",
	[Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
	[string] $DockerVersion = "3.0.100",
	[Parameter(Position = 4, ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerLocal,
    [Parameter(Position = 5, ValueFromPipelineByPropertyName = $true)]
	[switch] $DockerContext,
    [Parameter(Position = 6, ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(Position = 7, ValueFromPipelineByPropertyName = $true)]
    [string] $NetCoreAppVersion = "net5.0",
    [Parameter(Position = 8, ValueFromPipelineByPropertyName = $true)]
    [string] $Version = "0.0.0",
    [Parameter(Position = 9, ValueFromPipelineByPropertyName = $true)]
    [string] $Rid = "any"
)

# Init

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$SrcDirectory = Join-Path $WorkingDir src
$NupkgsDir = Join-Path $WorkingDir nupkgs
$SnapSrcDir = Join-Path $SrcDirectory Snap
$SnapxSrcDir = Join-Path $SrcDirectory Snapx

$SnapCsProjPath = Join-Path $SnapSrcDir Snap.csproj
$SnapxCsProjPath = Join-Path $SnapxSrcDir Snapx.csproj

# Environment variables

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$Stopwatch = [System.Diagnostics.Stopwatch]

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
$DockerContainerUrl = "mcr.microsoft.com/dotnet/sdk:5.0.100-rc.1-focal"
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False

$SummaryStopwatch = $Stopwatch::StartNew()
$SummaryStopwatch.Restart()

function Invoke-Build-Rids-Array {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    $Rids = @()

    switch($OSPlatform) {
        "Windows" {
            if($Rid -eq "any") {
                $Rids += "win-x86"
                $Rids += "win-x64"
            } else {
                $Rids += $Rid
            }
        }
        "Unix" {
            if($Rid -eq "any") {
                $Rids += "linux-x64"
                $Rids += "linux-arm64"
            } else {
                $Rids += $Rid
            }
        }
    }

    return $Rids
}

function Invoke-Bootstrap-Ps1 {
    param(
        [string] $Target,
        [string[]] $Arguments
    )

    $DefaultArguments = @(
        $Target,
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
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    switch($OSPlatform) {
        "Windows" {
            $WindowsArguments = @("-Configuration $Configuration")
            if($Configuration -eq "Release") {
                $WindowsArguments += "-Lto"
            }
            Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
                $WindowsArgumentsTmp = $WindowsArguments
                $WindowsArgumentsTmp += "-Rid $_"
                Invoke-Bootstrap-Ps1 Native $WindowsArgumentsTmp
            }
        }
        "Unix" {
            $UnixArguments = @("-Configuration $Configuration")
            if($Configuration -eq "Release") {
                $UnixArguments += "-Lto"
            }
            Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
                $UnixArgumentsTmp = $UnixArguments
                $UnixArgumentsTmp += "-Rid $_"
                Invoke-Bootstrap-Ps1 Native $UnixArgumentsTmp
            }
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
        [string] $Rid
    )

    Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
        $ActualRid = $_
        Invoke-Bootstrap-Ps1 Snap-Installer @("-Configuration $Configuration -Rid $ActualRid")
    }
}

function Invoke-Build-Snap {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
        $ActualRid = $_
        Invoke-Bootstrap-Ps1 Snap @("-Configuration $Configuration -Rid $ActualRid")
    }
}

function Invoke-Native-UnitTests
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
        $ActualRid = $_
        Invoke-Bootstrap-Ps1 Run-Native-UnitTests @("-Configuration $Configuration -Rid $ActualRid")
    }
}

function Invoke-Dotnet-UnitTests
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
        $ActualRid = $_
        Invoke-Bootstrap-Ps1 Run-Dotnet-UnitTests @("-Configuration $Configuration -Rid $ActualRid")
    }
}

function Invoke-Build-Dotnet-UnitTests
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    Invoke-Build-Rids-Array -Rid $Rid | ForEach-Object {
        $ActualRid = $_
        Invoke-Bootstrap-Ps1 Build-Dotnet-UnitTests @("-Configuration $Configuration -Rid $ActualRid")
    }
}

function Invoke-Bootstrap-Unix
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    if($env:BUILD_IS_DOCKER -ne 1)
    {
        Invoke-Docker -Entrypoint "Bootstrap-Unix"
        return
    }

    $Rids = Invoke-Build-Rids-Array -Rid $Rid

    $Rids  | ForEach-Object {
        $ActualRid = $_
        Invoke-Build-Native -Rid $ActualRid
    }

    $Rids  | ForEach-Object {
        $ActualRid = $_
        Invoke-Build-Snap-Installer -Rid $ActualRid
    }
}

function Invoke-Bootstrap-Windows {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Rid
    )

    $Rids = Invoke-Build-Rids-Array -Rid $Rid

    $Rids | ForEach-Object {
        $ActualRid = $_
        Invoke-Build-Native -Rid $ActualRid
    }

    $Rids | ForEach-Object {
        $ActualRid = $_
        Invoke-Build-Snap-Installer -Rid $ActualRid
    }
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
        "-NetCoreAppVersion $NetCoreAppVersion"
        "-Version $Version"
        "-Rid $Rid"
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

function Invoke-Clean-Build {
    Invoke-Dotnet-Clear $SrcDirectory
}

switch ($Target) {
    "Bootstrap-Unix"
    {
        Invoke-Clean-Build
        Invoke-Bootstrap-Unix -Rid $Rid
        Invoke-Summary
    }
    "Bootstrap-Windows"
    {
        Invoke-Clean-Build
        Invoke-Bootstrap-Windows -Rid $Rid
        Invoke-Summary
    }
    "Bootstrap" {
        Invoke-Clean-Build
        Invoke-Bootstrap-Unix -Rid $Rid
        Invoke-Bootstrap-Windows -Rid $Rid
        Invoke-Summary
    }
    "Snap" {
        Invoke-Clean-Build
        Invoke-Build-Snap -Rid $Rid
        Invoke-Summary
    }
    "Snap-Installer" {
        Invoke-Clean-Build
        Invoke-Build-Snap-Installer -Rid $Rid
        Invoke-Summary
    }
    "Snapx" {
        Invoke-Clean-Build
        Invoke-Install-Snapx -Rid $Rid
        Invoke-Summary
    }
    "Run-Native-UnitTests" {
        switch($OSPlatform) {
            "Windows" {
                Invoke-Clean-Build

                if($Rid.StartsWith("win")) {
                    Invoke-Native-UnitTests -Rid $Rid
                    Invoke-Summary
                    return
                }

                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Native-UnitTests"
                    if($OSPlatform -eq "Windows") {
                        Invoke-Native-UnitTests -Rid $Rid
                    }
                    Invoke-Summary
                    return
                }

                Invoke-Native-UnitTests -Rid $Rid
            }
            "Unix" {
                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Native-UnitTests"
                    return
                }

                Invoke-Native-UnitTests -Rid $Rid
                Invoke-Summary
            }
            Default {
                Invoke-Exit "Unsupported os: $OSPlatform"
            }
        }
    }
    "Build-Dotnet-UnitTests" {
        Invoke-Clean-Build
        Invoke-Build-Dotnet-UnitTests -Rid $Rid
    }
    "Run-Dotnet-UnitTests" {
        switch($OSPlatform) {
            "Windows" {
                Invoke-Clean-Build

                if($Rid.StartsWith("win")) {
                    Invoke-Dotnet-UnitTests -Rid $Rid
                    Invoke-Summary
                    return
                }

                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Dotnet-UnitTests"
                    if($OSPlatform -eq "Windows") {
                        Invoke-Dotnet-UnitTests -Rid $Rid
                    }
                    Invoke-Summary
                    return
                }

                Invoke-Dotnet-UnitTests -Rid $Rid
            }
            "Unix" {
                Invoke-Clean-Build

                if($env:BUILD_IS_DOCKER -ne 1) {
                    Invoke-Docker -Entrypoint "Run-Dotnet-UnitTests"
                    return
                }

                Invoke-Dotnet-UnitTests -Rid $Rid
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
