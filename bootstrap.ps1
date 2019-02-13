param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Native", "Snap-Installer")]
    [string] $Target = "Native",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [boolean] $Cross = $FALSE,
    [Parameter(Position = 3, ValueFromPipeline = $true)]
    [boolean] $Lto = $FALSE
)

# Global functions

function Die {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Red
    Write-Host

    exit $LASTEXITCODE
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

# IO Variables

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
$ToolsDir = Join-Path $WorkingDir tools

# Global variables

$OSPlatform = $null
$OSVersion = [Environment]::OSVersion
$ProcessorCount = [Environment]::ProcessorCount
$Arch = $null
$ArchCross = $null
$PathSeperator = [IO.Path]::PathSeparator

$CmakeGenerator = $null
$CommandCmake = $null
$CommandGit = $null
$CommandDotnet = $null
$CommandMsBuild = $null
$CommandMake = $null
$CommandUpx = $null
$CommandPacker = $null
$CommandSnapx = $null

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CmakeGenerator = "Visual Studio 15 Win64"
        $CommandCmake = "cmake.exe"
        $CommandGit = "git.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandUpx = Join-Path $ToolsDir upx.exe
        $CommandPacker = Join-Path $ToolsDir warp-packer.exe
        $CommandSnapx = "snapx.exe"
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
        $CommandSnapx = "snapx.exe"
        $Arch = "x86_64-linux-gcc"
        $ArchCross = "x86_64-w64-mingw32-gcc"
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
$SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\native\$OSPlatform\$TargetArch\$Configuration

$SnapNetSrcDir = Join-Path $WorkingDir src\Snap
$SnapNetBuildOutputDir = Join-Path $SnapNetSrcDir bin\$TargetArchDotNet\$Configuration\publish

$SnapInstallerNetSrcDir = Join-Path $WorkingDir src\Snap.Installer
$SnapInstallerNetBuildOutputDir = Join-Path $SnapInstallerNetSrcDir bin\$TargetArchDotNet\$Configuration\publish

# Miscellaneous functions that require bootstrapped variable state

function Requires-Cmake {
    if ((Get-Command $CommandCmake -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find cmake executable in environment path: $CommandCmake"
    }
}

function Requires-Git {
    if ((Get-Command $CommandGit -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find git executable in environment path: $CommandGit"
    }
}

function Requires-Dotnet {
    if ((Get-Command $CommandDotnet -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find dotnet executable in environment path: $CommandDotnet"
    }
}

function Requires-Msbuild {
    if ((Get-Command $CommandMsBuild -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find msbuild executable in environment path: $CommandMsBuild"
    }
}

function Requires-Make {
    if ((Get-Command $CommandMake -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find make executable in environment path: $CommandMake"
    }
}

function Requires-Upx {
    if ((Get-Command $CommandUpx -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find upx executable in environment path: $CommandUpx"
    }
}

function Requires-Packer {
    if ((Get-Command $CommandPacker -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find packer executable in environment path: $CommandPacker"
    }
}
function Requires-Snapx {
    if ((Get-Command $CommandSnapx -ErrorAction SilentlyContinue) -eq $null) {
        Die "Unable to find snapx executable in environment path: $CommandSnapx"
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
        [Parameter(Position = 1, Mandatory = $true, ValueFromPipeline = $true)]
        [string[]] $Arguments
    )

    $CommandStr = "{0} {1}" -f $Command, ($Arguments -join " ")
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
    $wswhere = Join-Path $WorkingDir Tools\vswhere
    $VxxCommonTools = $null
	
    $Ids = 'Community', 'Professional', 'Enterprise', 'BuildTools' | foreach { 'Microsoft.VisualStudio.Product.' + $_ }
    $Instance = & $wswhere -version 15 -products $ids -requires 'Microsoft.Component.MSBuild' -format json `
        | convertfrom-json `
        | select-object -first 1
						
    if ($Instance -eq $null) {
        Die "Visual Studio 2017 was not found"
    }
		
    $VXXCommonTools = Join-Path $Instance.installationPath VC\Auxiliary\Build
    $script:CommandMsBuild = Join-Path $Instance.installationPath MSBuild\15.0\Bin\msbuild.exe

    if ($VXXCommonTools -eq $null -or (-not (Test-Path($VXXCommonTools)))) {
        Die "PlatformToolset $PlatformToolset is not installed."
    }
    
    $script:VCVarsAll = Join-Path $VXXCommonTools vcvarsall.bat
    if (-not (Test-Path $VCVarsAll)) {
        Die "Unable to find $VCVarsAll"
    }
        
    Invoke-BatchFile $VXXCommonTools x64
	
    Write-Output "Successfully configured msvs"
}

function Build-Native {	
    Write-Output-Header "Building native dependencies"

    $CmakeArguments = @(
        "-G`"$CmakeGenerator`"",
        "-H`"$SnapCoreRunSrcDir`"",
        "-B`"$SnapCoreRunBuildOutputDir`""
    )
	
    if ($Lto) {
        $CmakeArguments += "-DENABLE_LTO=1"
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
	
    switch ($OSPlatform) {
        "Unix" {
            sh -c "(cd $SnapCoreRunBuildOutputDir && make -j $ProcessorCount)"

            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun
                if ($Cross -eq $TRUE) {
                    $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun.exe
                }
                Command-Exec $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }

        }
        "Windows" {
            Command-Exec $CommandCmake @(
                "--build `"$SnapCoreRunBuildOutputDir`" --config $Configuration"
            )
        
            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\$Configuration\corerun.exe
                Command-Exec $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }
        }
        default {
            Die "Unsupported os platform: $OSPlatform"
        }
    }
			
}

function Build-Snap-Installer 
{
    Write-Output-Header "Building Snap.Installer"

    $Rid = $null
    $PackerArch = $null
    $SnapInstallerExeName = $null

    switch($OSPlatform)
    {
        "Windows"
        {
            $Rid = "win-x64"
            $PackerArch = "windows-x64"
            $SnapInstallerExeName = "Snap.Installer.exe"
        }
        "Unix"
        {
            $Rid = "linux-x64"
            $PackerArch = "linux-x64"
            $SnapInstallerExeName = "Snap.Installer"
        }
        default {
            Die "Platform not supported: $OSPlatform"
        }
    }

    Write-Output "Build src directory: $SnapInstallerNetSrcDir"
    Write-Output "Build output directory: $SnapInstallerNetBuildOutputDir"
    Write-Output "Arch: $TargetArchDotNet"
    Write-Output "Rid: $Rid"
    Write-Output "PackerArch: $PackerArch"
    Write-Output ""

    Command-Exec $CommandDotnet @(
        "clean $SnapInstallerNetSrcDir",
        "--configuration $Configuration"
    )

    Command-Exec $CommandDotnet @(
        ("publish {0}" -f (Join-Path $SnapInstallerNetSrcDir Snap.Installer.csproj)),
        "/p:ShowLinkerSizeComparison=true",
        "--configuration $Configuration",
        "--runtime $Rid",
        "--framework $TargetArchDotNet",
        "--self-contained"
    )

    if($OSPlatform -eq "Windows")
    {
        Command-Exec $CommandSnapx @(
            "rcedit"
            "--gui-app" 
            ("--filename {0}" -f (Join-Path $SnapInstallerNetBuildOutputDir Snap.Installer.exe))
        )
    }

    Command-Exec $CommandPacker @(
        "--arch $PackerArch"
        "--exec $SnapInstallerExeName"
        ("--output {0} " -f (Join-Path $SnapInstallerNetBuildOutputDir Setup.exe))
        "--input_dir $SnapInstallerNetBuildOutputDir"
    )

    Command-Exec $CommandUpx @(
        ("--ultra-brute {0}" -f (Join-Path $SnapInstallerNetBuildOutputDir Setup.exe))
    )
}

Write-Output-Header "----------------------- CONFIGURATION DETAILS ------------------------" 
Write-Output "OS: $OSVersion"
Write-Output "OS Platform: $OSPlatform"
Write-Output "Processor count: $ProcessorCount"
Write-Output "Build output directory: $BuildOutputDir"
Write-Output "Configuration: $Configuration"

if ($Cross) {
    Write-Output "Target arch: $ArchCross"		
}
else {
    Write-Output "Target arch: $Arch"				
}

switch ($OSPlatform) {
    "Windows" {
        Requires-Windows 							
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
Requires-Snapx 

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
    "Snap-Installer" {
        switch ($OSPlatform) {
            "Windows" {		
                Build-Snap-Installer
            }
            "Unix" {
                Build-Snap-Installer
            }
            default {
                Die "Unsupported os platform: $OSPlatform"
            }
        }
    }
}