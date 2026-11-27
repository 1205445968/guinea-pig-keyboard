param([string]$Executable)
$ErrorActionPreference = 'Stop'
if (-not $Executable) { $Executable = Join-Path $PSScriptRoot '../dist/WheekKeys.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Run build.ps1 before testing drawn prone movement.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDir 'csc.exe'))) { $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('wheek-drawn-prone-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$testExe = Join-Path $scratch 'DrawnProneSmokeTest.exe'
try {
    $arguments = @('/nologo', '/codepage:65001', "/out:$testExe", '/reference:System.Core.dll', '/reference:System.Xaml.dll')
    foreach ($assembly in 'WindowsBase', 'PresentationCore', 'PresentationFramework') { $arguments += '/reference:' + (Join-Path $frameworkDir "WPF/$assembly.dll") }
    & (Join-Path $frameworkDir 'csc.exe') @arguments (Join-Path $PSScriptRoot 'DrawnProneSmokeTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Drawn prone smoke test compilation failed.' }
    & $testExe $Executable
    if ($LASTEXITCODE -ne 0) { throw 'Drawn prone smoke test failed.' }
} finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
    if (@(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0) { Remove-Item -LiteralPath $scratch }
}
