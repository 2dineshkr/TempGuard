[CmdletBinding()]
param(
    [string]$OutputDirectory = 'build\TempGuard-win-x64',
    [string]$Version = '1.0.0',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $projectRoot 'packages'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nuget = Join-Path $env:TEMP 'nuget-6.11.1.exe'

$projectRootWithSeparator = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
if (-not $outputRoot.StartsWith($projectRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside the project directory.'
}
if ($outputRoot -eq [IO.Path]::GetFullPath($projectRoot)) {
    throw 'OutputDirectory cannot be the project root.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$') {
    throw 'Version must look like 1.0.0 or 1.0.0-preview.1.'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x compiler was not found.'
}

if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'LibreHardwareMonitorLib.0.9.6'))) {
    if (-not (Test-Path -LiteralPath $nuget)) {
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/v6.11.1/nuget.exe' -OutFile $nuget
    }
    & $nuget install LibreHardwareMonitorLib -Version 0.9.6 -OutputDirectory $packageRoot `
        -Framework net472 -NonInteractive -DirectDownload -Source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw 'NuGet dependency restore failed.' }
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$lhmReference = Join-Path $packageRoot 'LibreHardwareMonitorLib.0.9.6\ref\net472\LibreHardwareMonitorLib.dll'
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName

& $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug- /langversion:5 `
    "/out:$outputRoot\TempGuard.exe" "/win32manifest:$projectRoot\app.manifest" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Management.dll `
    "/reference:$lhmReference" $sourceFiles

if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

$runtimeFiles = @(
    @{ Source = 'LibreHardwareMonitorLib.0.9.6\runtimes\win-x64\lib\net472\LibreHardwareMonitorLib.dll'; Name = 'LibreHardwareMonitorLib.dll' },
    @{ Source = 'DiskInfoToolkit.1.1.2\lib\net472\DiskInfoToolkit.dll'; Name = 'DiskInfoToolkit.dll' },
    @{ Source = 'RAMSPDToolkit-NDD.1.4.2\lib\net472\RAMSPDToolkit-NDD.dll'; Name = 'RAMSPDToolkit-NDD.dll' },
    @{ Source = 'BlackSharp.Core.1.0.7\lib\net472\BlackSharp.Core.dll'; Name = 'BlackSharp.Core.dll' },
    @{ Source = 'HidSharp.2.6.4\lib\net35\HidSharp.dll'; Name = 'HidSharp.dll' },
    @{ Source = 'System.Buffers.4.6.1\lib\net462\System.Buffers.dll'; Name = 'System.Buffers.dll' },
    @{ Source = 'System.Memory.4.6.3\lib\net462\System.Memory.dll'; Name = 'System.Memory.dll' },
    @{ Source = 'System.Numerics.Vectors.4.6.1\lib\net462\System.Numerics.Vectors.dll'; Name = 'System.Numerics.Vectors.dll' },
    @{ Source = 'System.Runtime.CompilerServices.Unsafe.6.1.2\lib\net462\System.Runtime.CompilerServices.Unsafe.dll'; Name = 'System.Runtime.CompilerServices.Unsafe.dll' },
    @{ Source = 'System.Security.AccessControl.6.0.0\lib\net461\System.Security.AccessControl.dll'; Name = 'System.Security.AccessControl.dll' },
    @{ Source = 'System.Security.Principal.Windows.5.0.0\lib\net461\System.Security.Principal.Windows.dll'; Name = 'System.Security.Principal.Windows.dll' },
    @{ Source = 'System.Threading.AccessControl.10.0.3\lib\net462\System.Threading.AccessControl.dll'; Name = 'System.Threading.AccessControl.dll' }
)

foreach ($file in $runtimeFiles) {
    $source = Join-Path $packageRoot $file.Source
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing restored dependency: $($file.Source)" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot $file.Name) -Force
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputRoot 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'SECURITY.md') -Destination (Join-Path $outputRoot 'SECURITY.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $outputRoot -Force

if (-not $SkipArchive) {
    New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
    $archive = Join-Path $artifactsRoot "TempGuard-$Version-win-x64.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $outputRoot '*') -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    Set-Content -LiteralPath ($archive + '.sha256') -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding ascii
    Write-Host "Package: $archive" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor Green
}

Write-Host "Built: $outputRoot\TempGuard.exe" -ForegroundColor Green
