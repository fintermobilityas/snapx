param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Native", "Snap", "Snapx", "Snap-Installer", "Run-Native-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target = "Native",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $Cross,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $Lto,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $DotNetRid = $null,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [Validateset(16)]
    [int] $VisualStudioVersion = 16,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("netcoreapp3.1")]
    [string] $NetCoreAppVersion = "netcoreapp3.1",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [string] $Version = "0.0.0",
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerBuild
)

$ErrorActionPreference = "Stop";
$ConfirmPreference = "None";

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
$ArchCross = $null

$CmakeGenerator = $null
$CommandCmake = $null
$CommandDotnet = $null
$CommandMake = $null
$CommandSnapx = $null
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
        $CommandSnapx = "snapx.exe"
        $CommandVsWhere = Join-Path $ToolsDir vswhere-win-x64.exe
        $Arch = "win-msvs-$($VisualStudioVersion)-x64"
        $ArchCross = "x86_64-win64-gcc"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CmakeGenerator = "Unix Makefiles"
        $CommandCmake = "cmake"
        $CommandDotnet = "dotnet"
        $CommandMake = "make"
        $CommandSnapx = "snapx"
        $Arch = "x86_64-linux-gcc"
        $ArchCross = "x86_64-w64-mingw32-gcc"
    }
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

$TargetArch = $Arch
$TargetArchDotNet = $NetCoreAppVersion
if ($Cross) {
    $TargetArch = $ArchCross
}

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

    if ($Cross -eq $TRUE -and $OsPlatform -eq "Unix") {
        $CmakeToolChainFile = Join-Path $WorkingDir cmake\Toolchain-x86_64-w64-mingw32.cmake
        $CmakeArguments += "-DCMAKE_TOOLCHAIN_FILE=""$CmakeToolChainFile"""
    }
    elseif ($Cross -eq $TRUE) {
        Write-Error "Cross compiling is not support on: $OSPlatform"
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
    Resolve-Shell-Dependency $CommandSnapx

    Invoke-Command-Clean-Dotnet-Directory $SnapDotnetSrcDir

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapDotnetSrcDir Snap.csproj))
        "/p:Version=$Version",
        "/p:SnapNupkg=true",
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
    Resolve-Shell-Dependency $CommandSnapx

    Invoke-Command-Clean-Dotnet-Directory $SnapxDotnetSrcDir

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapxDotnetSrcDir Snapx.csproj))
        "/p:Version=$Version",
        "/p:SnapNupkg=true",
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
    Resolve-Shell-Dependency $CommandSnapx

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

    Write-Output "Build src directory: $SnapInstallerDotnetSrcDir"
    Write-Output "Build output directory: $SnapInstallerDotnetBuildPublishDir"
    Write-Output "Arch: $TargetArchDotNet"
    Write-Output "Rid: $Rid"
    Write-Output "PackerArch: $PackerArch"
    Write-Output ""

    Invoke-Command-Clean-Dotnet-Directory $SnapInstallerDotnetSrcDir

    Invoke-Command-Colored $CommandDotnet @(
        ("publish {0}" -f (Join-Path $SnapInstallerDotnetSrcDir Snap.Installer.csproj))
        "/p:PublishTrimmed=true"
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "/p:SnapNativeConfiguration=$Configuration"
        "/p:Version=$Version"
        "--runtime $Rid"
        "--framework $TargetArchDotNet"
        "--self-contained true"
        "--output $SnapInstallerDotnetBuildPublishDir"
    )

    if ($Rid -eq "win-x64") {
        Invoke-Command-Colored $CommandSnapx @(
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

            # MINGW
            $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\$Configuration\Snap.CoreRun.Tests)

            # MSVS
            $Projects += (Join-Path $WorkingDir build\native\Windows\win-msvs-${VisualStudioVersion}-x64\${Configuration}\Snap.CoreRun.Tests\${Configuration})

            $TestRunnerCount = $Projects.Length
            Write-Output "Running native windows unit tests. Test runner count: $TestRunnerCount"

            foreach ($GoogleTestsDir in $Projects) {
                Invoke-Google-Tests $GoogleTestsDir corerun_tests.exe $CommandGTestsDefaultArguments
            }
        }
        "Unix" {
            $Projects = @()

            # GCC
            $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\${Configuration}\Snap.CoreRun.Tests)

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

    $Projects = @()
    $Projects += Join-Path $SrcDir Snap.Installer.Tests
    $Projects += Join-Path $SrcDir Snap.Tests
    $Projects += Join-Path $SrcDir Snapx.Tests

    $ProjectsCount = $Projects.Length

    Write-Output-Header "Running dotnet tests. Test project count: $ProjectsCount"

    foreach($Project in $Projects)
    {
        $TestProjectName = Split-Path $Project -LeafBase
        $TestAdapterPath = Split-Path $Project
        $TestResultsOutputDirectoryPath = Join-Path $WorkingDir build\dotnet\TestResults\$TestProjectName

        Invoke-Command-Colored $CommandDotnet @(
            "test"
            "$Project"
            "--configuration=$Configuration"
            "--verbosity normal"
			"--test-adapter-path:""$TestAdapterPath"""
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

if ($Cross) {
    Write-Output "Native target arch: $ArchCross"
}
else {
    Write-Output "Native target arch: $Arch"
    if($OSPlatform -eq "Windows")
    {
        Write-Output "Visual Studio Version: $VisualStudioVersion"
    }
}

switch ($OSPlatform) {
    "Windows" {
        Resolve-Windows $OSPlatform
        Resolve-Shell-Dependency $CommandVsWhere
        Use-Msvs-Toolchain -VisualStudioVersion $VisualStudioVersion
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
                if ($Cross -eq $FALSE) {
                    Invoke-Build-Native
                }
                else {
                    Write-Error "Cross compiling is not supported on Windows."
                }
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
