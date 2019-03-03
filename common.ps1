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
    $CommandStr = $Filename
    $DashsesRepeatCount = $Filename.Length

    if($Arguments.Length -gt 0)
    {
        $ArgumentsStr = $Arguments -join " "
        $CommandStr = "$Filename $ArgumentsStr"
        $DashsesRepeatCount = $CommandStr.Length
    }

    try {
        # NB! Accessing this property during a CI build will throw.
        # Ref issue: https://github.com/Microsoft/azure-pipelines-tasks/issues/9719
        if([console]::BufferWidth -gt 0)
        {
            $DashsesRepeatCount = [console]::BufferWidth
        }    
    } catch {
        $DashsesRepeatCount = 80
    }

    $DashesStr = "-" * $DashsesRepeatCount

    Write-Output-Colored -Message $DashesStr -ForegroundColor White
    Write-Output-Colored -Message $CommandStr -ForegroundColor Green
    Write-Output-Colored -Message $DashesStr -ForegroundColor White

    Invoke-Expression $CommandStr
}
function Get-Msvs-Toolchain-Instance
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet(15, 16)]
        [int] $VisualStudioVersion
    )

    $Ids = 'Community', 'Professional', 'Enterprise', 'BuildTools' | ForEach-Object { 'Microsoft.VisualStudio.Product.' + $_ }
    $Instance = & $CommandVsWhere -version $VisualStudioVersion $PreRelease -products $ids -requires 'Microsoft.Component.MSBuild' -format json `
        | convertfrom-json `
        | select-object -first 1

    return $Instance
}
function Use-Msvs-Toolchain 
{
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet(15, 16)]
        [int] $VisualStudioVersion
    )

    Write-Output-Header "Configuring msvs toolchain"

    $script:CommandMsBuild = $null

    $Instance = Get-Msvs-Toolchain-Instance $VisualStudioVersion
    if ($null -eq $Instance) {
        if($VisualStudioVersion -eq 16)
        {
            Write-Error "Visual Studio 2019 was not found"
            exit 1
        } elseif($VisualStudioVersion -eq 15)
        {
            Write-Error "Visual Studio 2017 was not found"
            exit 1
        }
    } else {
        if($VisualStudioVersion -eq 16)
        {
            Write-Output "Found Visual Studio 2019"
        } elseif($VisualStudioVersion -eq 15) {
            Write-Output "Found Visual Studio 2017"
        } else {
            Write-Error "Unknown Visual Studio version: $VisualStudioVersion"
            exit 1
        }
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
        [Parameter(Position = 2, Mandatory = $true, ValueFromPipeline = $true)]
        [string[]] $GTestsArguments 
    )
    try 
    {
        Push-Location $GTestsDirectory
        $GTestsExe = Join-Path $GTestsDirectory $GTestsExe
        $GTestsArguments += "--gtest_output=""xml:./testsummary.xml"""        
        Invoke-Command-Colored $GTestsExe $GTestsArguments
    } finally {             
        Pop-Location 
    }
}

function Convert-Boolean-MSBuild {
    param(
        [boolean] $Value
    )
    
    if ($true -eq $Value) {
        return "true"
    }
    
    return "false"
}