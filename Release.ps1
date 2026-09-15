#Requires -Version 5.1
<#
.SYNOPSIS
Builds EnCReset and publishes its VSIX as a GitHub release.
.EXAMPLE
.\Release.ps1 -BuildOnly
.EXAMPLE
.\Release.ps1 -Draft
.EXAMPLE
.\Release.ps1
#>
[CmdletBinding()]
param(
    [switch]$BuildOnly,
    [switch]$Draft,
    [string]$MSBuildPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File failed with exit code $LASTEXITCODE."
    }
}

Push-Location $PSScriptRoot
try {
    if ($BuildOnly -and $Draft) {
        throw 'Choose either -BuildOnly or -Draft.'
    }
    [xml]$manifest = Get-Content -LiteralPath 'source.extension.vsixmanifest' -Raw
    $version = $manifest.PackageManifest.Metadata.Identity.Version
    if ($version -notmatch '^\d+\.\d+(\.\d+){0,2}$') {
        throw "Invalid VSIX version: $version"
    }
    $tag = "v$version"
    $repository = 'aguerrieri82/EnCReset'
    $asset = Join-Path $PSScriptRoot 'bin\Release\net48\EnCReset.vsix'

    if (!$BuildOnly) {
        if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
            throw 'Install GitHub CLI (winget install --id GitHub.cli), reopen PowerShell, then run gh auth login.'
        }
        Invoke-Checked 'gh' @('auth', 'status', '--hostname', 'github.com')
        $changes = Invoke-Checked 'git' @('status', '--porcelain')
        if ($changes) {
            throw 'Commit all changes before releasing, including the manifest version and release script.'
        }
        $commit = Invoke-Checked 'git' @('rev-parse', 'HEAD')
        # Ensure GitHub has the exact commit being built.
        $remoteCommit = Invoke-Checked 'gh' @('api', "repos/$repository/commits/$commit", '--jq', '.sha')
        if ($remoteCommit -ne $commit) {
            throw 'Push the current commit to GitHub before releasing.'
        }
        $tags = @(Invoke-Checked 'git' @('ls-remote', '--tags', "https://github.com/$repository.git", "refs/tags/$tag"))
        if ($tags.Count -gt 0) {
            throw "Tag $tag already exists. Increase the manifest version and commit/push it before a new release."
        }
        $releases = Invoke-Checked 'gh' @('api', '--paginate', "repos/$repository/releases", '--jq', '.[].tag_name')
        if ($tag -in @($releases)) {
            throw "Release $tag already exists. Review it on GitHub before retrying."
        }
    }

    if (!$MSBuildPath) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (!(Test-Path -LiteralPath $vswhere)) {
            throw 'Visual Studio was not found. Install the extension development workload or supply -MSBuildPath.'
        }
        $candidates = @(Invoke-Checked $vswhere @('-latest', '-prerelease', '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-find', 'MSBuild\**\Bin\MSBuild.exe'))
        if (!$candidates.Count) {
            throw 'MSBuild was not found. Supply -MSBuildPath.'
        }
        $MSBuildPath = $candidates[0]
    }
    if (!(Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) {
        throw "MSBuild not found: $MSBuildPath"
    }

    Invoke-Checked $MSBuildPath @(
        'EnCReset.csproj', '/restore', '/t:Rebuild',
        '/p:Configuration=Release', '/p:DeployExtension=false',
        '/p:VsixDeployOnDebug=false', '/nologo', '/v:minimal'
    )
    if (!(Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "Build did not produce $asset"
    }

    # Check the packaged version before uploading.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($asset)
    try {
        $entry = $archive.GetEntry('extension.vsixmanifest')
        if (!$entry) { throw 'VSIX is missing its manifest.' }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml]$packagedManifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        if ($packagedManifest.PackageManifest.Metadata.Identity.Version -ne $version) {
            throw 'Packaged VSIX version does not match the source manifest.'
        }
    }
    finally { $archive.Dispose() }

    if ($BuildOnly) {
        Write-Host "Build verified: $asset (version $version). Nothing published."
        return
    }

    # Recheck the checkout after the build, before any remote mutation.
    $changes = Invoke-Checked 'git' @('status', '--porcelain')
    $currentCommit = Invoke-Checked 'git' @('rev-parse', 'HEAD')
    if ($changes -or $currentCommit -ne $commit) {
        throw 'The checkout changed during the build. Commit/push changes and rerun.'
    }
    $releaseArguments = @(
        'release', 'create', $tag, $asset,
        '--repo', $repository, '--target', $commit,
        '--title', "EnC Reset $version", '--generate-notes'
    )
    if ($Draft) { $releaseArguments += '--draft' }
    Invoke-Checked 'gh' $releaseArguments
}
finally {
    Pop-Location
}