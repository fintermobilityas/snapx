param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    $Bootstrap = $false
)

function Write-Output-Colored {
    param(
        $ForegroundColor
    )
	
    $fc = $host.UI.RawUI.ForegroundColor

    $host.UI.RawUI.ForegroundColor = $ForegroundColor

    if ($args) {
        Write-Output $args
    }
    else {
        $input | Write-Output
    }

    $host.UI.RawUI.ForegroundColor = $fc
}

function Exec {
    param(
        [string] $Command,
        [boolean] $AllowFail = $false
    )

    $Dashses = "-" * $Command.Length
	
    Write-Output $Dashses
    Write-Output-Colored Green $Command
    Write-Output $Dashses
		
    Invoke-Expression $Command
	
    if ($false -eq $AllowFail -and $LASTEXITCODE -ne 0) {
        Write-Error "Command failed: $Command"
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

$Properties = @(
    ("/p:SnapBootstrap={0}" -f (Convert-Boolean-MSBuild $Bootstrap)) 
) -join " "

Exec "& dotnet tool uninstall -g snapx" -AllowFail $true
Exec "& dotnet clean src/Snapx"
Exec "& dotnet build -c Release src/Snapx -f netcoreapp2.2 $Properties"
Exec "& dotnet pack -c Release src/Snapx --no-build"
Exec "& dotnet tool install --global --add-source ./nupkgs snapx"

. snapx 
