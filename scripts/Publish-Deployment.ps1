<#
.SYNOPSIS
    Builds the self-contained PropFlow deployment bundles.

.DESCRIPTION
    Produces one directory (and one .tar.gz) per runtime identifier, each holding everything a
    target machine needs except Docker and Node:

        propflow-deploy       the deployer (docs/DEPLOYMENT.md)
        api/                  self-contained PropFlow.Api
        admin/                self-contained PropFlow.Admin
        web/                  Next.js source; the deployer runs npm ci + next build on first `up`
        compose.yaml          PostgreSQL only

    The web app ships as source rather than a prebuilt tree on purpose: Next's SWC compiler is a
    per-platform native binary (@next/swc-darwin-arm64, @next/swc-win32-x64-msvc, ...), so a
    node_modules built here would not run on a Mac. Node 22 is therefore a target prerequisite.

    A RID-specific restore appends a per-runtime target to every committed packages.lock.json
    (for example `"net10.0/win-x64": {}`). That is a build artifact of this publish, not source:
    each RID would add its own section, and CI restores with --locked-mode for none of them.
    Setting RestorePackagesWithLockFile=false is not an option either — NuGet rejects it outright
    with NU1005 when a lock file exists. So this script records which lock files were clean
    beforehand, publishes, and then reverts exactly those, leaving any genuinely-edited lock file
    (a package bump in flight) untouched.

.PARAMETER RuntimeIdentifier
    One or more .NET RIDs. Defaults to both macOS architectures.

.PARAMETER OutputDirectory
    Where bundles are written. Defaults to ./artifacts.

.PARAMETER SkipArchive
    Leave the bundle directories without also producing .tar.gz files.

.EXAMPLE
    ./scripts/Publish-Deployment.ps1
    ./scripts/Publish-Deployment.ps1 -RuntimeIdentifier win-x64 -SkipArchive
#>
[CmdletBinding()]
param(
    [string[]]$RuntimeIdentifier = @('osx-arm64', 'osx-x64'),
    [string]$OutputDirectory = 'artifacts',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-GnuTar {
    <#
        Returns a tar that supports --mode, or $null.

        On Windows `tar` on PATH is bsdtar (C:\Windows\system32\tar.exe), which rejects --mode.
        Git for Windows ships GNU tar in its usr\bin. Its install root varies by installer --
        Program Files for the official one, but scoop, winget and chocolatey all put it
        elsewhere -- so the location is derived from where `git` itself actually is rather than
        guessed from a fixed list. Every candidate is confirmed by running --version; nothing is
        assumed from the path alone.
    #>
    $candidates = @()

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        # ...\cmd\git.exe or ...\bin\git.exe -> ...\usr\bin\tar.exe
        $gitRoot = Split-Path -Parent (Split-Path -Parent $git.Source)
        $candidates += (Join-Path $gitRoot 'usr\bin\tar.exe')
    }
    $candidates += @(
        "$env:ProgramFiles\Git\usr\bin\tar.exe"
        "${env:ProgramFiles(x86)}\Git\usr\bin\tar.exe"
        "$env:LOCALAPPDATA\Programs\Git\usr\bin\tar.exe"
    )
    $onPath = Get-Command tar -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not $candidate -or -not (Test-Path $candidate)) { continue }
        $banner = & $candidate --version 2>$null | Select-Object -First 1
        if ($banner -match 'GNU tar') { return $candidate }
    }
    return $null
}
Push-Location $repoRoot
try {
    $version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.VersionPrefix
    $suffix = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.VersionSuffix
    if ($suffix) { $version = "$version-$suffix" }
    Write-Host "PropFlow $version" -ForegroundColor Cyan

    $output = Join-Path $repoRoot $OutputDirectory
    New-Item -ItemType Directory -Force -Path $output | Out-Null

    # A self-contained restore appends a per-RID section to every committed packages.lock.json,
    # and this script reverts them afterwards. That revert is only safe if they start clean:
    # refuse rather than guess, because an earlier interrupted run leaves them dirty and a
    # "skip the dirty ones" policy would then silently leave RID sections behind. Fail fast and
    # say exactly how to recover.
    $trackedLocks = @(git ls-files -- '*packages.lock.json')
    $dirtyBefore = @(git status --porcelain -- '*packages.lock.json' |
        ForEach-Object { $_.Substring(3).Trim('"') })
    if ($dirtyBefore.Count -gt 0) {
        throw ("These lock files have uncommitted changes, so this script cannot safely revert " +
            "what the RID-specific restore is about to add:`n  $($dirtyBefore -join "`n  ")`n" +
            "Commit them, or discard with: git checkout -- '*packages.lock.json'")
    }
    Write-Host "Guarding $($trackedLocks.Count) lock file(s) against the RID-specific restore."

    foreach ($rid in $RuntimeIdentifier) {
        $bundleName = "propflow-$version-$rid"
        $bundle = Join-Path $output $bundleName
        Write-Host "`n=== $rid ===" -ForegroundColor Cyan

        if (Test-Path $bundle) { Remove-Item -Recurse -Force $bundle }
        New-Item -ItemType Directory -Force -Path $bundle | Out-Null

        $publishArgs = @(
            '--configuration', 'Release'
            '--runtime', $rid
            '--self-contained', 'true'
            '-p:PublishSingleFile=false'
            '-p:DebugType=none'
        )

        foreach ($component in @(
                @{ Project = 'src/PropFlow.Api'; Target = 'api' },
                @{ Project = 'tools/PropFlow.Admin'; Target = 'admin' })) {

            $target = Join-Path $bundle $component.Target
            Write-Host "  publish $($component.Project) -> $($component.Target)"
            dotnet publish $component.Project @publishArgs --output $target | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($component.Project) ($rid)." }
        }

        # The deployer is published as a single file so the bundle root holds one obvious
        # executable rather than ~190 runtime assemblies. The API and admin tool stay as ordinary
        # directories: they are never launched by hand, and single-file extraction would only add
        # a first-run cost to every invocation.
        Write-Host '  publish tools/PropFlow.Deploy -> propflow-deploy (single file)'
        $deployStaging = Join-Path $output "_deploy-$rid"
        if (Test-Path $deployStaging) { Remove-Item -Recurse -Force $deployStaging }
        dotnet publish 'tools/PropFlow.Deploy' @publishArgs `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            --output $deployStaging | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for tools/PropFlow.Deploy ($rid)." }

        $deployExe = Get-ChildItem $deployStaging -File |
            Where-Object { $_.Name -in @('propflow-deploy', 'propflow-deploy.exe') }
        if (-not $deployExe) {
            throw "Single-file publish produced no propflow-deploy executable in $deployStaging."
        }
        Copy-Item $deployExe.FullName (Join-Path $bundle $deployExe.Name)
        Remove-Item -Recurse -Force $deployStaging

        Write-Host '  copy compose.yaml'
        Copy-Item 'compose.yaml' (Join-Path $bundle 'compose.yaml')

        Write-Host '  copy web source'
        $web = Join-Path $bundle 'web'
        New-Item -ItemType Directory -Force -Path $web | Out-Null
        foreach ($item in @('app', 'lib', 'public')) {
            $source = Join-Path 'apps/web' $item
            if (Test-Path $source) { Copy-Item -Recurse $source (Join-Path $web $item) }
        }
        foreach ($file in @(
                'package.json', 'package-lock.json', 'next.config.ts', 'tsconfig.json',
                'next-env.d.ts', 'eslint.config.mjs', '.prettierrc.json', '.prettierignore')) {
            $source = Join-Path 'apps/web' $file
            if (Test-Path $source) { Copy-Item $source (Join-Path $web $file) }
        }

        Copy-Item 'docs/DEPLOYMENT.md' (Join-Path $bundle 'README.md')
        Copy-Item 'CHANGELOG.md' (Join-Path $bundle 'CHANGELOG.md')

        # Windows filesystems carry no Unix execute bit, so an archive built here would unpack
        # with propflow-deploy non-executable. GNU tar's --mode stamps one into the archive;
        # Windows' bundled bsdtar rejects the option outright ("Option --mode=a+rx is not
        # supported"). Git for Windows ships GNU tar, so prefer that and say so loudly when only
        # bsdtar is available rather than shipping a bundle that cannot run.
        if (-not $SkipArchive) {
            $archive = Join-Path $output "$bundleName.tar.gz"
            if (Test-Path $archive) { Remove-Item -Force $archive }
            Write-Host "  archive -> $archive"

            $gnuTar = Get-GnuTar
            if ($gnuTar) {
                # Two GNU-tar-on-Windows quirks, both load-bearing:
                #   --force-local  : without it `C:\path` is parsed as a remote host spec and
                #                    fails with "Cannot connect to C: resolve failed".
                #   PATH           : --gzip shells out to gzip, which lives beside tar in Git's
                #                    usr\bin and is not on PATH when invoked from PowerShell.
                $previousPath = $env:PATH
                try {
                    $env:PATH = (Split-Path -Parent $gnuTar) + [IO.Path]::PathSeparator + $env:PATH
                    & $gnuTar --create --gzip --force-local --file $archive `
                        --directory $output --mode='a+rx' $bundleName
                }
                finally {
                    $env:PATH = $previousPath
                }
                if ($LASTEXITCODE -ne 0) { throw "tar failed for $bundleName." }
            }
            else {
                Write-Warning ("No GNU tar found; falling back to the bundled tar, which cannot " +
                    "set the execute bit. Whoever unpacks $bundleName.tar.gz must run: " +
                    "chmod +x propflow-deploy api/PropFlow.Api admin/PropFlow.Admin")
                tar --create --gzip --file $archive --directory $output $bundleName
                if ($LASTEXITCODE -ne 0) { throw "tar failed for $bundleName." }
            }
        }

        Write-Host "  done: $bundle" -ForegroundColor Green
    }

    Write-Host "`nAll bundles built." -ForegroundColor Green
}
finally {
    # The revert belongs here, not at the end of the try: a publish that throws half way through
    # has already dirtied the lock files, and leaving them that way is what makes the *next* run
    # unsafe. Runs on success and failure alike.
    if ($trackedLocks -and $trackedLocks.Count -gt 0) {
        Write-Host "`nReverting the per-RID sections the restore added to the lock files..." -ForegroundColor Cyan
        git checkout -- @trackedLocks
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'Could not revert the lock files. Check `git status` before shipping.'
        }
        else {
            # Prove it, rather than assume the checkout did what it should. They started clean,
            # so anything dirty now is a failure.
            $stillDirty = @(git status --porcelain -- '*packages.lock.json' |
                ForEach-Object { $_.Substring(3).Trim('"') })
            if ($stillDirty.Count -gt 0) {
                Write-Warning "These lock files are still modified after the revert:`n  $($stillDirty -join "`n  ")"
            }
            else {
                Write-Host 'Lock files are back to their committed state.' -ForegroundColor Green
            }
        }
    }
    Pop-Location
}
