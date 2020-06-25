param(
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [ValidateSet("Native", "Snap", "Snapx", "Snap-Installer", "Run-Native-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target,
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $Configuration,
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
    [switch] $Lto,
    [Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
    [string] $DotNetRid = $null,
    [Parameter(Position = 4, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $VisualStudioVersion,
    [Parameter(Position = 5, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $NetCoreAppVersion,
    [Parameter(Position = 6, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $Version,
    [Parameter(Position = 7, ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(Position = 8, ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerBuild
)

# Global Variables
$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$ToolsDir = Join-Path $WorkingDir tools
$SrcDir = Join-Path $WorkingDir src
$NupkgsDir = Join-Path $WorkingDir nupkgs

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount
$Arch = $null

$CmakeGenerator = $null
$CommandCmake = $null
$CommandDotnet = $null
$CommandMake = $null
$CommandVsWhere = $null
$CommandGTestsDefaultArguments = @(
    "--gtest_break_on_failure"
)

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CmakeGenerator = "Visual Studio $VisualStudioVersion"
        $CommandCmake = "cmake.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandVsWhere = Join-Path $ToolsDir vswhere-win-x64.exe
        $Arch = "win-msvs-${VisualStudioVersion}-x64"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CmakeGenerator = "Unix Makefiles"
        $CommandCmake = "cmake"
        $CommandDotnet = "dotnet"
        $CommandMake = "make"
        $Arch = "x86_64-linux-gcc"
    }
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

$TargetArch = $Arch
$TargetArchDotNet = $NetCoreAppVersion

# Projects
$SnapCoreRunSrcDir = Join-Path $WorkingDir src
$SnapDotnetSrcDir = Join-Path $WorkingDir src\Snap
$SnapInstallerDotnetSrcDir = Join-Path $WorkingDir src\Snap.Installer
$SnapxDotnetSrcDir = Join-Path $WorkingDir src\Snapx

function Invoke-Build-Native {
    Write-Output-Header "Building native dependencies"

    Resolve-Shell-Dependency $CommandCmake

    $SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$TargetArch\$Configuration

    $CmakeArchNewSyntaxPrefix = ""
    $CmakeGenerator = $CmakeGenerator

    if($OSPlatform -eq "Windows")
    {
        if($VisualStudioVersion -ne 16) {
           Write-Error "Only Visual Studio 2019 is supported"
        }

        $CmakeGenerator += " 2019"
        $CmakeArchNewSyntaxPrefix = "-A x64"
    }

    $CmakeArguments = @(
        "-G""$CmakeGenerator"""
        "$CmakeArchNewSyntaxPrefix"
        "-H""$SnapCoreRunSrcDir"""
        "-B""$SnapCoreRunBuildOutputDir"""
    )

    if ($Lto) {
        $CmakeArguments += "-DBUILD_ENABLE_LTO=ON"
    }

    if ($Configuration -eq "Debug") {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Debug"
    }
    else {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Release"
    }

    Write-Output "Build src directory: $SnapCoreRunSrcDir"
    Write-Output "Build output directory: $SnapCoreRunBuildOutputDir"
    Write-Output "Arch: $TargetArch"
    Write-Output ""

    Invoke-Command-Colored $CommandCmake $CmakeArguments

    switch ($OSPlatform) {
        "Unix" {
            Invoke-Command-Colored sh @("-c ""(cd $SnapCoreRunBuildOutputDir && make -j $ProcessorCount)""")
        }
        "Windows" {
            Invoke-Command-Colored $CommandCmake @(
                "--build `"$SnapCoreRunBuildOutputDir`" --config $Configuration"
            )
        }
        default {
            Write-Error "Unsupported os platform: $OSPlatform"
        }
    }

}

function Invoke-Build-Snap {
    Write-Output-Header "Building Snap"

    Resolve-Shell-Dependency $CommandDotnet

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapDotnetSrcDir Snap.csproj))
        "/p:Version=$Version",
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "--configuration $Configuration"
    )

    Invoke-Command-Colored $CommandDotnet @(
        "pack",
        "/p:PackageVersion=$Version",
        "--no-build",
        "--output ${NupkgsDir}",
        "--configuration $Configuration",
        "$SnapDotnetSrcDir"
    )
}

function Invoke-Build-Snapx {
    Write-Output-Header "Building Snapx"

    Resolve-Shell-Dependency $CommandDotnet

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapxDotnetSrcDir Snapx.csproj))
        "/p:Version=$Version",
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "--configuration $Configuration"
    )

    Invoke-Command-Colored $CommandDotnet @(
        "pack",
        "/p:PackageVersion=$Version",
        "--no-build",
        "--output ${NupkgsDir}",
        "--configuration $Configuration",
        "$SnapxDotnetSrcDir"
    )
}

function Invoke-Build-Snap-Installer {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("win-x64", "linux-x64")]
        [string] $Rid
    )
    Write-Output-Header "Building Snap.Installer"

    Resolve-Shell-Dependency $CommandDotnet

    $PackerArch = $null
    $SnapInstallerExeName = $null

    switch ($Rid) {
        "win-x64" {
            $PackerArch = "windows-x64"
            $SnapInstallerExeName = "Snap.Installer.exe"
        }
        "linux-x64" {
            $PackerArch = "linux-x64"
            $SnapInstallerExeName = "Snap.Installer"
        }
        default {
            Write-Error "Rid not supported: $Rid"
        }
    }

    $SnapInstallerDotnetBuildPublishDir = Join-Path $WorkingDir build\dotnet\$Rid\Snap.Installer\$TargetArchDotNet\$Configuration\publish
    $SnapInstallerExeAbsolutePath = Join-Path $SnapInstallerDotnetBuildPublishDir $SnapInstallerExeName
    $SnapInstallerExeZipAbsolutePath = Join-Path $SnapInstallerDotnetBuildPublishDir "Setup-$Rid.zip"
    $SnapInstallerCsProj = Join-Path $SnapInstallerDotnetSrcDir Snap.Installer.csproj
    $SnapxCsProj = Join-Path $SnapxDotnetSrcDir Snapx.csproj

    Write-Output "Build src directory: $SnapInstallerDotnetSrcDir"
    Write-Output "Build output directory: $SnapInstallerDotnetBuildPublishDir"
    Write-Output "Arch: $TargetArchDotNet"
    Write-Output "Rid: $Rid"
    Write-Output "PackerArch: $PackerArch"
    Write-Output ""

    if(Test-Path $SnapInstallerDotnetBuildPublishDir) {
        Get-Item $SnapInstallerDotnetBuildPublishDir | Remove-Item -Recurse -Force
    }

    Invoke-Command-Colored $CommandDotnet @(
        "publish $SnapInstallerCsProj"
        "/p:PublishTrimmed=true"
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "/p:Version=$Version"
        "--runtime $Rid"
        "--framework $TargetArchDotNet"
        "--self-contained true"
        "--output $SnapInstallerDotnetBuildPublishDir"
        "--configuration $Configuration"
    )

    if ($Rid -eq "win-x64") {
        Invoke-Command-Colored $CommandDotnet @(
            "build $SnapxCsProj"
            "-p:SnapBootstrap=True"
            "--configuration $Configuration"
            "--framework $TargetArchDotNet"
        )

        Invoke-Command-Colored $CommandDotnet @(
            "run"
            "--project $SnapxCsProj"
            "--configuration $Configuration"
            "--framework $TargetArchDotNet"
            "--no-build"
            "--"
            "rcedit"
            "$SnapInstallerExeAbsolutePath"
            "--gui-app"
        )
    }

    if ($OSPlatform -ne "Windows") {
        Invoke-Command-Colored chmod @("+x $SnapInstallerExeAbsolutePath")
    }

    if(Test-Path $SnapInstallerExeZipAbsolutePath)
    {
        Remove-Item $SnapInstallerExeZipAbsolutePath -Force | Out-Null
    }

    Compress-Archive `
        -Path $SnapInstallerDotnetBuildPublishDir\* `
        -CompressionLevel Optimal `
        -DestinationPath $SnapInstallerExeZipAbsolutePath
}

function Invoke-Native-UnitTests
{
    switch($OSPlatform)
    {
        "Windows" {

            $Projects = @()

            $MsvsProject = Join-Path $WorkingDir build\native\Windows\win-msvs-${VisualStudioVersion}-x64\${Configuration}\Snap.CoreRun.Tests\${Configuration}
            if($env:SNAPX_CI_WINDOWS_DISABLE_MSVS_TESTS -ne 1) {
                $Projects += $MsvsProject
            }

            $TestRunnerCount = $Projects.Length
            Write-Output "Running native windows unit tests. Test runner count: $TestRunnerCount"

            foreach ($GoogleTestsDir in $Projects) {
                Invoke-Google-Tests $GoogleTestsDir corerun_tests.exe $CommandGTestsDefaultArguments
            }
        }
        "Unix" {
            $Projects = @()

            $GccProject = Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\${Configuration}\Snap.CoreRun.Tests
            if($env:SNAPX_CI_UNIX_DISABLE_GCC_TESTS -ne 1) {
                $Projects += $GccProject
            }

            $TestRunnerCount = $Projects.Length
            Write-Output "Running native unix unit tests. Test runner count: $TestRunnerCount"

            foreach ($GoogleTestsDir in $Projects) {
                Invoke-Google-Tests $GoogleTestsDir corerun_tests $CommandGTestsDefaultArguments
            }

        }
        default {
            Write-Error "Unsupported os platform: $OSPlatform"
        }
    }
}

function Invoke-Dotnet-UnitTests
{
    Push-Location $WorkingDir

    Resolve-Shell-Dependency $CommandDotnet

    $Projects = @(
        Join-Path $SrcDir Snap.Installer.Tests
        Join-Path $SrcDir Snap.Tests
        Join-Path $SrcDir Snapx.Tests
    )

    $ProjectsCount = $Projects.Length

    Write-Output-Header "Running dotnet tests. Test project count: $ProjectsCount"

    foreach($Project in $Projects)
    {
        $TestProjectName = Split-Path $Project -LeafBase
        $TestResultsOutputDirectoryPath = Join-Path $WorkingDir build\dotnet\TestResults\$TestProjectName

        Invoke-Command-Colored $CommandDotnet @(
            "build"
            "--configuration $Configuration"
            "--framework $NetCoreAppVersion"
            "$Project"
        )

        Invoke-Command-Colored $CommandDotnet @(
            "test"
            "$Project"
            "--configuration=$Configuration"
            "--verbosity normal"
            "--logger:""xunit;LogFileName=TestResults.xml"""
            "--results-directory:""$TestResultsOutputDirectoryPath"""
        )
    }

}

Write-Output-Header "----------------------- CONFIGURATION DETAILS ------------------------"
Write-Output "OS: $OSVersion"
Write-Output "OS Platform: $OSPlatform"
Write-Output "Processor count: $ProcessorCount"
Write-Output "Configuration: $Configuration"
Write-Output "Docker: $DockerBuild"
Write-Output "CI Build: $CIBuild"
Write-Output "Native target arch: $Arch"

if($OSPlatform -eq "Windows")
{
    Write-Output "Visual Studio Version: $VisualStudioVersion"
}

switch ($OSPlatform) {
    "Windows" {
        Resolve-Windows $OSPlatform
        Invoke-Configure-Msvs-Toolchain $VisualStudioVersion $CommandVsWhere
    }
    "Unix" {
        Resolve-Unix $OSPlatform
        Resolve-Shell-Dependency $CommandMake
    }
    default {
        Write-Error "Unsupported os platform: $OSPlatform"
    }
}

switch ($Target) {
    "Native" {
        switch ($OSPlatform) {
            "Windows" {
                Invoke-Build-Native
            }
            "Unix" {
                Invoke-Build-Native
            }
            default {
                Write-Error "Unsupported os platform: $OSPlatform"
            }
        }
    }
    "Snap" {
        Invoke-Build-Snap
    }
    "Snapx" {
        Invoke-Build-Snapx
    }
    "Snap-Installer" {
        Invoke-Build-Snap-Installer -Rid $DotNetRid
    }
    "Run-Native-UnitTests" {
        Invoke-Native-UnitTests
    }
    "Run-Dotnet-UnitTests" {
        Invoke-Dotnet-UnitTests
    }
}
