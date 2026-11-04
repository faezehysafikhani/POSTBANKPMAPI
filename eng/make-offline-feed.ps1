<#
Builds ./offline-feed: the NexusCore packages plus every third-party package the host needs, so the
host can be restored with no internet. Run once on a machine that can reach nuget.org, then copy the
folder (with SHA256SUMS.txt) to the offline network.

  pwsh ./eng/make-offline-feed.ps1 -CorePackages <folder-with-NexusCore-nupkg-files> [-Output offline-feed]
#>
param(
    [Parameter(Mandatory = $true)][string]$CorePackages,
    [string]$Output = "offline-feed"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "PostBank.Api/PostBank.Api.csproj"
$core = (Resolve-Path $CorePackages).Path
$out = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("postbankpmapi-feed-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    if (-not (Get-ChildItem -Path $core -Filter "NexusCore.Application.*.nupkg" -File -ErrorAction SilentlyContinue)) {
        throw "No NexusCore packages in $core"
    }

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
  <add key="core" value="$core" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
"@ | Set-Content -Path (Join-Path $work "online.config") -Encoding UTF8

    Write-Host "1/3 Restoring the host (Core packages + nuget.org) into a scratch package cache..."
    dotnet restore $project --packages (Join-Path $work "cache") --configfile (Join-Path $work "online.config")
    if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

    Write-Host "2/3 Collecting every package the restore needed into $out ..."
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Get-ChildItem -Path (Join-Path $work "cache") -Recurse -Filter "*.nupkg" -File |
        ForEach-Object { Copy-Item $_.FullName -Destination $out }
    Get-ChildItem -Path $out -Filter "*.nupkg" -File | Sort-Object Name |
        ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } |
        Set-Content -Path (Join-Path $out "SHA256SUMS.txt") -Encoding ASCII

    Write-Host "3/3 Verifying: restoring again from the offline feed only..."
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="offline-feed" value="$out" /></packageSources></configuration>
"@ | Set-Content -Path (Join-Path $work "offline.config") -Encoding UTF8
    dotnet restore $project --packages (Join-Path $work "verify") --configfile (Join-Path $work "offline.config") --force --no-cache
    if ($LASTEXITCODE -ne 0) { throw "Offline verification failed: the feed is incomplete" }

    $count = (Get-ChildItem -Path $out -Filter "*.nupkg" -File).Count
    Write-Host "Done: $count packages in $out"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
