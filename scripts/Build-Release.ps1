param(
    [string]$OutputDirectory = "",
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository "artifacts\release"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

Push-Location $repository
try {
    $dirty = git status --porcelain
    if (-not $AllowDirty -and $dirty) {
        throw "Release builds require a clean working tree. Commit the tested source first."
    }

    $commit = (git rev-parse HEAD).Trim()
    [xml]$props = Get-Content -LiteralPath "Directory.Build.props"
    $version = [string]$props.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Directory.Build.props does not define Version."
    }

    $packageText = Get-Content -LiteralPath "Installer\Package.wxs" -Raw
    if ($packageText -notmatch ('Version="' + [regex]::Escape($version) + '"')) {
        throw "Installer version does not match application version $version."
    }

    dotnet test "tests\SpatialBridge.Tests\SpatialBridge.Tests.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

    $staging = Join-Path $repository "artifacts\staging"
    $stagingFull = [IO.Path]::GetFullPath($staging)
    if (-not $stagingFull.StartsWith($repository + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean staging path outside the repository: $stagingFull"
    }
    if (Test-Path -LiteralPath $stagingFull) {
        Remove-Item -LiteralPath $stagingFull -Recurse -Force
    }

    $bridgePublish = Join-Path $stagingFull "bridge"
    $trayPublish = Join-Path $stagingFull "tray"
    dotnet publish "SpatialBridge.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:SourceRevisionId=$commit `
        -p:ContinuousIntegrationBuild=true -o $bridgePublish
    if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed." }
    dotnet publish "Tray\SpatialBridge.Tray.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:SourceRevisionId=$commit `
        -p:ContinuousIntegrationBuild=true -o $trayPublish
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed." }

    $payload = Join-Path $repository "Installer\payload"
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $bridgePublish "SpatialBridge.exe") `
        -Destination (Join-Path $payload "SpatialBridge.exe") -Force
    Copy-Item -LiteralPath (Join-Path $trayPublish "SpatialBridgeTray.exe") `
        -Destination (Join-Path $payload "SpatialBridgeTray.exe") -Force
    Copy-Item -LiteralPath "README.md" -Destination (Join-Path $payload "README.md") -Force
    Copy-Item -LiteralPath "LICENSE" -Destination (Join-Path $payload "LICENSE") -Force

    foreach ($name in @("SpatialBridge.exe", "SpatialBridgeTray.exe")) {
        $published = if ($name -eq "SpatialBridge.exe") {
            Join-Path $bridgePublish $name
        } else {
            Join-Path $trayPublish $name
        }
        $packaged = Join-Path $payload $name
        $publishedHash = (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash
        $packagedHash = (Get-FileHash -LiteralPath $packaged -Algorithm SHA256).Hash
        if ($publishedHash -ne $packagedHash) {
            throw "$name in the installer payload does not match the tested publish output."
        }

        $productVersion = (Get-Item -LiteralPath $packaged).VersionInfo.ProductVersion
        if ($productVersion -ne "$version+$commit") {
            throw "$name reports $productVersion; expected $version+$commit."
        }
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $msi = Join-Path $OutputDirectory "WindowsSpatialBridge-$version-x64.msi"
    Push-Location (Join-Path $repository "Installer")
    try {
        wix build "Package.wxs" -arch x64 `
            -ext WixToolset.UI.wixext/4.0.6 `
            -ext WixToolset.Util.wixext/4.0.6 -out $msi
        if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }
    }
    finally {
        Pop-Location
    }
    wix msi validate $msi
    if ($LASTEXITCODE -ne 0) { throw "MSI validation failed." }

    $msiHash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash
    $checksumFile = Join-Path $OutputDirectory "SHA256SUMS.txt"
    Set-Content -LiteralPath $checksumFile -Encoding ascii `
        -Value "$($msiHash.ToLowerInvariant())  $(Split-Path -Leaf $msi)"
    Write-Host "Release MSI: $msi"
    Write-Host "Source commit: $commit"
    Write-Host "SHA256: $msiHash"
}
finally {
    Pop-Location
}
