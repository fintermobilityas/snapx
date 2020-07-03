param(
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true)]
    [switch] $WithCIBuild,
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true)]
    [switch] $WithRunDotNetTests
)

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$SrcDirectory = Join-Path $WorkingDir src
$SnapxVersion = (dotnet gitversion /showVariable NugetVersionv2 | Out-String).Trim()

Write-Output-Colored "Init: Debug, Release. This might take a while! :)"

$Configurations = @("Debug", "Release")

$Configurations  | ForEach-Object {
    $Configuration = $_

    Invoke-Command-Colored pwsh @("build.ps1 -Target Bootstrap -Version $SnapxVersion -Configuration $Configuration -CIBuild:$WithCIBuild")
}

if($WithRunDotNetTests) {
    $Configurations  | ForEach-Object {
        $Configuration = $_

        Invoke-Command-Colored pwsh @("build.ps1 -Target Run-Dotnet-UnitTests -Version $SnapxVersion -Configuration $Configuration -CIBuild:$WithCIBuild")
    }
}

Invoke-Dotnet-Clear $SrcDirectory

