$ErrorActionPreference = "Stop"

Write-Host "=== SomaticVR Symlink Setup ==="

$protobufSource = Join-Path $env:APPDATA "SomaticVR-Server-Backend\Protobuf"
$protobufLink = "src\Protobuf"

$emulationSource = Join-Path $env:APPDATA "SomaticVR-Server-Backend\EmulationData"
$emulationLink = "EmulationData"

function CreateLink($link, $target) {

    if (-not (Test-Path $target)) {
        Write-Host "ERROR: Target does not exist: $target"
        exit 1
    }

    # Determine parent directory
    $parent = Split-Path -Path $link -Parent
    if ([string]::IsNullOrWhiteSpace($parent)) {
        $parent = (Get-Item ".").FullName
    }

    # Ensure parent directory exists
    if (-not (Test-Path $parent)) {
        Write-Host "Creating directory: $parent"
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    # Remove existing item
    if (Test-Path $link) {
        Write-Host "Removing existing symlink  at $link"
        Remove-Item -Force -Recurse -LiteralPath $link
    }

    Write-Host "Creating symlink:"
    Write-Host "  Link:   $link"
    Write-Host "  Target: $target"

    New-Item -ItemType SymbolicLink -Path $link -Target $target | Out-Null
}

CreateLink $protobufLink $protobufSource
CreateLink $emulationLink $emulationSource

Write-Host "Symlinks created successfully."
