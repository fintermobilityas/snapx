param(
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [ValidateSet("Native", "Snap", "Snapx", "Snap-Installer", "Run-Native-UnitTests", "Build-Dotnet-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target,
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $Configuration,
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
    [switch] $Lto,
    [Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("win-x86", "win-x64", "linux-x64", "linux-arm64")]
    [string] $Rid = $null,
    [Parameter(Position = 4, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $NetCoreAppVersion,
    [Parameter(Position = 5, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $Version,
    [Parameter(Position = 6, ValueFromPipelineByPropertyName = $true)]
    [switch] $CIBuild,
    [Parameter(Position = 7, ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerBuild
)

# Global Variables
$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$ToolsDir = Join-Path $WorkingDir tools
$SrcDir = Join-Path $WorkingDir src
$NupkgsDir = Join-Path $WorkingDir nupkgs
$CmakeFilesDir = Join-Path $WorkingDir cmake

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount

# TODO: REMOVE ME
$VisualStudioVersion = 0

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
        $VisualStudioVersion = 17
        $CmakeGenerator = "Visual Studio $VisualStudioVersion 2022"
        $CommandCmake = "cmake.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandVsWhere = Join-Path $ToolsDir vswhere-win-x64.exe
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CmakeGenerator = "Unix Makefiles"
        $CommandCmake = "cmake"
        $CommandDotnet = "dotnet"
        $CommandMake = "make"
    }
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

# Projects
$SnapCoreRunSrcDir = Join-Path $WorkingDir src
$SnapDotnetSrcDir = Join-Path $WorkingDir src\Snap
$SnapInstallerDotnetSrcDir = Join-Path $WorkingDir src\Snap.Installer
$SnapxDotnetSrcDir = Join-Path $WorkingDir src\Snapx

function Invoke-Build-Native {
    Write-Output-Header "Building native dependencies"

    Resolve-Shell-Dependency $CommandCmake

    $SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$Rid\$Configuration

    $CmakeArchNewSyntaxPrefix = ""
    $CmakeGenerator = $CmakeGenerator

    $CmakeArguments = @()

    if($OSPlatform -eq "Unix") {
        if($Rid -eq "linux-arm64") {
            $LinuxAarch64Toolchain = Join-Path $CmakeFilesDir aarch64-linux-gnu.toolchain.cmake
            $CmakeArguments += "-DCMAKE_TOOLCHAIN_FILE=""$LinuxAarch64Toolchain"""
        }
    } elseif($OSPlatform -eq "Windows")
    {
        switch($Rid) {
            "win-x86" {
                $CmakeArchNewSyntaxPrefix = "-A Win32"
            }
            "win-x64" {
                $CmakeArchNewSyntaxPrefix = "-A x64"
            }
            Default {
                Invoke-Exit "Rid not supported: $Rid"
            }
        }
    }

    $CmakeArguments = @(
        $CmakeArguments,
        "-G""$CmakeGenerator"""
        "$CmakeArchNewSyntaxPrefix"
        "-H""$SnapCoreRunSrcDir"""
        "-B""$SnapCoreRunBuildOutputDir"""
    )

    if ($Lto) {
        $CmakeArguments += "-DBUILD_ENABLE_LTO=ON"
    }

    if($CIBuild) {
        $CmakeArguments += "-DBUILD_ENABLE_TESTS=ON"
    }

    if ($Configuration -eq "Debug") {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Debug"
    }
    else {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Release"
    }

    Write-Output "Build src directory: $SnapCoreRunSrcDir"
    Write-Output "Build output directory: $SnapCoreRunBuildOutputDir"
    Write-Output "Rid: $Rid"
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
    Write-Output-Header "Building Snap.Installer"

    Resolve-Shell-Dependency $CommandDotnet

    $SnapInstallerExeName = $null

    switch ($Rid) {
        "win-x86" {
            $SnapInstallerExeName = "Snap.Installer.exe"
        }
        "win-x64" {
            $SnapInstallerExeName = "Snap.Installer.exe"
        }
        "linux-x64" {
            $SnapInstallerExeName = "Snap.Installer"
        }
        "linux-arm64" {
            $SnapInstallerExeName = "Snap.Installer"
        }
        default {
            Write-Error "Rid not supported: $Rid"
        }
    }

    $SnapInstallerDotnetBuildPublishDir = Join-Path $WorkingDir build\dotnet\$Rid\Snap.Installer\$NetCoreAppVersion\$Configuration\publish
    $SnapInstallerExeAbsolutePath = Join-Path $SnapInstallerDotnetBuildPublishDir $SnapInstallerExeName
    $SnapInstallerExeZipAbsolutePath = Join-Path $SnapInstallerDotnetBuildPublishDir "Setup-$Rid.zip"
    $SnapInstallerCsProj = Join-Path $SnapInstallerDotnetSrcDir Snap.Installer.csproj
    $SnapxCsProj = Join-Path $SnapxDotnetSrcDir Snapx.csproj

    Write-Output "Build src directory: $SnapInstallerDotnetSrcDir"
    Write-Output "Build output directory: $SnapInstallerDotnetBuildPublishDir"
    Write-Output "NetCoreAppVersion: $NetCoreAppVersion"
    Write-Output "Rid: $Rid"
    Write-Output ""

    if(Test-Path $SnapInstallerDotnetBuildPublishDir) {
        Get-Item $SnapInstallerDotnetBuildPublishDir | Remove-Item -Recurse -Force
    }

    $PublishReadyToRun = "False"

    $RuntimeIdentifier = $Rid

    Invoke-Command-Colored $CommandDotnet @(
        "publish $SnapInstallerCsProj"
        "/p:PublishTrimmed=" + ($Configuration -eq "Debug" ? "False" : "True")
        "/p:PublishReadyToRun=" + $PublishReadyToRun
        "/p:Version=$Version"
        "/p:SnapRid=$Rid"
        "--runtime $RuntimeIdentifier"
        "--framework $NetCoreAppVersion"
        "--self-contained true"
        "--output $SnapInstallerDotnetBuildPublishDir"
        "--configuration $Configuration"
    )

    if ($Rid.StartsWith("win-")) {
        Invoke-Command-Colored $CommandDotnet @(
            "build $SnapxCsProj"
            "-p:SnapBootstrap=True"
            "--configuration $Configuration"
            "--framework $NetCoreAppVersion"
        )

        Invoke-Command-Colored $CommandDotnet @(
            "run"
            "--project $SnapxCsProj"
            "--configuration $Configuration"
            "--framework $NetCoreAppVersion"
            "--no-build"
            "--"
            "rcedit"
            "$SnapInstallerExeAbsolutePath"
            "--gui-app"
        )
    } else {
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

            $MsvsProject = Join-Path $WorkingDir build\native\Windows\$Rid\${Configuration}\Snap.CoreRun.Tests\${Configuration}
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

            $GccProject = Join-Path $WorkingDir build\native\Unix\$Rid\${Configuration}\Snap.CoreRun.Tests
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
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [switch] $BuildOnly
    )

    Push-Location $WorkingDir

    Resolve-Shell-Dependency $CommandDotnet

    # Todo: Enable win-x86 tests when this issue has been resolved:
    # https://github.com/actions/setup-dotnet/issues/25
    $IsWinX86CIBuild = $CIBuild -and $Rid -eq "win-x86"

    $Projects = @(
        @{
            SrcDirectory = Join-Path $SrcDir Snap.Tests
            Framework = $NetCoreAppVersion
            OSPlatform = "Any"
            Skip = $IsWinX86CIBuild
        }
        @{
            SrcDirectory = Join-Path $SrcDir Snap.Tests
            Framework = "net7.0"
            OSPlatform = "Any"
            Skip = $IsWinX86CIBuild
        }
        @{
            SrcDirectory = Join-Path $SrcDir Snap.Installer.Tests
            Framework = $NetCoreAppVersion
            OSPlatform = "Any"
            Skip = $IsWinX86CIBuild
        }
        @{
            SrcDirectory = Join-Path $SrcDir Snapx.Tests
            Framework = $NetCoreAppVersion
            OSPlatform = "Any"
            Skip = $IsWinX86CIBuild
        }
    )

    foreach($ProjectKv in $Projects)
    {
        $ProjectSrcDirectory = $ProjectKv.SrcDirectory
        $ProjectName = Split-Path $ProjectSrcDirectory -Leaf
        $ProjectDotnetFramework = $ProjectKv.Framework
        $ProjectSkip = $ProjectKv.Skip
        $ProjectOSPlatform = $ProjectKv.OSPlatform
        $ProjectTestResultsDirectory = Join-Path $WorkingDir build\dotnet\$Rid\TestResults\$ProjectName

        if(($ProjectOSPlatform -ne "Any") -and ($OSPlatform -ne $ProjectOSPlatform)) {
            continue
        }

        if($ProjectSkip) {
            continue
        }

        Invoke-Dotnet-Clear $ProjectSrcDirectory

        $Platform = $null

        switch($Rid) {
            "win-x86" {
                $Platform = "x86"
            }
            "win-x64" {
                $Platform = "x64"
            }
            "linux-x64" {
                $Platform = "x64"
            }
            "linux-arm64" {
                $Platform = "arm64"
            }
            Default {
                Invoke-Exit "Rid not supported: $Rid"
            }
        }

        Write-Output-Colored "Building tests for project $ProjectName - .NET sdk: $ProjectDotnetFramework"

        $BuildProperties = @(
            "/p:SnapInstallerAllowElevatedContext=" + ($CIBuild ? "True" : "False")
            "/p:SnapRid=$Rid"
            "/p:TargetFrameworks=$ProjectDotnetFramework"
        )

        Invoke-Command-Colored $CommandDotnet @(
            "build"
            $BuildProperties
            "--configuration $Configuration"
            "--framework $ProjectDotnetFramework"
            "--arch $Platform"
            $ProjectSrcDirectory
        )

        if($BuildOnly) {
            continue
        }

        Write-Output-Colored "Running tests for project $ProjectName - .NET sdk: $ProjectDotnetFramework"

        Invoke-Command-Colored $CommandDotnet @(
            "test"
            $ProjectSrcDirectory
            $BuildProperties
            "--configuration $Configuration"
            "--framework $ProjectDotnetFramework"
            "--arch $Platform"
            "--verbosity normal"
            "--no-build"
            "--logger:""xunit;LogFileName=TestResults.xml"""
            "--results-directory:""$ProjectTestResultsDirectory"""
        )
    }

}

Write-Output-Header "----------------------- CONFIGURATION DETAILS ------------------------"
Write-Output "OS: $OSVersion"
Write-Output "OS Platform: $OSPlatform"
Write-Output "Processor count: $ProcessorCount"
Write-Output "Configuration: $Configuration"
Write-Output "Docker: $DockerBuild"
Write-Output "CIBuild: $CIBuild"
Write-Output "Rid: $Rid"

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
        Invoke-Build-Snap-Installer
    }
    "Run-Native-UnitTests" {
        Invoke-Native-UnitTests
    }
    "Build-Dotnet-UnitTests" {
        Invoke-Dotnet-UnitTests -BuildOnly
    }
    "Run-Dotnet-UnitTests" {
        Invoke-Dotnet-UnitTests
    }
}
