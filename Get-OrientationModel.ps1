<#
.SYNOPSIS
Downloads the ONNX image-orientation-classifier model into data/models/orientation/ and verifies
its checksum. Git-ignored (unlike the WordNet/taxonomy data) - this ~80MB binary is fetched on
demand rather than committed, matching the bulk art-style test image convention.

Model: DuarteBarbosa/deep-image-orientation-detection (EfficientNetV2-S fine-tuned for 4-way
image orientation classification, 98.82% validation accuracy, MIT license).
https://huggingface.co/DuarteBarbosa/deep-image-orientation-detection

OnnxImageOrientationClassifier also auto-downloads this file on first use if missing, so running
this script by hand is optional - it exists for explicit/CI pre-fetching and checksum
verification.
#>

$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    $modelDir = Join-Path $PSScriptRoot 'data/models/orientation'
    New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

    $modelPath = Join-Path $modelDir 'orientation_model_v2_0.9882.onnx'
    $expectedSha256 = 'cffe911c1dff47fbfbbd90110aaab9c07134645c460d35b3ae8832079bea91ba'

    Write-Host 'Downloading orientation classifier model (orientation_model_v2_0.9882.onnx, ~80MB)...'
    Invoke-WebRequest `
        -Uri 'https://huggingface.co/DuarteBarbosa/deep-image-orientation-detection/resolve/main/orientation_model_v2_0.9882.onnx' `
        -OutFile $modelPath

    $actualSha256 = (Get-FileHash -Path $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        Write-Warning "Checksum mismatch for orientation_model_v2_0.9882.onnx - expected $expectedSha256, got $actualSha256. If this is an intentional upstream update, update the expected hash in this script and in OnnxImageOrientationClassifier."
    }
    else {
        Write-Host "Verified orientation_model_v2_0.9882.onnx (sha256 matches)."
    }
}
finally {
    Pop-Location
}
