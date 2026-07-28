<#
.SYNOPSIS
Regenerates XMP sidecars for the checked-in test images under data/test-images/.

Always overwrites, so re-running this refreshes the fixtures on demand. Annotated
preview images are written to a temp folder since they aren't part of what this
script is for.
#>

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $previewDir = Join-Path $env:TEMP 'PictTag-annotated-preview'
    dotnet run --project source/PictTag.Cli -- `
        -i 'data/test-images/*.jpg' `
        -o $previewDir `
        --xmp `
        --xmp-overwrite
}
finally {
    Pop-Location
}
