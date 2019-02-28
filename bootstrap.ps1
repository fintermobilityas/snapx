param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Native", "Snap", "Snap-Installer", "Run-Native-Mingw-UnitTests-Windows")]
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

$BuildUsingDocker = $env:SNAPX_DOCKER_BUILD -gt 0
if($BuildUsingDocker)
{
    $WorkingDir = "/build/snapx"
} else {
    $WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
}

$ToolsDir = Join-Path $WorkingDir tools

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount
$Arch = $null
$ArchCross = $null

$CmakeGenerator = $null
$CommandCmake = $null
$CommandGit = $null
$CommandDotnet = $null
$CommandMsBuild = $null
$CommandMake = $null
$CommandUpx = $null
$CommandPacker = $null
$CommandSnapx = $null
$CommandVsWhere = $null
$CommandGTestsDefaultArguments = @("--gtest_break_on_failure", "--gtest_repeat=3", "--gtest_shuffle")

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CmakeGenerator = "Visual Studio 15 Win64"
        $CommandCmake = "cmake.exe"
        $CommandGit = "git.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandUpx = Join-Path $ToolsDir upx-win-x64.exe
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
        $CommandGit = "git"
        $CommandDotnet = "dotnet"
        $CommandMake = "make"
        $CommandUpx = "upx"
        $CommandPacker = Join-Path $ToolsDir warp-packer-linux-x64.exe
        $CommandSnapx = "snapx"
        $Arch = "x86_64-linux-gcc"
        $ArchCross = "x86_64-w64-mingw32-gcc"

        if($BuildUsingDocker -eq $false)
        {
            $CommandCmakeMaybe = Join-Path $WorkingDir cmake\cmake-x-xx\bin\cmake
            if(Test-Path $CommandCmakeMaybe)
            {
                $CommandCmake = $CommandCmakeMaybe
            }    
        }
        
    }	
    default {
        Die "Unsupported os: $OSVersion"
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

# Functions

function Die {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    $MessageDashes = "-" * [math]::min($Message.Length, [console]::BufferWidth)

    Write-Output-Colored $MessageDashes -ForegroundColor White
    Write-Output-Colored $Message -ForegroundColor Red
    Write-Output-Colored $MessageDashes -ForegroundColor White

    $exitCode = $LASTEXITCODE
    if(0 -eq $exitCode)
    {
        $exitCode = 1
    }

    exit $exitCode
}
function Write-Output-Colored {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message,
        [Parameter(Position = 1, Mandatory = $false, ValueFromPipeline = $true)]
        [string] $ForegroundColor = "Green"
    )
	
    $fc = $host.UI.RawUI.ForegroundColor

    $host.UI.RawUI.ForegroundColor = $ForegroundColor

    Write-Output $Message

    $host.UI.RawUI.ForegroundColor = $fc
}
function Write-Output-Header {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Output-Colored $Message -ForegroundColor Green
    Write-Host
}
function Write-Output-Header-Warn {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Output-Colored $Message -ForegroundColor Yellow
    Write-Host
}
function Requires-Cmake {
    if ($null -eq (Get-Command $CommandCmake -ErrorAction SilentlyContinue)) {
        Die "Unable to find cmake executable in environment path: $CommandCmake"
    }
}
function Requires-Git {
    if ($null -eq (Get-Command $CommandGit -ErrorAction SilentlyContinue)) {
        Die "Unable to find git executable in environment path: $CommandGit"
    }
}
function Requires-Dotnet {
    if ($null -eq (Get-Command $CommandDotnet -ErrorAction SilentlyContinue)) {
        Die "Unable to find dotnet executable in environment path: $CommandDotnet"
    }
}
function Requires-Msbuild {
    if ($null -eq (Get-Command $CommandMsBuild -ErrorAction SilentlyContinue)) {
        Die "Unable to find msbuild executable in environment path: $CommandMsBuild"
    }
}
function Requires-Make {
    if ($null -eq (Get-Command $CommandMake -ErrorAction SilentlyContinue)) {
        Die "Unable to find make executable in environment path: $CommandMake"
    }
}
function Requires-Upx {
    if ($null -eq (Get-Command $CommandUpx -ErrorAction SilentlyContinue)) {
        Die "Unable to find upx executable in environment path: $CommandUpx"
    }
}
function Requires-Packer {
    if ($null -eq (Get-Command $CommandPacker -ErrorAction SilentlyContinue)) {
        Die "Unable to find packer executable in environment path: $CommandPacker"
    }
}
function Requires-Snapx {
    if ($null -eq (Get-Command $CommandSnapx -ErrorAction SilentlyContinue)) {
        Die "Unable to find snapx executable in environment path: $CommandSnapx"
    }
}
function Requires-VsWhere {
    if ($null -eq (Get-Command $CommandVsWhere -ErrorAction SilentlyContinue)) {
        Die "Unable to find vswhere executable in environment path: $CommandVsWhere"
    }
}
function Requires-Unix {
    if ($OSPlatform -ne "Unix") {
        Die "Unable to continue because OS version is not Unix but $OSVersion"
    }	
}
function Requires-Windows {
    if ($OSPlatform -ne "Windows") {
        Die "Unable to continue because OS version is not Windows but $OSVersion"
    }	
}
function Command-Exec {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Command,
        [Parameter(Position = 1, ValueFromPipeline = $true)]
        [string[]] $Arguments
    )

    if($Arguments.Length -gt 0) {
        $CommandStr = "{0} {1}" -f $Command, ($Arguments -join " ")
    } else {
        $CommandStr = $Command
    }
    $CommandDashes = "-" * [math]::min($CommandStr.Length, [console]::BufferWidth)

    Write-Output-Colored $CommandDashes -ForegroundColor White
    Write-Output-Colored $CommandStr -ForegroundColor Green
    Write-Output-Colored $CommandDashes -ForegroundColor White
    
    Invoke-Expression $CommandStr

    if ($LASTEXITCODE -ne 0) {
        Die "Command failed: $CommandStr"
    }
}
function Invoke-BatchFile {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Path, 
        [Parameter(Position = 1, Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Parameters
    )

    $tempFile = [IO.Path]::GetTempFileName()
    $batFile = [IO.Path]::GetTempFileName() + ".cmd"

    Set-Content -Path $batFile -Value "`"$Path`" $Parameters && set > `"$tempFile`"" | Out-Null

    $batFile | Out-Null

    Get-Content $tempFile | Foreach-Object {   
        if ($_ -match "^(.*?)=(.*)$") { 
            Set-Content "env:\$($matches[1])" $matches[2] | Out-Null
        }	
    }
   
    Remove-Item $tempFile | Out-Null
}
function Configure-Msvs-Toolchain {
    Write-Output-Header "Configuring msvs toolchain"

    # https://github.com/Microsoft/vswhere/commit/a8c90e3218d6c4774f196d0400a8805038aa13b1 (Release mode / VS 2015 Update 3)
    # SHA512: 06FAE35E3A5B74A5B0971FB19EE0987E15E413558C620AB66FB3188F6BF1C790919E8B163596744D126B3716D0E91C65F7C1325F5752614078E6B63E7C81D681
    $wswhere = $CommandVsWhere
    $VxxCommonTools = $null
	
    $Ids = 'Community', 'Professional', 'Enterprise', 'BuildTools' | foreach { 'Microsoft.VisualStudio.Product.' + $_ }
    $Instance = & $wswhere -version 15 -products $ids -requires 'Microsoft.Component.MSBuild' -format json `
        | convertfrom-json `
        | select-object -first 1
						
    if ($null -eq $Instance) {
        Die "Visual Studio 2017 was not found"
    }
		
    $VXXCommonTools = Join-Path $Instance.installationPath VC\Auxiliary\Build
    $script:CommandMsBuild = Join-Path $Instance.installationPath MSBuild\15.0\Bin\msbuild.exe

    if ($null -eq $VXXCommonTools -or (-not (Test-Path($VXXCommonTools)))) {
        Die "PlatformToolset $PlatformToolset is not installed."
    }
    
    $script:VCVarsAll = Join-Path $VXXCommonTools vcvarsall.bat
    if (-not (Test-Path $VCVarsAll)) {
        Die "Unable to find $VCVarsAll"
    }
        
    Invoke-BatchFile $VXXCommonTools x64
	
    Write-Output "Successfully configured msvs"
}

# Build targets

function Build-Native {	
    $SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$TargetArch\$Configuration

    Write-Output-Header "Building native dependencies"

    $CmakeArguments = @(
        "-G`"$CmakeGenerator`"",
        "-H`"$SnapCoreRunSrcDir`"",
        "-B`"$SnapCoreRunBuildOutputDir`""
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
        Die "Cross compiling is not support on: $OSPlatform"
    }
			
    Write-Output "Build src directory: $SnapCoreRunSrcDir"
    Write-Output "Build output directory: $SnapCoreRunBuildOutputDir"
    Write-Output "Arch: $TargetArch"
    Write-Output ""
	
    Command-Exec $CommandCmake $CmakeArguments
    
    $CommandGTests = Join-Path $SnapCoreRunBuildOutputDir Snap.Tests
    $RunGTests = $true

    switch ($OSPlatform) {
        "Unix" {
            sh -c "(cd $SnapCoreRunBuildOutputDir && make -j $ProcessorCount)"

            if($Cross)
            {
                $RunGTests = $false
            }

            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun
                if ($Cross) {
                    $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun.exe
                }
                #Command-Exec $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }

        }
        "Windows" {
            Command-Exec $CommandCmake @(
                "--build `"$SnapCoreRunBuildOutputDir`" --config $Configuration"
            )
        
            $SnapTestsExePath = Join-Path $SnapCoreRunBuildOutputDir $Configuration\Snap.Tests.exe
            $CoreRunExePath = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\$Configuration
            Copy-Item $SnapTestsExePath $CoreRunExePath | Out-Null
            $CommandGTests = Join-Path $CoreRunExePath Snap.Tests.exe

            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\$Configuration\corerun.exe
                #Command-Exec $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }
        }
        default {
            Die "Unsupported os platform: $OSPlatform"
        }
    }

    if($RunGTests -eq $false)
    {
        return
    }

    Write-Output-Header "Running unit tests"

    if($false -eq (Test-Path $CommandGTests)) {
        Die "Unable to find test runner: $CommandGTests"
    }

    try {
        Push-Location $SnapCoreRunBuildOutputDir
        Command-Exec $CommandGTests $CommandGTestsDefaultArguments
    } finally {
        Pop-Location 
        if(0 -ne $LASTEXITCODE)
        {
            exit $LASTEXITCODE
        }
    }
}

function Build-Snap {
    Write-Output-Header "Building Snap"

    Requires-Snapx 

    Command-Exec $CommandDotnet @(
        "clean $SnapNetSrcDir"
    )

    Command-Exec $CommandDotnet @(
        ("build {0}" -f (Join-Path $SnapNetSrcDir Snap.csproj)),
        "/p:SnapNupkg=true"
        "--configuration $Configuration"
    )

    Command-Exec $CommandDotnet @(
        ("pack {0}" -f (Join-Path $SnapNetSrcDir Snap.csproj)),
        "--configuration $Configuration",
        "--no-build"
        "--no-dependencies"
    )

    # Clean is required because of ILRepack
    Command-Exec $CommandDotnet @(
       "clean $SnapNetSrcDir"
    )
}
function Build-Snap-Installer {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet("win-x64", "linux-x64")]
        [string] $Rid 
    )
    Write-Output-Header "Building Snap.Installer"

    Requires-Snapx 

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
            Die "Rid not supported: $Rid"
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

    Command-Exec $CommandDotnet @(
        "clean $SnapInstallerNetSrcDir"
    )

    Command-Exec $CommandDotnet @(
        ("publish {0}" -f (Join-Path $SnapInstallerNetSrcDir Snap.Installer.csproj)),
        "/p:ShowLinkerSizeComparison=true",
        "/p:CrossGenDuringPublish=$MonoLinkerCrossGenEnabled",
        # "--configuration $Configuration",
        "--runtime $Rid",
        "--framework $TargetArchDotNet",
        "--self-contained true",
        "--output $SnapInstallerNetBuildPublishDir"
    )

    if ($Rid -eq "win-x64") {
        Command-Exec $CommandSnapx @(
            "rcedit"
            "--gui-app" 
            "--filename $SnapInstallerExeAbsolutePath"
        )
    }

    if ($OSPlatform -ne "Windows") {
        Command-Exec chmod @("+x $SnapInstallerExeAbsolutePath")
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

if ($Cross) {
    Write-Output "Native target arch: $ArchCross"		
}
else {
    Write-Output "Native target arch: $Arch"				
}

switch ($OSPlatform) {
    "Windows" {
        Requires-Windows 			
        Requires-VsWhere 				
        Configure-Msvs-Toolchain
        Requires-Msbuild
    }
    "Unix" {
        Requires-Unix
        Requires-Make
    }
    default {
        Die "Unsupported os platform: $OSPlatform"
    }
}
			
Requires-Cmake
Requires-Upx
Requires-Packer
Requires-Dotnet

switch ($Target) {
    "Native" {
        switch ($OSPlatform) {
            "Windows" {		
                if ($Cross -eq $FALSE) {
                    Build-Native		
                }
                else {
                    Die "Cross compiling is not supported on Windows."
                } 
            }
            "Unix" {
                Build-Native		
            }
            default {
                Die "Unsupported os platform: $OSPlatform"
            }
        }
    }
    "Run-Native-Mingw-UnitTests-Windows" {
        switch($OSPlatform)
        {
            "Windows" {
                $SnapCoreRunMingwBuildOutputDir = Join-Path $WorkingDir build\native\Unix\x86_64-w64-mingw32-gcc\$Configuration
                $CommandGTests = Join-Path $SnapCoreRunMingwBuildOutputDir Snap.Tests.exe
                
                Write-Output-Header "Running mingw unit tests"

                if($false -eq (Test-Path $CommandGTests)) {
                    Die "Unable to find test runner: $CommandGTests"
                }

                try 
                {
                    Push-Location $SnapCoreRunMingwBuildOutputDir
                    Command-Exec $CommandGTests $CommandGTestsDefaultArguments		
                } finally {             
                    Pop-Location 
                    if(0 -ne $LASTEXITCODE)
                    {
                        exit $LASTEXITCODE
                    }
                }
                
            }
            default {
                Die "Unsupported os platform: $OSPlatform"
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
