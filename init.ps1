param(
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = $true)]
    [switch] $WithCIBuild,
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = $true)]
    [switch] $WithRunDotNetTests,
    [Parameter(Position = 2, ValueFromPipelineByPropertyName = $true)]
    [switch] $WithDebugOnly,
    [Parameter(Position = 3, ValueFromPipelineByPropertyName = $true)]
    [switch] $DockerLocal
)

$WorkingDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. $WorkingDir\common.ps1

$SrcDirectory = Join-Path $WorkingDir src
$SnapxVersion = (dotnet gitversion /showVariable NugetVersionv2 | Out-String).Trim()

$Configurations = @("Debug")

if($WithDebugOnly -eq $false) {
    $Configurations += "Release"
}

$ConfigurationsStr = $Configurations -join ", "
Write-Output-Colored "Init: $ConfigurationsStr. This might take a while! :)"

$Configurations  | ForEach-Object {
    $Configuration = $_

    Invoke-Command-Colored pwsh @("build.ps1 -Target Bootstrap -Version $SnapxVersion -Configuration $Configuration -CIBuild:$WithCIBuild -DockerLocal:$DockerLocal")
}

if($WithRunDotNetTests) {
    $Configurations  | ForEach-Object {
        $Configuration = $_

        Invoke-Command-Colored pwsh @("build.ps1 -Target Run-Dotnet-UnitTests -Version $SnapxVersion -Configuration $Configuration -CIBuild:$WithCIBuild -DockerLocal:$DockerLocal")
    }
}

Invoke-Dotnet-Clear $SrcDirectory

