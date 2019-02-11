param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [Parameter(Position = 1, ValueFromPipeline = $true)]
    [boolean] $Cross = $FALSE,
    [Parameter(Position = 2, ValueFromPipeline = $true)]
    [boolean] $Lto = $FALSE
)

# Global stateless functions

function Out-FileUtf8NoBom {

    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)] [string] $LiteralPath,
        [switch] $Append,
        [switch] $NoClobber,
        [AllowNull()] [int] $Width,
        [Parameter(ValueFromPipeline)] $InputObject
    )

    #requires -version 3

    # Make sure that the .NET framework sees the same working dir. as PS
    # and resolve the input path to a full path.
    [System.IO.Directory]::SetCurrentDirectory($PWD) # Caveat: .NET Core doesn't support [Environment]::CurrentDirectory
    $LiteralPath = [IO.Path]::GetFullPath($LiteralPath)

    # If -NoClobber was specified, throw an exception if the target file already
    # exists.
    if ($NoClobber -and (Test-Path $LiteralPath)) {
        Throw [IO.IOException] "The file '$LiteralPath' already exists."
    }

    # Create a StreamWriter object.
    # Note that we take advantage of the fact that the StreamWriter class by default:
    # - uses UTF-8 encoding
    # - without a BOM.
    $sw = New-Object IO.StreamWriter $LiteralPath, $Append

    $htOutStringArgs = @{}
    if ($Width) {
        $htOutStringArgs += @{ Width = $Width }
    }

    # Note: By not using begin / process / end blocks, we're effectively running
    #       in the end block, which means that all pipeline input has already
    #       been collected in automatic variable $Input.
    #       We must use this approach, because using | Out-String individually
    #       in each iteration of a process block would format each input object
    #       with an indvidual header.
    try {
        $Input | Out-String -Stream @htOutStringArgs | % { $sw.WriteLine($_) }
    }
    finally {
        $sw.Dispose()
    }

}

function Die {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Red
    Write-Host
    
    Write-Error $Message
    exit 0	
}

function Write-Output-Header {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Green
    Write-Host
}

function Write-Output-Header-Warn {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Yellow
    Write-Host
}

# IO Variables

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
$ToolsDir = Join-Path $WorkingDir tools

# Global variables

$DebugPrefix = ""
if ($Configuration -eq "Debug") {
    $DebugPrefix = "d"
}

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

switch -regex ($OSVersion) {
    "^Microsoft Windows" {
        $OSPlatform = "Windows"
        $CmakeGenerator = "Visual Studio 15 Win64"
        $CommandCmake = "cmake.exe"
        $CommandGit = "git.exe"
        $CommandDotnet = "dotnet.exe"
        $CommandUpx = Join-Path $ToolsDir upx.exe
        $Arch = "win7-x64"
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
        $Arch = "x86_64-linux-gcc"
        $ArchCross = "x86_64-w64-mingw32-gcc"
    }	
    default {
        Die "Unsupported os: $OSVersion"
    }
}

$TargetArch = $Arch
if ($Cross) {
    $TargetArch = $ArchCross
}

# Projects

$SnapCoreRunSrcDir = Join-Path $WorkingDir src
$SnapCoreRunBuildOutputDir = Join-Path $WorkingDir build\$OSPlatform\$TargetArch\$Configuration
$SnapCoreRunInstallDir = Join-Path $SnapCoreRunBuildOutputDir install

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

function Start-Process {
    param(
        [string] $Filename,
        [string[]] $Arguments
    )
    
    $StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $StartInfo.FileName = $Filename
    $StartInfo.Arguments = $Arguments

    $StartInfo.EnvironmentVariables.Clear()

    Get-ChildItem -Path env:* | ForEach-Object {
        $StartInfo.EnvironmentVariables.Add($_.Name, $_.Value)
    }

    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $false

    $Process = New-Object System.Diagnostics.Process
    $Process.StartInfo = $startInfo
    $Process.Start() | Out-Null
    $Process.WaitForExit()

    if ($Process.ExitCode -ne 0) {
        Die ("{0} returned a non-zero exit code" -f $Filename)
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

function Build-SnapCoreRun {	
    Write-Output-Header "Building Snap.CoreRun"

    $CmakeArguments = @(
        "-G`"$CmakeGenerator`"",
        "-H`"$SnapCoreRunSrcDir`"",
        "-B`"$SnapCoreRunBuildOutputDir`""
    )
	
    if ($Lto) {
        $CmakeArguments += "-DENABLE_LTO=1"
    }

    if($Configuration -eq "Debug")
    {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Debug"
    } else {
        $CmakeArguments += "-DCMAKE_BUILD_TYPE=Release"
    }

    if ($Cross -eq $TRUE -and $OsPlatform -eq "Unix") {
        $CmakeToolChainFile = Join-Path $WorkingDir cmake\Toolchain-x86_64-w64-mingw32.cmake
        $CmakeArguments += "-DCMAKE_TOOLCHAIN_FILE=$CmakeToolChainFile"
    }
    elseif ($Cross -eq $TRUE) {
        Die "Cross compiling is not support on: $OSPlatform"
    }
			
    Write-Output "Build src directory: $SnapCoreRunSrcDir"
    Write-Output "Build output directory: $SnapCoreRunBuildOutputDir"
    Write-Output "Build install directory: $SnapCoreRunInstallDir"
    Write-Output "Arch: $TargetArch"
    Write-Output ""
	
    Write-Output $CmakeArguments
	
    Start-Process $CommandCmake $CmakeArguments
	
    switch ($OSPlatform) {
        "Unix" {
            sh -c "(cd $SnapCoreRunBuildOutputDir && make -j $ProcessorCount)"

            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun
                if ($Cross -eq $TRUE) {
                    $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\corerun.exe
                }
                # Start-Process $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }

        }
        "Windows" {
            Start-Process $CommandCmake @(
                "--build `"$SnapCoreRunBuildOutputDir`" --config $Configuration"
            )
        
            if ($Configuration -eq "Release") {
                $SnapCoreRunBinary = Join-Path $SnapCoreRunBuildOutputDir Snap.CoreRun\$Configuration\corerun.exe
                # Start-Process $CommandUpx @("--ultra-brute $SnapCoreRunBinary")
            }
        }
        default {
            Die "Unsupported os platform: $OSPlatform"
        }
    }
			
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

switch ($OSPlatform) {
    "Windows" {		
        if ($Cross -eq $FALSE) {
            Build-SnapCoreRun		
        }
        else {
            Die "Cross compiling is not supported on Windows."
        } 
    }
    "Unix" {
        Build-SnapCoreRun		
    }
}
