param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Native", "Snap", "Snap-Installer", "Run-Native-UnitTests")]
    [string] $Target = "Native",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [boolean] $Cross = $FALSE,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [boolean] $Lto = $FALSE,
    [Parameter(Position = 4, ValueFromPipeline = $true)]
    [string] $DotNetRid = $null
)

$ErrorActionPreference = "Stop"; 
$ConfirmPreference = "None"; 

# Global Variables
$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$BuildUsingDocker = $env:SNAPX_DOCKER_BUILD -gt 0

$ToolsDir = Join-Path $WorkingDir tools

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount
$Arch = $null
$ArchCross = $null

$CmakeGenerator = $null
$CommandCmake = $null
$CommandDotnet = $null
$CommandMsBuild = $null
$CommandMake = $null
$CommandPacker = $null
$CommandSnapx = $null
$CommandVsWhere = $null
$CommandGTestsDefaultArguments = @(
    "--gtest_break_on_failure"
    "--gtest_shuffle"
)

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CmakeGenerator = "Visual Studio 15 Win64"
        $CommandCmake = "cmake.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandPacker = Join-Path $ToolsDir warp-packer-win-x64.exe
        $CommandSnapx = "snapx.exe"
        $CommandVsWhere = Join-Path $ToolsDir vswhere-win-x64.exe
        $Arch = "win-x64"
        $ArchCross = "x86_64-win64-gcc"
    }
    "^Unix" {
        $OSPlatform = "Unix"
        $CmakeGenerator = "Unix Makefiles"
        $CommandCmake = "cmake"
        $CommandDotnet = "dotnet"
        $CommandMake = "make"
        $CommandPacker = Join-Path $ToolsDir warp-packer-linux-x64.exe
        $CommandSnapx = "snapx"
        $Arch = "x86_64-linux-gcc"
        $ArchCross = "x86_64-w64-mingw32-gcc"    
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

function Build-Native {	
    $SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$TargetArch\$Configuration

    Write-Output-Header "Building native dependencies"

    $CmakeArguments = @(
        "-G""$CmakeGenerator"""
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
        
            $SnapTestsExePath = Join-Path $SnapCoreRunBuildOutputDir $Configuration\Snap.Tests.exe
            $CoreRunExePath = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\$Configuration
            Copy-Item $SnapTestsExePath $CoreRunExePath | Out-Null        
        }
        default {
            Write-Error "Unsupported os platform: $OSPlatform"
        }
    }

}

function Build-Snap {
    Write-Output-Header "Building Snap"

    Resolve-Shell-Dependency $CommandSnapx

    Invoke-Command-Colored $CommandDotnet @(
        "clean $SnapNetSrcDir"
    )

    Invoke-Command-Colored $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapNetSrcDir Snap.csproj))
        "/p:SnapNupkg=true",
        "--configuration $Configuration"
    )
}
function Build-Snap-Installer {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("win-x64", "linux-x64")]
        [string] $Rid 
    )
    Write-Output-Header "Building Snap.Installer"

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

Write-Output-Header "----------------------- CONFIGURATION DETAILS ------------------------" 
Write-Output "OS: $OSVersion"
Write-Output "OS Platform: $OSPlatform"
Write-Output "Processor count: $ProcessorCount"
Write-Output "Configuration: $Configuration"
Write-Output "Docker: $BuildUsingDocker" 

if ($Cross) {
    Write-Output "Native target arch: $ArchCross"		
}
else {
    Write-Output "Native target arch: $Arch"				
}

switch ($OSPlatform) {
    "Windows" {
        Resolve-Windows $OSPlatform 			
        Resolve-Shell-Dependency $CommandVsWhere				
        Use-Msvs-Toolchain
        Resolve-Shell-Dependency $CommandMsBuild
    }
    "Unix" {
        Resolve-Unix $OSPlatform
        Resolve-Shell-Dependency $CommandMake
    }
    default {
        Write-Error "Unsupported os platform: $OSPlatform"
    }
}
			
Resolve-Shell-Dependency $CommandCmake
Resolve-Shell-Dependency $CommandPacker
Resolve-Shell-Dependency $CommandDotnet

switch ($Target) {
    "Native" {
        switch ($OSPlatform) {
            "Windows" {		
                if ($Cross -eq $FALSE) {
                    Build-Native		
                }
                else {
                    Write-Error "Cross compiling is not supported on Windows."
                } 
            }
            "Unix" {
                Build-Native		
            }
            default {
                Write-Error "Unsupported os platform: $OSPlatform"
            }
        }
    }
    "Run-Native-UnitTests" {
        switch($OSPlatform)
        {
            "Windows" {          

                $Projects = @() 

                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\Debug)
                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\Release)
                $Projects += (Join-Path $WorkingDir build\native\Windows\win-x64\Debug\Snap.CoreRun\Debug)
                $Projects += (Join-Path $WorkingDir build\native\Windows\win-x64\Release\Snap.CoreRun\Release)

                Write-Output "Running native windows unit tests. Test runner count: {0}" -f ($Projects.Length)

                foreach ($GoogleTestsDir in $Projects) {
                    Invoke-Google-Tests $GoogleTestsDir Snap.Tests.exe $CommandGTestsDefaultArguments
                }                
            }
            "Unix" {                
                $Projects = @()

                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\Debug)
                $Projects += (Join-Path $WorkingDir build\native\Unix\x86_64-linux-gcc\Release)
                
                Write-Output "Running native unix unit tests. Test runner count: {0}" -f ($Projects.Length)

                foreach ($GoogleTestsDir in $Projects) {
                    Invoke-Google-Tests $GoogleTestsDir Snap.Tests $CommandGTestsDefaultArguments
                }                

            }
            default {
                Write-Error "Unsupported os platform: $OSPlatform"
            }
        }
    }
    "Snap" {
        Build-Snap
    }
    "Snap-Installer" {
        Build-Snap-Installer -Rid $DotNetRid
    }
}
