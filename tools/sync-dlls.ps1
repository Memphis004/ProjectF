#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Copies the netstandard2.1 ProjectF DLLs (and their transitive dependencies)
    into Unity at UnityProject/Assets/Plugins/ProjectF.

.DESCRIPTION
    1. Publishes ProjectF.Tables / ProjectF.Lib / ProjectF.Shared for
       netstandard2.1 (knowledge.md rule 4 — Unity cannot consume NuGet).
    2. Copies the Libplanet.Net dependency stack (embedded node) from the
       local NuGet cache — those are NOT dependencies of the three
       netstandard2.1 projects, so they never appear in publish output.
    3. Skips every assembly Unity already provides (facade shims) and every
       assembly NuGetForUnity provides inside the project (Assets/Plugins/NuGet,
       Assets/packages) — duplicate assembly identities make Unity refuse to
       load Assembly-CSharp (observed with System.Text.Json 6.x vs 8.x).

    Usage (from the repository root):
        pwsh tools/sync-dlls.ps1              # Release build
        pwsh tools/sync-dlls.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$unityPluginDir = Join-Path $repoRoot 'UnityProject/Assets/Plugins/ProjectF'

$netstandardProjects = @(
    'src/ProjectF.Tables/ProjectF.Tables.csproj',
    'src/ProjectF.Lib/ProjectF.Lib.csproj',
    'src/ProjectF.Shared/ProjectF.Shared.csproj'
)

$projectAssemblies = @('ProjectF.Tables.dll', 'ProjectF.Lib.dll', 'ProjectF.Shared.dll')

# Everything the netstandard2.1 publish output may contain (allowlist).
$thirdPartyAssemblies = @(
    'Bencodex.dll',
    'Bencodex.Json.dll',
    'Libplanet.Action.dll',
    'Libplanet.Common.dll',
    'Libplanet.Crypto.dll',
    'Libplanet.Store.dll',
    'Libplanet.Types.dll',
    'Libplanet.dll',
    'BouncyCastle.Cryptography.dll',
    'MessagePack.dll',
    'MessagePack.Annotations.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'Microsoft.Bcl.HashCode.dll',
    'Microsoft.Extensions.Primitives.dll',
    'System.Collections.Immutable.dll',
    'System.IO.Pipelines.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Text.Json.dll',
    'System.Threading.Channels.dll',
    'Nito.AsyncEx.dll',
    'Nito.AsyncEx.Coordination.dll',
    'Nito.AsyncEx.Context.dll',
    'Nito.AsyncEx.Tasks.dll',
    'Nito.CancellationStructs.dll',
    'Nito.Disposables.dll',
    'Norgerman.Cryptography.Scrypt.dll',
    'System.Linq.Async.dll',
    'Destructurama.Attributed.dll',
    'Serilog.dll',
    'System.Diagnostics.DiagnosticSource.dll',
    'AsyncEnumerator.dll',
    # --- embedded node (Libplanet.Net) + persistent store extras ---
    'Libplanet.Net.dll',
    'Libplanet.Stun.dll',
    'Planetarium.NetMQ.dll',
    'AsyncIO.dll',
    'LiteDB.dll',
    'LruCacheNet.dll',
    'BitFaster.Caching.dll',
    'Caching.dll',
    'ImmutableTrie.dll',
    'ListSortHelper.dll',
    'Validation.dll',
    'Zio.dll',
    'System.Text.Encodings.Web.dll',
    'System.Threading.Tasks.Extensions.dll',
    'Nito.AsyncEx.Interop.WaitHandles.dll',
    'Nito.AsyncEx.Oop.dll',
    'Nito.Cancellation.dll',
    'Nito.Collections.Deque.dll',
    'System.Buffers.dll',
    'Grpc.Core.Api.dll',
    'MagicOnion.Abstractions.dll',
    'NetMQ.dll',
    'Microsoft.Extensions.Caching.Memory.dll',
    'AsyncEnumerable.dll',
    'Destructurama.Attributed.dll',
    'NaCl.dll'
)

# Unity/Mono ships its own copies — loading ours would conflict.
$unityProvided = @(
    'mscorlib.dll', 'netstandard.dll', 'System.dll', 'System.Core.dll',
    'System.Runtime.dll', 'System.Collections.dll', 'System.Collections.Concurrent.dll',
    'System.Collections.NonGeneric.dll', 'System.IO.dll', 'System.IO.Compression.ZipFile.dll',
    'System.Linq.dll', 'System.Linq.Expressions.dll', 'System.Reflection.dll',
    'System.Reflection.Emit.dll', 'System.Reflection.Emit.ILGeneration.dll',
    'System.Reflection.Emit.Lightweight.dll', 'System.Reflection.TypeExtensions.dll',
    'System.Runtime.InteropServices.RuntimeInformation.dll', 'System.Runtime.Numerics.dll',
    'System.AppContext.dll', 'System.ObjectModel.dll', 'System.Text.RegularExpressions.dll',
    'System.Threading.dll', 'System.Threading.Tasks.dll', 'System.Net.WebHeaderCollection.dll',
    'System.Security.Cryptography.Algorithms.dll', 'System.Security.Cryptography.Encoding.dll',
    'System.Security.Cryptography.Primitives.dll', 'System.Security.Cryptography.X509Certificates.dll',
    'System.Security.Cryptography.OpenSsl.dll', 'System.Text.Encoding.CodePages.dll',
    'System.Xml.ReaderWriter.dll', 'System.Xml.XDocument.dll', 'Microsoft.CSharp.dll',
    'System.IO.FileSystem.Primitives.dll', 'System.Net.Http.dll', 'System.Net.Sockets.dll',
    'System.Net.Primitives.dll', 'System.Net.NameResolution.dll'
)

# Assemblies NuGetForUnity already restores INSIDE the Unity project.
$nugetProvided = @{}
foreach ($src in @(
        (Join-Path $repoRoot 'UnityProject/Assets/Plugins/NuGet'),
        (Join-Path $repoRoot 'UnityProject/Assets/packages'))) {
    if (Test-Path $src) {
        foreach ($item in Get-ChildItem $src -Recurse -Filter '*.dll' -File) {
            $nugetProvided[$item.BaseName] = $true
        }
        foreach ($dir in Get-ChildItem $src -Directory) {
            # "MessagePack.3.1.10" -> "MessagePack"
            $nugetProvided[($dir.Name -replace '\.\d+(\.\d+)*$', '')] = $true
        }
    }
}

$copied = @{}
$skipped = @{}

# ---- 1. Publish the netstandard2.1 projects --------------------------------

Write-Host "==> Publishing netstandard2.1 projects ($Configuration)..." -ForegroundColor Cyan

$publishDirs = @()
foreach ($project in $netstandardProjects) {
    $fullPath = Join-Path $repoRoot $project
    $publishDir = Join-Path $repoRoot "artifacts/publish/$Configuration/$( [IO.Path]::GetFileNameWithoutExtension($project) )"
    $publishDirs += $publishDir

    dotnet publish $fullPath -c $Configuration -f netstandard2.1 -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $project"
    }
}

# ---- 2. Wipe + collect ------------------------------------------------------

Write-Host "==> Collecting assemblies..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $unityPluginDir | Out-Null
# The plugin folder is fully generated — wipe it first so stale DLLs from a
# previous sync (since excluded by the allowlists) cannot linger. Keep
# .gitkeep (tracked placeholder) and let Unity regenerate the .meta files.
Get-ChildItem $unityPluginDir -File -Exclude '.gitkeep' | Remove-Item -Force

function Copy-IfAllowed([System.IO.FileInfo]$dll) {
    $name = $dll.Name
    $nameNoExt = $dll.BaseName

    if ($script:unityProvided -contains $name) {
        $script:skipped[$name] = 'unity-provided'
        return
    }
    if ($script:nugetProvided.ContainsKey($nameNoExt)) {
        $script:skipped[$name] = 'nuget-provided'
        return
    }
    if ($script:projectAssemblies -contains $name -or $script:thirdPartyAssemblies -contains $name) {
        Copy-Item $dll.FullName -Destination (Join-Path $script:unityPluginDir $name) -Force
        $script:copied[$name] = $dll.Length
        return
    }

    Write-Warning "Unrecognized assembly '$name' — not copied. Add it to tools/sync-dlls.ps1 if Unity needs it."
    $script:skipped[$name] = 'unrecognized'
}

# 2a. From the publish output.
foreach ($dir in $publishDirs) {
    if (-not (Test-Path $dir)) {
        throw "Expected publish directory not found: $dir"
    }
    foreach ($file in Get-ChildItem $dir -File) {
        if ($file.Extension -ne '.dll') {
            $skipped[$file.Name] = 'not-a-dll'
            continue
        }
        Copy-IfAllowed $file
    }
}

# 2b. Net stack (embedded node) from the NuGet cache — exact pinned versions.
$nugetCachePackages = @(
    'libplanet.net',
    'libplanet.stun',
    'planetarium.netmq',
    'asyncio',
    'asyncenumerator',
    'destructurama.attributed',
    'microsoft.extensions.caching.memory',
    'nacl.net'
)

foreach ($packageId in $nugetCachePackages) {
    $packageRoot = Join-Path $env:USERPROFILE ".nuget/packages/$packageId"
    if (-not (Test-Path $packageRoot)) {
        Write-Warning "NuGet cache package not found: $packageId"
        continue
    }

    $versionDir = Get-ChildItem $packageRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1

    $libDir = @('netstandard2.1', 'netstandard2.0', 'netstandard1.6', 'netstandard1.3', 'netstandard1.1') |
        ForEach-Object { Join-Path $versionDir.FullName "lib/$_" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $libDir) {
        Write-Warning "No netstandard lib dir for $packageId ($($versionDir.Name))"
        continue
    }

    foreach ($dll in Get-ChildItem $libDir -Filter '*.dll' -File) {
        Copy-IfAllowed $dll
    }
}

# ---- 3. Report ---------------------------------------------------------------

Write-Host ""
Write-Host "==> Copied to $unityPluginDir" -ForegroundColor Green
foreach ($name in ($copied.Keys | Sort-Object)) {
    "{0,-45} {1,10:N0} bytes" -f $name, $copied[$name] | Write-Host
}

$other = $skipped.Keys | Sort-Object
if ($other.Count -gt 0) {
    Write-Host ""
    Write-Host "==> Not copied" -ForegroundColor Yellow
    foreach ($name in $other) {
        "{0,-45} ({1})" -f $name, $skipped[$name] | Write-Host
    }
}

Write-Host ""
Write-Host "Done. Open the Unity project and check the Console for version conflicts." -ForegroundColor Green
