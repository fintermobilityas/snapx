# Init

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$SnapxVersion = (dotnet gitversion /showVariable NugetVersionv2 | Out-String).Trim()

Write-Output-Colored "Init: Debug, Release. This might take a while! :)"

@("Debug", "Release")  | ForEach-Object {
    $Configuration = $_

    Invoke-Command-Colored pwsh @("build.ps1 -Version $SnapxVersion -Configuration $Configuration")
}

