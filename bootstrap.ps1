param(
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [ValidateSet("Native", "Snap", "Snapx", "Snap-Installer", "Run-Native-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target,
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true, Mandatory = $true)]
    [string] $Configuration,
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
    [switch] $Lto,
    [Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
    [ValidateSet("win-x86", "win-x64", "linux-x64")]
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

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount
$Utf8NoBomEncoding = New-Object System.Text.UTF8Encoding $False

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
        $VisualStudioVersion = 16
        $CmakeGenerator = "Visual Studio $VisualStudioVersion 2019"
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

    if($OSPlatform -eq "Windows")
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

    Invoke-Command-Colored $CommandDotnet @(
        "publish $SnapInstallerCsProj"
        "/p:PublishTrimmed=" + ($Configuration -eq "Debug" ? "False" : "True")
        "/p:Version=$Version"
        "/p:SnapRid=$Rid"
        "--runtime $Rid"
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
    Push-Location $WorkingDir

    Resolve-Shell-Dependency $CommandDotnet

    $GlobalJsonFilename = Join-Path $WorkingDir global.json    
    Invoke-Command-Colored git @("checkout $GlobalJsonFilename")

    $GlobalJson = Get-Content $GlobalJsonFilename
    $DefaultDotnetVersion = $GlobalJson | ConvertFrom-Json | Select-Object -ExpandProperty sdk | Select-Object -ExpandProperty version

    # NB! This only works for Github Actions. If you are having problems
    # set ReplaceGlobalJson to $false.

    $Projects = @(
        @{
            SrcDirectory = Join-Path $SrcDir Snap.Tests
            DotnetVersion = "2.1.807"
            ReplaceGlobalJson = $OSPlatform -eq "Windows" -and $CIBuild
        }
        @{
            SrcDirectory = Join-Path $SrcDir Snap.Installer.Tests
            DotnetVersion = $DefaultDotnetVersion
            ReplaceGlobalJson = $false
        }
        @{            
            SrcDirectory = Join-Path $SrcDir Snapx.Tests
            DotnetVersion = $DefaultDotnetVersion
            ReplaceGlobalJson = $false
        }
    )

    foreach($ProjectKv in $Projects)
    {

        try {
            $ProjectSrcDirectory = $ProjectKv.SrcDirectory
            $ProjectName = Split-Path $ProjectSrcDirectory -Leaf
            $ProjectDotnetVersion = $ProjectKv.DotnetVersion
            $ProjectReplaceGlobalJson = $ProjectKv.ReplaceGlobalJson    
            $ProjectTestResultsDirectory = Join-Path $WorkingDir build\dotnet\$Rid\TestResults\$ProjectName

            Invoke-Dotnet-Clear $ProjectSrcDirectory

            Write-Output-Colored "Running tests for project $ProjectName - .NET sdk version: $ProjectDotnetVersion"

            # TODO: Remove me code when https://github.com/actions/setup-dotnet/issues/25 is resolved.
            if($ProjectReplaceGlobalJson) {
                if($ProjectDotnetVersion -ne $DefaultDotnetVersion) {
                    if(Test-Path $GlobalJsonFilename) {
                        Remove-Item $GlobalJsonFilename
                    }
                    dotnet new global.json --sdk-version $ProjectDotnetVersion
                } else {
                    $GlobalJson | Out-File $GlobalJsonFilename -Encoding $Utf8NoBomEncoding -Force
                }        
            } else {
                Invoke-Command-Colored git @("checkout $GlobalJsonFilename")
            }
            
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
                Default {
                    Invoke-Exit "Rid not supported: $Rid"
                }
            }
    
            $BuildProperties = @(
                "/p:SnapInstallerAllowElevatedContext=" + ($CIBuild ? "True" : "False")
                "/p:SnapRid=$Rid"
                "/p:Platform=$Platform"
            )
    
            Invoke-Command-Colored $CommandDotnet @(
                "build"
                $BuildProperties
                "--configuration $Configuration"
                "$ProjectSrcDirectory"
            )
    
            Invoke-Command-Colored $CommandDotnet @(
                "test"
                $BuildProperties
                "$ProjectSrcDirectory"
                "--configuration $Configuration"
                "--verbosity normal"
                "--no-build"
                "--logger:""xunit;LogFileName=TestResults.xml"""
                "--results-directory:""$ProjectTestResultsDirectory"""
            )    
        } finally {
            if($ProjectReplaceGlobalJson) {
                Invoke-Command-Colored git @("checkout $GlobalJsonFilename")
            }
        }
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
    "Run-Dotnet-UnitTests" {
        Invoke-Dotnet-UnitTests
    }
}
