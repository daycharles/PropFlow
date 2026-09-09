param(
    [Parameter(Mandatory = $true)][string]$AdminEmail,
    [Parameter(Mandatory = $true)][string]$OrganizationName,
    [Parameter(Mandatory = $true)][System.Security.SecureString]$AdminPassword
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path -LiteralPath '.env')) {
    $adminBytes = New-Object byte[] 32
    $runtimeBytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($adminBytes); $generator.GetBytes($runtimeBytes) } finally { $generator.Dispose() }
    $adminSecret = ([BitConverter]::ToString($adminBytes)).Replace('-', '')
    $runtimeSecret = ([BitConverter]::ToString($runtimeBytes)).Replace('-', '')
    @("POSTGRES_DB=propflow", "POSTGRES_USER=propflow", "POSTGRES_PASSWORD=$adminSecret", "APP_DB_PASSWORD=$runtimeSecret") |
        Set-Content -LiteralPath '.env' -Encoding ascii
}
$values = @{}
foreach ($line in Get-Content -LiteralPath '.env') {
    if ($line -match '^([A-Z_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2] }
}
foreach ($key in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'APP_DB_PASSWORD')) {
    if (-not $values[$key] -or $values[$key] -match '^replace-') { throw "Set $key in .env first." }
}
# Restrict the local helper to simple values so connection-string syntax cannot be injected.
foreach ($key in $values.Keys) {
    if ($values[$key] -match '[;"\r\n]') { throw "Unsupported characters in $key. Use alphanumeric local credentials." }
}
try {
    Invoke-Checked 'docker' @('compose', 'up', '-d', '--wait', '--wait-timeout', '60', 'database')
    $env:ConnectionStrings__Admin = "Host=localhost;Database=$($values.POSTGRES_DB);Username=$($values.POSTGRES_USER);Password=$($values.POSTGRES_PASSWORD)"
    $env:Runtime__Password = $values.APP_DB_PASSWORD
    Invoke-Checked 'dotnet' @('run', '--project', 'tools/PropFlow.Admin', '--', 'migrate')
    Invoke-Checked 'dotnet' @('run', '--project', 'tools/PropFlow.Admin', '--', 'configure-runtime')
    $env:Bootstrap__Organization = $OrganizationName
    $env:Bootstrap__Email = $AdminEmail
    $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $AdminPassword).Password
    Invoke-Checked 'dotnet' @('run', '--project', 'tools/PropFlow.Admin', '--', 'bootstrap')
    $env:ConnectionStrings__Database = "Host=localhost;Database=$($values.POSTGRES_DB);Username=propflow_app;Password=$($values.APP_DB_PASSWORD)"
    Write-Output 'Local setup complete. Copy the organization ID above for login. The runtime connection is set in this PowerShell session.'
} finally {
    Remove-Item Env:ConnectionStrings__Admin,Env:Runtime__Password,Env:Bootstrap__Password,Env:Bootstrap__Email,Env:Bootstrap__Organization -ErrorAction SilentlyContinue
}
