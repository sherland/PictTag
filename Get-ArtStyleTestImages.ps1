<#
.SYNOPSIS
Downloads 2-3 freely-licensed representative images per art style listed in
data/art-styles-manifest.json from Wikimedia Commons, for use as detection test fixtures.

Images are saved under data/test-images/art-styles/<slug>/ and are NOT committed to git
(bulk binary fixtures, regenerable on demand from the manifest) - see .gitignore.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Resize-ImageInPlace {
    param([string]$Path, [int]$MaxDimension = 2000)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    try {
        $original = [System.Drawing.Image]::FromStream($stream)
        try {
            if ($original.Width -le $MaxDimension -and $original.Height -le $MaxDimension) {
                return
            }

            $scale = $MaxDimension / [Math]::Max($original.Width, $original.Height)
            $newWidth = [int]([Math]::Round($original.Width * $scale))
            $newHeight = [int]([Math]::Round($original.Height * $scale))

            $resized = New-Object System.Drawing.Bitmap($newWidth, $newHeight)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($resized)
                try {
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.DrawImage($original, 0, 0, $newWidth, $newHeight)
                }
                finally { $graphics.Dispose() }

                $format = $original.RawFormat
                $resized.Save($Path, $format)
            }
            finally { $resized.Dispose() }
        }
        finally { $original.Dispose() }
    }
    finally { $stream.Dispose() }
}

Push-Location $PSScriptRoot
try {
    $manifestPath = Join-Path $PSScriptRoot 'data/art-styles-manifest.json'
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    $imagesPerStyle = 3
    $userAgent = 'PictTag-TestFixtureDownloader/1.0 (https://github.com/sherland/PictTag)'
    $allowedLicensePatterns = @('public domain', 'cc0', 'cc by')

    $totalDownloaded = 0
    $emptyStyles = @()

    foreach ($style in $manifest) {
        $styleDir = Join-Path $PSScriptRoot "data/test-images/art-styles/$($style.slug)"
        New-Item -ItemType Directory -Force -Path $styleDir | Out-Null

        $searchUri = 'https://commons.wikimedia.org/w/api.php?' + (
            @(
                'action=query'
                'generator=search'
                "gsrsearch=$([Uri]::EscapeDataString($style.searchQuery))"
                'gsrnamespace=6'
                'gsrlimit=12'
                'prop=imageinfo'
                'iiprop=url|extmetadata|mime|size'
                'format=json'
            ) -join '&'
        )

        try {
            $response = Invoke-RestMethod -Uri $searchUri -Headers @{ 'User-Agent' = $userAgent }
        }
        catch {
            Write-Warning "[$($style.slug)] Commons API request failed: $($_.Exception.Message)"
            $emptyStyles += $style.slug
            continue
        }

        $pages = $response.query.pages.PSObject.Properties.Value
        $candidates = @()
        foreach ($page in $pages) {
            $info = $page.imageinfo | Select-Object -First 1
            if (-not $info) { continue }
            if ($info.mime -notmatch '^image/(jpeg|png|webp)$') { continue }
            if ($info.width -lt 400 -or $info.height -lt 400) { continue }

            $license = $info.extmetadata.LicenseShortName.value
            if (-not $license) { continue }
            $licenseLower = $license.ToLowerInvariant()
            $isAllowed = $false
            foreach ($pattern in $allowedLicensePatterns) {
                if ($licenseLower.Contains($pattern)) { $isAllowed = $true; break }
            }
            if (-not $isAllowed) { continue }

            $candidates += [PSCustomObject]@{
                Url   = $info.url
                Mime  = $info.mime
                Title = $page.title
            }
        }

        if ($candidates.Count -eq 0) {
            Write-Warning "[$($style.slug)] No permissively-licensed images found for query '$($style.searchQuery)'."
            $emptyStyles += $style.slug
            continue
        }

        $picked = $candidates | Select-Object -First $imagesPerStyle
        $index = 1
        foreach ($image in $picked) {
            $ext = switch ($image.Mime) {
                'image/jpeg' { '.jpg' }
                'image/png'  { '.png' }
                'image/webp' { '.webp' }
                default      { '.jpg' }
            }
            $destPath = Join-Path $styleDir "image_$index$ext"

            if (Test-Path $destPath) {
                Write-Host "[$($style.slug)] Already have $destPath, skipping."
                $index++
                continue
            }

            $maxAttempts = 4
            $delayMs = 800
            for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
                try {
                    Invoke-WebRequest -Uri $image.Url -Headers @{ 'User-Agent' = $userAgent } -OutFile $destPath
                    Resize-ImageInPlace -Path $destPath
                    Write-Host "[$($style.slug)] Saved $($image.Title) -> $destPath"
                    $totalDownloaded++
                    break
                }
                catch {
                    $statusCode = $_.Exception.Response.StatusCode.value__
                    if ($statusCode -eq 429 -and $attempt -lt $maxAttempts) {
                        Write-Warning "[$($style.slug)] Rate limited, backing off $($delayMs)ms (attempt $attempt/$maxAttempts)..."
                        Start-Sleep -Milliseconds $delayMs
                        $delayMs *= 3
                        continue
                    }
                    Write-Warning "[$($style.slug)] Failed to download $($image.Url): $($_.Exception.Message)"
                    break
                }
            }

            Start-Sleep -Milliseconds 700
            $index++
        }

        Start-Sleep -Milliseconds 300
    }

    Write-Host ""
    Write-Host "Downloaded $totalDownloaded images across $($manifest.Count - $emptyStyles.Count)/$($manifest.Count) styles."
    if ($emptyStyles.Count -gt 0) {
        Write-Host "Styles with no usable images: $($emptyStyles -join ', ')"
    }
}
finally {
    Pop-Location
}
