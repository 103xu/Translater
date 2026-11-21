param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$binary = Join-Path $output 'Translater.exe'
$names = 'Program.cs', 'Settings.cs', 'Popup.cs', 'Capture.cs', 'DeepSeek.cs', 'Credentials.cs', 'AppFonts.cs', 'StatusHint.cs'
$sources = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
$options = @('/nologo', '/target:winexe', '/platform:x64', '/langversion:5', '/optimize+', '/codepage:65001',
    "/out:$binary", "/win32icon:$PSScriptRoot\Translater.ico", "/win32manifest:$PSScriptRoot\app.manifest")
$references = 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll',
    'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll'
$options += $references | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler @options @sources
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'assets/fonts') -Destination $output -Recurse -Force
Write-Output $binary
