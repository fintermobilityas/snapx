function Write-Output-Colored
{
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

function Exec
{
	param(
		[string] $Command
	)

	$Dashses = "-" * $Command.Length
	
	Write-Output $Dashses
	Write-Output-Colored Green $Command
	Write-Output $Dashses
		
	Invoke-Expression $Command
	
	if($LASTEXITCODE -ne 0)
	{
		Write-Error "Command failed: $Command"
	}
}

Exec "& dotnet tool uninstall -g snapx"
Exec "& dotnet clean src/Snapx"
Exec "& dotnet build -c Release src/Snapx -f netcoreapp2.2"
Exec "& dotnet pack -c Release src/Snapx --no-build"
Exec "& dotnet tool install --global --add-source ./nupkgs snapx"

. snapx 
