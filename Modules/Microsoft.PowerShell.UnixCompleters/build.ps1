# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#requires -Version 6.0

[CmdletBinding(DefaultParameterSetName = 'Build')]
param(
    [Parameter(ParameterSetName = 'Build')]
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [Parameter(ParameterSetName = 'Build')]
    [ValidateSet('net5.0', 'netstandard2.0')]
    [string] $Framework = 'netstandard2.0',

    [Parameter(ParameterSetName = 'Test')]
    [switch] $Build,

    [Parameter(ParameterSetName = 'Test')]
    [switch] $Test
)

$ErrorActionPreference = 'Stop'

$ModuleName = 'Microsoft.PowerShell.UnixCompleters'
$OutDir = "$PSScriptRoot/out"
$OutModuleDir = "$OutDir/$ModuleName"
$SrcDir = "$PSScriptRoot/$ModuleName"
$ZshCompleterScriptLocation = "$OutModuleDir/zcomplete.sh"

$script:Artifacts = @{
    "$ModuleName.psd1" = "$ModuleName.psd1"
    "OnStart.ps1" = "OnStart.ps1"
    "$ModuleName/bin/$Configuration/$Framework/$ModuleName.dll" = "$ModuleName.dll"
    "$ModuleName/bin/$Configuration/$Framework/$ModuleName.pdb" = "$ModuleName.pdb"
    "../../LICENSE" = "LICENSE.txt"
}

function Exec([scriptblock]$sb, [switch]$IgnoreExitcode)
{
    & $sb
    # note, if $sb doesn't have a native invocation, $LASTEXITCODE will
    # point to the obsolete value
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreExitcode)
    {
	    throw "Execution of {$sb} failed with exit code $LASTEXITCODE"
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Build' -or $Build)
{
    try
    {
        $null = Get-Command dotnet -ErrorAction Stop
    }
    catch
    {
        throw 'Unable to find dotnet executable'
    }

    foreach ($path in $OutDir,"${script:SrcDir}/bin","${script:SrcDir}/obj")
    {
        if (Test-Path -Path $path)
        {
            Remove-Item -Force -Recurse -Path $path -ErrorAction Stop
        }
    }

    try
    {
        Push-Location $SrcDir
        Exec { dotnet build -f $Framework -c $Configuration }
    }
    finally
    {
        Pop-Location
    }

    New-Item -ItemType Directory -Path $OutModuleDir -ErrorAction SilentlyContinue

    foreach ($artifactEntry in $script:Artifacts.GetEnumerator())
    {
        Copy-Item -Path (Join-Path $PSScriptRoot $artifactEntry.Key) -Destination (Join-Path $OutModuleDir $artifactEntry.Value) -ErrorAction Stop
    }

    # We need the zsh completer script to drive zsh completions
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/Valodim/zsh-capture-completion/master/capture.zsh' -OutFile $ZshCompleterScriptLocation
}

if ($Test)
{
    $pwsh = (Get-Process -Id $PID).Path
    $testPath = "$PSScriptRoot/tests"
    & $pwsh -c "Invoke-Pester '$testPath'"
}
