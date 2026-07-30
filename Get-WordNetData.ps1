<#
.SYNOPSIS
Downloads the Princeton WordNet 3.0 noun database (data.noun/index.noun/LICENSE) and the
ImageNet-1k class index, verifies them against the checksums recorded in
data/wordnet/raw/SOURCES.md, and (re)populates data/wordnet/raw/.

These four files ARE committed to git (unlike the bulk art-style test images) - they're
small plain-text/JSON inputs needed deterministically offline by PictTag.TaxonomyBuilder.
This script exists for reproducibility (verifying nothing drifted, or re-fetching after a
future WordNet version bump), not because the checked-in copies are regenerated routinely.

Only the noun files are extracted from the WordNet tarball - PictTag only tags nouns.
#>

$ErrorActionPreference = 'Stop'

function Get-Sha256 {
    param([string]$Path)
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

Push-Location $PSScriptRoot
try {
    $rawDir = Join-Path $PSScriptRoot 'data/wordnet/raw'
    New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("pict-tag-wordnet-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    try {
        $tarballPath = Join-Path $work 'WNdb-3.0.tar.gz'
        Write-Host 'Downloading WordNet 3.0 database (WNdb-3.0.tar.gz)...'
        Invoke-WebRequest -Uri 'https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz' -OutFile $tarballPath

        Write-Host 'Extracting dict/data.noun and dict/index.noun...'
        & tar -xzf $tarballPath -C $work 'dict/data.noun' 'dict/index.noun'
        if ($LASTEXITCODE -ne 0) {
            throw "tar extraction failed with exit code $LASTEXITCODE"
        }

        Copy-Item -Path (Join-Path $work 'dict/data.noun') -Destination (Join-Path $rawDir 'data.noun') -Force
        Copy-Item -Path (Join-Path $work 'dict/index.noun') -Destination (Join-Path $rawDir 'index.noun') -Force

        Write-Host 'Downloading WordNet LICENSE...'
        Invoke-WebRequest -Uri 'https://wordnetcode.princeton.edu/3.0/LICENSE' -OutFile (Join-Path $rawDir 'LICENSE')

        Write-Host 'Downloading ImageNet-1k class index...'
        Invoke-WebRequest -Uri 'https://storage.googleapis.com/download.tensorflow.org/data/imagenet_class_index.json' `
            -OutFile (Join-Path $rawDir 'imagenet_class_index.json')
    }
    finally {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Checksums as recorded in SOURCES.md at the time these files were first committed.
    # A mismatch means either the download was corrupted, or upstream has genuinely
    # changed the file (Princeton has never revised WordNet 3.0's files in place, so
    # in practice a mismatch here should be treated as suspicious, not silently trusted).
    $expected = [ordered]@{
        'data.noun'                  = '489f145e0f68877c0be5bd0eb4117adaaac52f38f6204eb8d85dbe2158b614cc'
        'index.noun'                 = 'a490d99d93d017bf4822fe2f0ffa51fd73911ce271dc7535fade21f8814b5a04'
        'imagenet_class_index.json'  = 'a1e7a966a1f601d39e4b43e119b3e7dd4a2ad3ea08cf69847cbaf021013767bc'
    }

    $mismatches = @()
    foreach ($file in $expected.Keys) {
        $actual = Get-Sha256 -Path (Join-Path $rawDir $file)
        if ($actual -ne $expected[$file]) {
            $mismatches += "$file - expected $($expected[$file]), got $actual"
        }
        else {
            Write-Host "Verified $file (sha256 matches SOURCES.md)."
        }
    }

    if ($mismatches.Count -gt 0) {
        Write-Warning "Checksum mismatch for:`n$($mismatches -join "`n")`nIf this is an intentional upstream update, refresh data/wordnet/raw/SOURCES.md with the new hashes."
    }
    else {
        Write-Host "`nAll files verified against data/wordnet/raw/SOURCES.md."
    }
}
finally {
    Pop-Location
}
