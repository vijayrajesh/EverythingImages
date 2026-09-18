# Puts the AI engine (llama.cpp's Windows Vulkan build) into runtime\ai-engine,
# from where the build copies it next to EverythingImages.exe. Vulkan runs the models
# on any NVIDIA, AMD or Intel graphics card through the driver already on the
# PC, and the same files run them on the processor, so nothing is downloaded
# when the app runs except the models. Only the files describing needs are kept.
# Build and checksum are pinned in scriptslama-build.json. Skips the download when
# that build is in place.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfg = Get-Content (Join-Path $PSScriptRoot 'llama-build.json') -Raw | ConvertFrom-Json
$dest = Join-Path $root 'runtime\ai-engine'
$marker = Join-Path $dest 'build.txt'
$keep = '^(llama-mtmd-cli\.exe|mtmd\.dll|llama\.dll|llama-common\.dll|ggml\.dll|ggml-base\.dll|ggml-vulkan\.dll|ggml-cpu-.*\.dll|libomp\.dll|LICENSE.*)$'

if ((Test-Path (Join-Path $dest 'llama-mtmd-cli.exe')) -and (Test-Path (Join-Path $dest 'ggml-vulkan.dll')) -and
    (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $cfg.build)) {
    Write-Host "AI engine ($($cfg.build)) already in place."
    exit 0
}

$name = "llama-$($cfg.build)-bin-win-vulkan-x64.zip"
$url = "https://github.com/ggml-org/llama.cpp/releases/download/$($cfg.build)/$name"
$zip = Join-Path $env:TEMP $name
$unpacked = Join-Path $env:TEMP "everythingimages-ai-engine-$($cfg.build)"

Write-Host "Downloading the AI engine: $url"
& curl.exe -L --fail -o $zip $url
if ($LASTEXITCODE -ne 0) { throw "Download failed (curl exit $LASTEXITCODE)." }

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($hash -ne $cfg.vulkan_zip_sha256) {
    Remove-Item $zip -Force
    throw "Checksum mismatch for ${name}: got $hash, expected $($cfg.vulkan_zip_sha256)."
}
Write-Host "Checksum OK. Unpacking to $dest"

if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $unpacked
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest | Out-Null
Get-ChildItem $unpacked -File | Where-Object { $_.Name -match $keep } | Copy-Item -Destination $dest
foreach ($required in 'llama-mtmd-cli.exe', 'ggml-vulkan.dll', 'ggml-base.dll', 'mtmd.dll') {
    if (-not (Test-Path (Join-Path $dest $required))) { throw "$name no longer contains $required." }
}
Set-Content -Path $marker -Value $cfg.build -Encoding ascii
Remove-Item $unpacked -Recurse -Force
Remove-Item $zip -Force
$files = Get-ChildItem $dest -File
Write-Host ("AI engine ready: {0} files, {1:N1} MB." -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
