[CmdletBinding()]
param([switch]$IncludeUi)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is not installed.' }
$outputDirectory = Join-Path $PSScriptRoot 'build\tests'
[void](New-Item -ItemType Directory -Path $outputDirectory -Force)
$suites = @(
    @{ Name = 'ProtocolTests'; Files = @('src\KeypadProtocol.cs', 'tests\ProtocolTests.cs') },
    @{ Name = 'HardwareServiceTests'; Files = @('src\KeypadProtocol.cs','src\WindowsHid.cs','src\HardwareService.cs','tests\HardwareServiceTests.cs') },
    @{ Name = 'LauncherTests'; Files = @('src\Launcher.cs', 'tests\LauncherTests.cs') }
)
if ($IncludeUi) {
    $suites += @{ Name = 'MainFormUiTests'; Files = @('src\Launcher.cs', 'src\MainForm.cs', 'tests\MainFormUiTests.cs') }
}
$failedSuites = @()
foreach ($suite in $suites) {
    $output = Join-Path $outputDirectory ($suite.Name + '.exe')
    $sources = @($suite.Files | ForEach-Object { Join-Path $PSScriptRoot $_ })
    & $compiler /nologo /target:exe /warn:4 "/out:$output" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $($suite.Name)." }
    & $output
    if ($LASTEXITCODE -ne 0) { $failedSuites += $suite.Name }
}
if ($failedSuites.Count -gt 0) { throw "Failed test suites: $($failedSuites -join ', ')" }
Write-Output 'All selected test suites passed. These tests do not program hardware or open assigned targets.'
