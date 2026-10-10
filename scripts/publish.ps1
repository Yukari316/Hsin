[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$extension = if ($Runtime -eq 'win-x64') { 'zip' } else { 'tar.gz' }
$output = Join-Path $repoRoot "artifacts/Hsin-$Runtime.$extension"
$webRoot = Join-Path $repoRoot 'server/wwwroot'

if (Test-Path -LiteralPath $output) {
    throw "Output already exists: $output. Move the previous build to an archive before publishing. Do not publish into a running installation."
}
if ((Test-Path -LiteralPath $webRoot) -and ((Get-Item -LiteralPath $webRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Refusing to replace a linked wwwroot: $webRoot"
}

Push-Location $repoRoot
try {
    & pnpm --dir web install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency restore failed.' }
    & pnpm --dir web run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }

    if (Test-Path -LiteralPath $webRoot) { Remove-Item -LiteralPath $webRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $webRoot | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'web/dist') -Force | Copy-Item -Destination $webRoot -Recurse

    $staging = Join-Path $repoRoot ('artifacts/.publish-' + $Runtime + '-' + [guid]::NewGuid().ToString('N'))
    & dotnet publish server/Hsin.csproj -c Release -r $Runtime --self-contained true -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Publish failed. Partial build retained at $staging" }

    foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $staging
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'config/hsin.example.json') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $staging 'docs') -Recurse
    New-Item -ItemType Directory -Path (Join-Path $staging 'licenses') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'web/src/vendor/LICENSE') -Destination (Join-Path $staging 'licenses/MediaMTX-LICENSE')
    & node scripts/collect-licenses.mjs $staging
    if ($LASTEXITCODE -ne 0) { throw "Dependency license collection failed. Partial build retained at $staging" }

    $temporaryArchive = Join-Path $repoRoot ('artifacts/.package-' + $Runtime + '-' + [guid]::NewGuid().ToString('N') + '.' + $extension)
    if ($Runtime -eq 'win-x64') {
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $temporaryArchive
    }
    else {
        $gnuTar = Join-Path ${env:ProgramFiles} 'Git/usr/bin/tar.exe'
        $gzip = Join-Path ${env:ProgramFiles} 'Git/usr/bin/gzip.exe'
        if (-not (Test-Path -LiteralPath $gnuTar) -or -not (Test-Path -LiteralPath $gzip)) {
            throw 'Creating a Linux archive on Windows requires GNU tar and gzip from Git for Windows.'
        }
        $archive = Join-Path (Split-Path $staging) ([IO.Path]::GetFileName($staging) + '.tar')
        Push-Location $staging
        try {
            & $gnuTar --force-local --create --file $archive --mode='u+rwX,go+rX' --exclude='./Hsin' .
            if ($LASTEXITCODE -ne 0) { throw 'Creating the Linux tar archive failed.' }
            & $gnuTar --force-local --append --file $archive --mode=0755 Hsin
            if ($LASTEXITCODE -ne 0) { throw 'Adding the Linux executable to the archive failed.' }
        }
        finally { Pop-Location }
        & $gzip --no-name $archive
        if ($LASTEXITCODE -ne 0) { throw 'Compressing the Linux tar archive failed.' }
        Move-Item -LiteralPath ($archive + '.gz') -Destination $temporaryArchive
    }
    Move-Item -LiteralPath $temporaryArchive -Destination $output
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($staging).StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove staging outside artifacts: $staging"
    }
    Remove-Item -LiteralPath $staging -Recurse -Force
    Write-Host "Published $Runtime to $output. Target-system runtime and media acceptance remain separate checks."
}
finally {
    Pop-Location
}
