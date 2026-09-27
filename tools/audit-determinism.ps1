#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Stage-4 determinism audit: ProjectF.Lib must contain ZERO non-deterministic
    API usage inside on-chain code (knowledge.md rule 2).

.DESCRIPTION
    Libplanet re-executes every IAction.Execute() on every full node, so wall
    clocks, process-seeded RNGs, file I/O and environment reads silently break
    consensus. This script is the grep the Stage-4 spec asks for, made
    repeatable and CI-friendly:

        pwsh tools/audit-determinism.ps1          # exit 1 on unexpected hits
        pwsh tools/audit-determinism.ps1 -Quiet   # prints hits only

    Scope: every .cs file under src/ProjectF.Lib (states, actions, exceptions,
    genesis, policy), excluding bin/obj. The stage-1 PingAction is not
    exempt — nothing in this assembly is allowed to be non-deterministic.

    The same rules are enforced in tests by
    ProjectF.Lib.Tests/DeterminismAuditTests.cs, so `dotnet test` fails too
    if someone lands a forbidden call.

.PARAMETER Quiet
    Only print matched lines (or nothing when clean). Useful in CI logs.

.PARAMETER ListPatterns
    Print the audited pattern table and exit (documentation mode).
#>

[CmdletBinding()]
param(
    [switch]$Quiet,
    [switch]$ListPatterns
)

$ErrorActionPreference = 'Stop'

# Forbidden non-deterministic API surface (knowledge.md rule 2 + standard
# consensus hazards). Regexes, evaluated case-sensitively.
$patterns = [ordered]@{
    'DateTime.Now'                = 'wall clock'
    'DateTime.UtcNow'             = 'wall clock'
    'DateTimeOffset\.Now'         = 'wall clock'
    'DateTimeOffset\.UtcNow'      = 'wall clock'
    'new\s+Random\s*\('           = 'process-seeded RNG'
    'System\.Random'              = 'process-seeded RNG'
    'Guid\.NewGuid'               = 'random GUID'
    '\bFile\.\w+'                 = 'file I/O'
    '\bDirectory\.\w+'            = 'filesystem I/O'
    '\bEnvironment\.\w+'          = 'machine/environment reads'
    'Process\.Start'              = 'process spawn'
    'Stopwatch\.'                 = 'wall-clock timing'
}

# Whitelisted source files (by name) that are known non-on-chain or test-only.
$exemptFiles = @(
    # None today. Tests live in ProjectF.Lib.Tests (outside this assembly).
    # If a file ever needs an exemption, name it here with a comment why.
)

# Path-anchored regex exemptions: 'path-regex' => 'reason'.
# Keep this empty; every entry weakens the guarantee.
$tolerances = @{
    # 'Policy/BlockPolicySource\.cs$' => 'example only: reason here'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = Join-Path $repoRoot 'src/ProjectF.Lib'

if ($ListPatterns) {
    foreach ($entry in $patterns.GetEnumerator()) {
        '{0,-32} {1}' -f $entry.Key, $entry.Value
    }
    exit 0
}

$files = Get-ChildItem $sourceRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Where-Object { $exemptFiles -notcontains $_.Name } |
    Sort-Object FullName

if (-not $files) {
    throw "No source files found under $sourceRoot — audit scope is broken."
}

$hits = @()
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($repoRoot, $file.FullName)
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($entry in $patterns.GetEnumerator()) {
            if ($lines[$i] -cmatch $entry.Key) {
                # Path-anchored tolerance filter.
                $tolerated = $false
                foreach ($tolerance in $tolerances.GetEnumerator()) {
                    if ($relative -cmatch $tolerance.Key) {
                        $tolerated = $true
                        break
                    }
                }

                if (-not $tolerated) {
                    $hits += [pscustomobject]@{
                        File    = $relative
                        Line    = $i + 1
                        Rule    = $entry.Key
                        Reason  = $entry.Value
                        Text    = $lines[$i].Trim()
                    }
                }
            }
        }
    }
}

if ($hits.Count -eq 0) {
    if (-not $Quiet) {
        $message = "==> PASS: {0} files scanned, 0 non-deterministic API hits (knowledge.md rule 2)." -f $files.Count
        Write-Host $message -ForegroundColor Green
    }
    exit 0
}

Write-Host "==> FAIL: $($hits.Count) forbidden API hit(s) in ProjectF.Lib:" -ForegroundColor Red
$hits | Format-Table File, Line, Rule, Text -AutoSize | Out-String | Write-Host
Write-Host 'Fix the code or, ONLY with a written justification, add a path-anchored'
Write-Host 'tolerance in tools/audit-determinism.ps1 ($tolerances).'
exit 1
