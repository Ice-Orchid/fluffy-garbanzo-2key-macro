[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\Personal Keypad')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler is not installed.'
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName)
if ($sources.Count -eq 0) { throw 'No source files were found.' }
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$shared = @('/nologo', '/optimize+', '/platform:anycpu', '/warn:4', "/win32manifest:$manifest",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll')
foreach ($binary in @(
    @{ Name = 'PersonalKeypad.exe'; Kind = 'winexe' },
    @{ Name = 'KeypadControl.exe'; Kind = 'exe' }
)) {
    $output = Join-Path $OutputDirectory $binary.Name
    & $compiler @shared "/target:$($binary.Kind)" "/out:$output" @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $($binary.Name)." }
    Write-Output "Built $output"
}
Write-Output 'Existing data and settings were kept. No targets were opened and no device was programmed.'
