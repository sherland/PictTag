<#
.SYNOPSIS
Runs the PictTag detection + XMP pipeline against every downloaded art-style test image
(see Get-ArtStyleTestImages.ps1) and prints a summary of detected vs. expected style/medium.

Skips images that already have a sidecar (default CLI behavior), so re-running only
processes images added since the last run. Pass -Overwrite to regenerate everything.
#>
param(
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $manifestPath = Join-Path $PSScriptRoot 'data/art-styles-manifest.json'
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    $previewDir = Join-Path $env:TEMP 'PictTag-annotated-preview-art-styles'
    $xmpArgs = @('--xmp')
    if ($Overwrite) { $xmpArgs += '--xmp-overwrite' }

    foreach ($ext in @('jpg', 'jpeg', 'png', 'webp')) {
        if (-not (Get-ChildItem -Path 'data/test-images/art-styles' -Filter "*.$ext" -Recurse -File -ErrorAction SilentlyContinue)) {
            continue
        }

        Write-Host "== Running detection for *.$ext art-style images =="
        dotnet run --project source/PictTag.Cli -- `
            -i "data/test-images/art-styles/**/*.$ext" `
            -o $previewDir `
            @xmpArgs
    }

    # --- Summary: read every generated sidecar back and compare against the manifest ---
    Add-Type -AssemblyName System.Xml.Linq

    $results = @()
    foreach ($style in $manifest) {
        $styleDir = Join-Path $PSScriptRoot "data/test-images/art-styles/$($style.slug)"
        if (-not (Test-Path $styleDir)) { continue }

        $xmpFiles = Get-ChildItem -Path $styleDir -Filter '*.xmp' -File
        foreach ($xmpFile in $xmpFiles) {
            [xml]$xmp = Get-Content $xmpFile.FullName -Raw
            $ns = New-Object System.Xml.XmlNamespaceManager($xmp.NameTable)
            $ns.AddNamespace('rdf', 'http://www.w3.org/1999/02/22-rdf-syntax-ns#')
            $ns.AddNamespace('pictTag', 'https://github.com/sherland/PictTag/ns/1.0/')

            $desc = $xmp.SelectSingleNode('//rdf:Description', $ns)
            $medium = $desc.GetAttribute('Medium', 'https://github.com/sherland/PictTag/ns/1.0/')
            $artStyle = $desc.GetAttribute('ArtStyle', 'https://github.com/sherland/PictTag/ns/1.0/')
            $colorVariance = $desc.GetAttribute('ColorVariance', 'https://github.com/sherland/PictTag/ns/1.0/')
            $edgeDensity = $desc.GetAttribute('EdgeDensity', 'https://github.com/sherland/PictTag/ns/1.0/')
            $symmetry = $desc.GetAttribute('Symmetry', 'https://github.com/sherland/PictTag/ns/1.0/')

            $artStyleLower = if ($artStyle) { $artStyle.ToLowerInvariant() } else { '' }
            $matched = $false
            foreach ($kw in $style.styleKeywords) {
                if ($artStyleLower.Contains($kw.ToLowerInvariant())) { $matched = $true; break }
            }

            $results += [PSCustomObject]@{
                Style         = $style.displayName
                File          = $xmpFile.Name
                Medium        = $medium
                ArtStyle      = $artStyle
                Matched       = $matched
                Symmetry      = $symmetry
                ColorVariance = $colorVariance
                EdgeDensity   = $edgeDensity
            }
        }
    }

    $results | Format-Table -AutoSize Style, File, Medium, ArtStyle, Matched, Symmetry, ColorVariance, EdgeDensity

    $matchedCount = ($results | Where-Object { $_.Matched }).Count
    $total = $results.Count
    Write-Host ""
    Write-Host "Style keyword match: $matchedCount / $total"

    $nonArtMedium = $results | Where-Object { $_.Medium -in @('Photograph', 'Screenshot') }
    if ($nonArtMedium.Count -gt 0) {
        Write-Host "Images classified as Photograph/Screenshot despite being art fixtures ($($nonArtMedium.Count)):"
        $nonArtMedium | ForEach-Object { Write-Host "  $($_.Style)/$($_.File): $($_.Medium)" }
    }

    $results | Export-Csv -Path (Join-Path $PSScriptRoot 'art-style-detection-results.csv') -NoTypeInformation
    Write-Host "Full results written to art-style-detection-results.csv"
}
finally {
    Pop-Location
}
