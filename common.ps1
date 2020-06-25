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
function Invoke-Exit
{
	param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Message
    )

    Write-Host
    Write-Host $Message -ForegroundColor Red
	Write-Host

	exit 1
}

function Invoke-Dotnet-Clear {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $SrcDirectory
    )

    $DirectoriesToRemove = @(".vs", "bin", "obj", "packages")

    Get-ChildItem -Directory $SrcDirectory -Recurse | ForEach-Object {
        $DirectoryName = Split-Path -Leaf $_

        if($DirectoriesToRemove.Contains($DirectoryName)) {
            Remove-Item $_ -Force -Recurse | Out-Null
        }
    }
}

function Invoke-Command-Colored {
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $Filename,
        [Parameter(Position = 1, ValueFromPipeline = $true)]
        [string[]] $Arguments,
		[Parameter(Position = 2, ValueFromPipeline = $true)]
        [Hashtable[]] $EnvironmentVariables,
		[Parameter(Position = 3, ValueFromPipeline = $true)]
        [switch] $StartProcess,
		[Parameter(Position = 4, ValueFromPipeline = $true)]
        [switch] $IgnoreExitCode
    )

	$CommandStr = $Filename
	$ArgumentsStr = $null

	if($Arguments.Length -gt 0)
	{
		$ArgumentsStr = $Arguments -join " "
		$CommandStr = "$Filename $ArgumentsStr"
	}

	$BufferWidth = 80

	try {
		$BufferWidthTmp = [console]::BufferWidth
		if($BufferWidthTmp -gt 0) {
			$BufferWidth = $BufferWidthTmp
		}
	} catch {
		# Ignore
	}

	$DashsesRepeatCount = $CommandStr.Length
	if($DashsesRepeatCount -gt $BufferWidth) {
		$DashsesRepeatCount = $BufferWidth
	}

	$DashesStr = "-" * $DashsesRepeatCount

	Write-Output-Colored -Message $DashesStr -ForegroundColor White
	Write-Output-Colored -Message $CommandStr -ForegroundColor Green
	Write-Output-Colored -Message $DashesStr -ForegroundColor White

	if($StartProcess -eq $false) {
		Invoke-Expression $CommandStr

		if($IgnoreExitCode -eq $false) {
			if($LASTEXITCODE -ne 0) {
				Invoke-Exit "Command returned a non-zero exit code"
			}
		}

		return
	}

	$StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $StartInfo.FileName = $Filename
    $StartInfo.Arguments = $Arguments

	$StartInfo.EnvironmentVariables.Clear()

	foreach($EnvironmentVariable in $EnvironmentVariables) {
		$Key = $EnvironmentVariable.Key
		$Value = $EnvironmentVariable.Value
        $StartInfo.EnvironmentVariables.Add($Key, $Value)
	}

    Get-ChildItem -Path env:* | ForEach-Object {
        $StartInfo.EnvironmentVariables.Add($_.Name, $_.Value)
    }

    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $false

    $Process = New-Object System.Diagnostics.Process
    $Process.StartInfo = $StartInfo
    $Process.Start() | Out-Null
    $Process.WaitForExit()

	if($IgnoreExitCode -eq $false) {
		if($Process.ExitCode -ne 0) {
			Invoke-Exit "Command returned a non-zero exit code"
		}
    }

}

function Invoke-Configure-Msvs-Toolchain
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $VisualStudioVersion,
        [Parameter(Position = 1, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $CommandVsWhere
    )

    Resolve-Shell-Dependency $CommandVsWhere

    $Path = & $CommandVsWhere -version $VisualStudioVersion -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

    if($Path) {
        $Path = Join-Path $Path Common7\Tools\VsDevCmd.bat

        if(-not (Test-Path $Path)) {
            Invoke-Exit "Unable to find: $Path"
        }

        cmd /s /c """$Path"" $args && set" | Where-Object { $_ -match '(\w+)=(.*)' } | ForEach-Object {
            $null = New-item -Force -Path "env:\$($Matches[1])" -Value $Matches[2]
        }

    } else {
        Invoke-Exit "Unable to find visual studio: $VisualStudioVersion"
    }
}

# Build targets

function Invoke-Google-Tests
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $GTestsDirectory,
        [Parameter(Position = 1, Mandatory = $true, ValueFromPipeline = $true)]
        [string] $GTestsExe,
        [Parameter(Position = 2, ValueFromPipeline = $true)]
        [string[]] $GTestsArguments
    )
    try
    {
        if($null -eq $GTestsArguments) {
            $GTestsArguments = @()
        }

        Push-Location $GTestsDirectory
        $GTestsExe = Join-Path $GTestsDirectory $GTestsExe
        $GTestsArguments += "--gtest_output=""xml:./googletestsummary.xml"""
        Invoke-Command-Colored $GTestsExe $GTestsArguments
    } finally {
        Pop-Location
    }
}
