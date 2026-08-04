<#
.SYNOPSIS
    Builds a disposable Postgres database at a chosen migration point, for Cycle U's
    migration rehearsals and two-account browser passes.

.DESCRIPTION
    Replaces the backup-restore drill W0 originally asked for. That drill had two
    purposes: proving the backup restores, and producing "a scratch Postgres seeded
    with pre-U data" for the four migration rehearsals W1-W3 need. The first purpose
    is void — the only real account is Alp's and it is disposable — but the second is
    not, and there is no local dataset to rehearse against at all: the local dev
    database sits at two migrations from March with one user and zero rows.

    So this creates one, deterministically, from migrations plus u0-seed-cohort.ps1.

    IN A CONTAINER, NOT THE LOCAL INSTALL. The local Postgres 18 install's 'alp' role
    is not a superuser and has no CREATEDB, so the harness cannot make databases
    there, and the superuser password is lost. Schemas were tried and rejected: with
    no default schema in the model, Npgsql's migration-history probe looks for
    __EFMigrationsHistory *unqualified*, finds whichever one exists first, and then
    fails to read it through a different search path — so two schemas cannot coexist.

    A container sidesteps all of it. Docker is a hard requirement for this cycle
    anyway (the backend integration tests are Testcontainers-based, and the 327/327
    baseline is unreachable without it), the image is the same postgres:16-alpine
    docker-compose.yml already declares, and the whole thing is thrown away with
    `docker rm -f et-u-pg`. The local install is never touched.

    Two databases inside that container, both disposable:

      et_u_dev      the cycle's dev database. Migrated to master + cohort-seeded once,
                    then carried through W1-W4 as the target of every browser pass.
      et_u_scratch  throwaway. Recreated at the pre-U baseline before each wave's
                    migration rehearsal, then thrown away again.

    The design-time factory (PostgresFactory.cs) reads ET_MIGRATIONS_CONNECTIONSTRING
    ahead of appsettings.Migrations.json, which is what lets this point `dotnet ef` at
    an arbitrary database without editing tracked config. It also loads
    appsettings.Migrations.json with optional:false relative to the *current
    directory*, so the ef invocation below runs from ExpenseTracker.Infrastructure\.

.EXAMPLE
    # The cycle's dev database, at the tip of whatever branch is checked out.
    .\u0-database.ps1 -Database et_u_dev

.EXAMPLE
    # A scratch database at the pre-U baseline, ready for a wave's migration to be
    # applied on top of populated tables.
    .\u0-database.ps1 -Database et_u_scratch -Migration SavingsEnvelopes

.EXAMPLE
    # What migrations exist, newest last:
    #   cd ..\ExpenseTracker.Infrastructure
    #   dotnet ef migrations list --context PostgresContext
#>
[CmdletBinding()]
param(
    # Database to (re)create inside the container.
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z_][a-z0-9_]*$')]
    [string]$Database,

    # Migration to stop at. Omitted = every migration on the current branch.
    # Pass the bare name (SavingsEnvelopes), not the timestamped id.
    [string]$Migration,

    # Host port the container publishes on. 5433 keeps it clear of the local
    # install's 5432, exactly as docker-compose.yml and DEPLOY.md already do.
    [int]$Port = 5433,

    [string]$ContainerName = 'et-u-pg',
    [string]$Image = 'postgres:16-alpine',

    # Container-local credentials. Not a secret: this database is reachable only
    # from this machine and holds nothing but fabricated data.
    [string]$Username = 'postgres',
    [string]$Password = 'rehearsal',

    # Migrate an existing database in place rather than dropping it first.
    [switch]$KeepExisting
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraProject = Join-Path $repoRoot 'ExpenseTracker.Infrastructure'

if ($Database -eq 'postgres') { throw "Refusing to drop the container's maintenance database." }

# ── the container ─────────────────────────────────────────────────────────────

& docker version --format '{{.Server.Version}}' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw @"
Docker is not running. Start Docker Desktop and wait for the whale icon to settle,
then re-run this script.

Docker is required for this cycle regardless of this harness: the backend
integration tests use Testcontainers, so the 327/327 gate cannot pass without it.
"@
}

$state = (& docker inspect --format '{{.State.Status}}' $ContainerName 2>$null)
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating container '$ContainerName' ($Image) on port $Port…" -ForegroundColor Cyan
    & docker run -d --name $ContainerName -e "POSTGRES_PASSWORD=$Password" -p "${Port}:5432" $Image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker run failed." }
}
elseif ($state -ne 'running') {
    Write-Host "Starting container '$ContainerName'…" -ForegroundColor Cyan
    & docker start $ContainerName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker start failed." }
}

# Postgres accepts TCP connections a moment before it is ready to serve them, so
# poll rather than sleeping a guessed interval.
Write-Host "Waiting for Postgres to accept connections…" -ForegroundColor DarkGray
$ready = $false
foreach ($attempt in 1..60) {
    & docker exec $ContainerName pg_isready -U $Username -q 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $ready) { throw "Postgres in '$ContainerName' did not become ready within 60s." }

$connectionString = "Host=localhost;Port=$Port;Database=$Database;Username=$Username;Password=$Password;"

function Invoke-Psql {
    param([string]$Db, [string]$Sql)
    $out = & docker exec -e "PGPASSWORD=$Password" $ContainerName psql -U $Username -d $Db -v ON_ERROR_STOP=1 -tAc $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed against '$Db':`n$out" }
    return $out
}

# ── (re)create ────────────────────────────────────────────────────────────────

$exists = (Invoke-Psql -Db 'postgres' -Sql "select 1 from pg_database where datname = '$Database';") -eq '1'

if ($exists -and -not $KeepExisting) {
    Write-Host "Dropping '$Database'…" -ForegroundColor Yellow
    # Idle sessions from a previous API run hold the database open and make DROP
    # fail with 'is being accessed by other users'.
    Invoke-Psql -Db 'postgres' -Sql "select pg_terminate_backend(pid) from pg_stat_activity where datname = '$Database' and pid <> pg_backend_pid();" | Out-Null
    Invoke-Psql -Db 'postgres' -Sql "drop database ""$Database"";" | Out-Null
    $exists = $false
}

if (-not $exists) {
    Write-Host "Creating '$Database'…" -ForegroundColor Cyan
    Invoke-Psql -Db 'postgres' -Sql "create database ""$Database"";" | Out-Null
}

# ── migrate ───────────────────────────────────────────────────────────────────

Write-Host "Applying migrations$(if ($Migration) { " up to $Migration" })…" -ForegroundColor Cyan

$previous = $env:ET_MIGRATIONS_CONNECTIONSTRING
$env:ET_MIGRATIONS_CONNECTIONSTRING = $connectionString
Push-Location $infraProject
try {
    $efArgs = @('ef', 'database', 'update')
    if ($Migration) { $efArgs += $Migration }
    $efArgs += @('--context', 'PostgresContext')

    & dotnet @efArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed (exit $LASTEXITCODE)." }
}
finally {
    Pop-Location
    $env:ET_MIGRATIONS_CONNECTIONSTRING = $previous
}

# ── report ────────────────────────────────────────────────────────────────────

$applied = Invoke-Psql -Db $Database -Sql 'select count(*) from "__EFMigrationsHistory";'
$head = Invoke-Psql -Db $Database -Sql 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1;'
$tables = Invoke-Psql -Db $Database -Sql "select count(*) from information_schema.tables where table_schema = 'public';"

Write-Host ""
Write-Host "  $Database ready — $applied migrations, $tables tables, head $head" -ForegroundColor Green
Write-Host ""
Write-Host "  Run the API against it with:" -ForegroundColor DarkGray
Write-Host "    `$env:ConnectionStrings__PostgreSQL = '$connectionString'" -ForegroundColor DarkGray
Write-Host "    `$env:RateLimiting__Auth__PermitLimit = '100'   # the seeder makes 8+ auth calls" -ForegroundColor DarkGray
Write-Host "    dotnet run --project ExpenseTracker.API" -ForegroundColor DarkGray
Write-Host ""
