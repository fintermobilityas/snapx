param()

function Resolve-Shell-Dependency {
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [string] $Command
    )
    if ($null -eq (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Write-Error "Unable to find executable in environment path: $Command"
    }
}
function Resolve-Windows {
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [string] $OSPlatform
    )
    if ($OSPlatform -ne "Windows") {
        Write-Error "Unable to continue because OS version is not Windows but $OSVersion"
    }	
}
function Resolve-Unix {
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [string] $OSPlatform
    )
    if ($OSPlatform -ne "Unix") {
        Write-Error "Unable to continue because OS version is not Unix but $OSVersion"
    }	
}
function Write-Output-Colored {
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [string] $Message,
        [Parameter(Position = 1, ValueFromPipeline = $true)]
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
    Write-Output-Colored -Message $Message -ForegroundColor Green
    Write-Host
}
function Write-Output-Header-Warn {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Output-Colored -Message $Message -ForegroundColor Yellow
    Write-Host
}
function Invoke-Command-Colored {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Filename,
        [Parameter(Position = 1, ValueFromPipeline = $true)]
        [string[]] $Arguments
    )
    begin {
        $CommandStr = $Filename
        $DashsesRepeatCount = $Filename.Length

        if($Arguments.Length -gt 1)
        {
            $ArgumentsStr = $Arguments -join " "
            $CommandStr = "$Filename $ArgumentsStr"
            $DashsesRepeatCount = $CommandStr.Length
        }

        if([console]::BufferWidth -gt 0)
        {
            $DashsesRepeatCount = [console]::BufferWidth
        }

        $DashesStr = "-" * $DashsesRepeatCount
    }   
    process {

        Write-Output-Colored -Message $DashesStr -ForegroundColor White
        Write-Output-Colored -Message $CommandStr -ForegroundColor Green
        Write-Output-Colored -Message $DashesStr -ForegroundColor White

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

        $global:LASTEXITCODE = $Process.ExitCode
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
function Use-Msvs-Toolchain {
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
        Write-Error "Visual Studio 2017 was not found"
    }
		
    $VXXCommonTools = Join-Path $Instance.installationPath VC\Auxiliary\Build
    $script:CommandMsBuild = Join-Path $Instance.installationPath MSBuild\15.0\Bin\msbuild.exe

    if ($null -eq $VXXCommonTools -or (-not (Test-Path($VXXCommonTools)))) {
        Write-Error "PlatformToolset $PlatformToolset is not installed."
    }
    
    $script:VCVarsAll = Join-Path $VXXCommonTools vcvarsall.bat
    if (-not (Test-Path $VCVarsAll)) {
        Write-Error "Unable to find $VCVarsAll"
    }
        
    Invoke-BatchFile $VXXCommonTools x64
	
    Write-Output "Successfully configured msvs"
}

# Build targets

function Invoke-Google-Tests 
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $GTestsDirectory,
        [Parameter(Position = 1, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $GTestsExe,
        [Parameter(Position = 2, Mandatory = $true, ValueFromPipeline = $true)]
        [string[]] $GTestsArguments 
    )
    try 
    {
        Push-Location $GTestsDirectory
        $GTestsExe = Join-Path $GTestsDirectory $GTestsExe
        $global:LASTEXITCODE = Invoke-Command-Colored $GTestsExe $GTestsArguments
    } finally {             
        Pop-Location 
    }
}
