$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    (Join-Path $framework 'WPF\UIAutomationClient.dll'),
    (Join-Path $framework 'WPF\UIAutomationTypes.dll'),
    (Join-Path $framework 'WPF\WindowsBase.dll'))
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    ('/out:' + (Join-Path $projectRoot 'TXT文件定位器.exe')),
    ('/win32icon:' + (Join-Path $projectRoot 'TXT文件定位器.ico')),
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')))
$compilerArgs += $references | ForEach-Object { '/reference:' + $_ }
$compilerArgs += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs').FullName
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Output 'Built TXT文件定位器.exe'
