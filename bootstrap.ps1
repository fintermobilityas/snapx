param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Native", "Snap", "Snap-Installer", "Run-Native-UnitTests", "Run-Dotnet-UnitTests")]
    [string] $Target = "Native",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [boolean] $Cross = $FALSE,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [boolean] $Lto = $FALSE,
    [Parameter(Position = 4, ValueFromPipeline = $true)]
    [string] $DotNetRid = $null,
    [Parameter(Position = 5, ValueFromPipeline = $true)]
    [Validateset(15, 16)]
    [int] $VisualStudioVersion = 15
)

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

# Global Variables
$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$ToolsDir = Join-Path $WorkingDir tools
$SrcDir = Join-Path $WorkingDir src

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

        if($env:SNAPX_CI_BUILD -eq $true) {
            $CommandSnapx = Join-Path $ToolsDir snapx\snapx.exe
        }
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

        if($env:SNAPX_CI_BUILD -eq $true) {
            $CommandSnapx = Join-Path $ToolsDir snapx\snapx
        }
    }	
    default {
        Write-Error "Unsupported os: $OSVersion"
    }
}

$TargetArch = $Arch
$TargetArchDotNet = "netcoreapp2.2"
if ($Cross) {
    $TargetArch = $ArchCross
}

# Projects
$SnapCoreRunSrcDir = Join-Path $WorkingDir src
$SnapNetSrcDir = Join-Path $WorkingDir src\Snap
$SnapInstallerNetSrcDir = Join-Path $WorkingDir src\Snap.Installer

function Invoke-Build-Native {	
    Write-Output-Header "Building native dependencies"

    Resolve-Shell-Dependency $CommandCmake

    $SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$TargetArch\$Configuration

    $CmakeArchNewSyntaxPrefix = ""
    $CmakeGenerator = $CmakeGenerator

    if($OSPlatform -eq "Windows")
    {
        if($VisualStudioVersion -ge 16) {
            $CmakeGenerator += " 2019"
            $CmakeArchNewSyntaxPrefix = "-A x64"
        } else {
            $CmakeGenerator += " 2017 Win64"
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
            sh -c "(cd $SnapCoreRunBuildOutputDir && make -j $ProcessorCount)"
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

    Invoke-Command-Colored $CommandDotnet @(
        "clean $SnapNetSrcDir"
    )

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapNetSrcDir Snap.csproj))
        "/p:SnapNupkg=true",
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "--configuration $Configuration"
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
    $MonoLinkerCrossGenEnabled = $false

    switch ($Rid) {
        "win-x64" {
            $PackerArch = "windows-x64"
            $SnapInstallerExeName = "Snap.Installer.exe"

            if ($OSPlatform -eq "Windows") {
                $MonoLinkerCrossGenEnabled = $true
            }
        }
        "linux-x64" {
            $PackerArch = "linux-x64"
            $SnapInstallerExeName = "Snap.Installer"
			
			if ($OSPlatform -eq "Unix") {
                $MonoLinkerCrossGenEnabled = $true
            }
        }
        default {
            Write-Error "Rid not supported: $Rid"
        }
    }

    $SnapInstallerNetBuildPublishDir = Join-Path $WorkingDir build\dotnet\$Rid\Snap.Installer\$TargetArchDotNet\$Configuration\publish
    $SnapInstallerExeAbsolutePath = Join-Path $SnapInstallerNetBuildPublishDir $SnapInstallerExeName
    $SnapInstallerExeZipAbsolutePath = Join-Path $SnapInstallerNetBuildPublishDir "Setup-$Rid.zip"

    Write-Output "Build src directory: $SnapInstallerNetSrcDir"
    Write-Output "Build output directory: $SnapInstallerNetBuildPublishDir"
    Write-Output "Arch: $TargetArchDotNet"
    Write-Output "Rid: $Rid"
    Write-Output "PackerArch: $PackerArch"
    Write-Output ""

    Invoke-Command-Colored $CommandDotnet @(
        "clean $SnapInstallerNetSrcDir"
    )

    Invoke-Command-Colored $CommandDotnet @(
        ("publish {0}" -f (Join-Path $SnapInstallerNetSrcDir Snap.Installer.csproj))
        "/p:ShowLinkerSizeComparison=true"
        "/p:CrossGenDuringPublish=$MonoLinkerCrossGenEnabled"
        "/p:SnapMsvsToolsetVersion=$VisualStudioVersion"
        "/p:SnapNativeConfiguration=$Configuration"
        "--runtime $Rid"
        "--framework $TargetArchDotNet"
        "--self-contained true"
        "--output $SnapInstallerNetBuildPublishDir"
    )

    if ($Rid -eq "win-x64") {
        Invoke-Command-Colored $CommandSnapx @(          
            "rcedit"
            "--gui-app" 
            "--filename $SnapInstallerExeAbsolutePath"
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
        -Path $SnapInstallerNetBuildPublishDir\* `
        -CompressionLevel Optimal `
        -DestinationPath $SnapInstallerExeZipAbsolutePath
}

function INvoke-Native-Unit-Tests
{
    switch($OSPlatform)
    {
        "Windows" {          

            $Projects = @() 

            # MINGW
            if($env:SNAPX_CI_BUILD -eq $false) {
                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\Debug\Snap.CoreRun.Tests)
            }
            $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\Release\Snap.CoreRun.Tests)
            
            # MSVS
            if($env:SNAPX_CI_BUILD -eq $false) {
                $Projects += (Join-Path $WorkingDir build\native\Windows\win-msvs-$($VisualStudioVersion)-x64\Debug\Snap.CoreRun.Tests\Debug)
            }
            $Projects += (Join-Path $WorkingDir build\native\Windows\win-msvs-$($VisualStudioVersion)-x64\Release\Snap.CoreRun.Tests\Release)

            $TestRunnerCount = $Projects.Length
            Write-Output "Running native windows unit tests. Test runner count: $TestRunnerCount"

            foreach ($GoogleTestsDir in $Projects) {
                Invoke-Google-Tests $GoogleTestsDir corerun_tests.exe $CommandGTestsDefaultArguments
            }                
        }
        "Unix" {                
            $Projects = @()

            # GCC
            if($env:SNAPX_CI_BUILD -eq $false) {
                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\Debug\Snap.CoreRun.Tests)
            }            
            $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\Release\Snap.CoreRun.Tests)
            
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

function Invoke-Dotnet-Unit-Tests
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
        Invoke-Command-Colored $CommandDotnet @(
            "test"
            "$Project",  
            "--logger trx", 
            "--verbosity=normal",
            "--configuration=$Configuration"
            # "--", # RunSettings
            # "RunConfiguration.TestSessionTimeout=60000"
        )    
    }

}

Write-Output-Header "----------------------- CONFIGURATION DETAILS ------------------------" 
Write-Output "OS: $OSVersion"
Write-Output "OS Platform: $OSPlatform"
Write-Output "Processor count: $ProcessorCount"
Write-Output "Configuration: $Configuration"
Write-Output "Docker: ${env:SNAPX_DOCKER_BUILD}" 
Write-Output "CI Build: ${env:SNAPX_CI_BUILD}"

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
    "Snap-Installer" {
        Invoke-Build-Snap-Installer -Rid $DotNetRid
    }
    "Run-Native-UnitTests" {
        INvoke-Native-Unit-Tests   
    }
    "Run-Dotnet-UnitTests" {
        Invoke-Dotnet-Unit-Tests
    }
}
