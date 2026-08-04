# Cycle U · W0 — the gate that is not code

W0 blocks W1. It asked for three things: a named and willing group, a fresh backup-restore
drill, and the pot's currency and low-balance default. This is what each one became once the
group turned out to be simulated, and how to run the tooling that replaced the drill.

## What changed, and why

**There are no other people.** The only real user of this app is Alp, so the four members are
simulated: four ordinary accounts created through the real signup endpoint, all controlled by
one person. Consent is not a question you can ask a fixture.

This makes the cycle *smaller*, not bigger, and the effect is worth stating plainly because
several of the plan's decisions were sized against the opposite assumption:

- Nobody else's money is at stake. W3's two-phase cross-user write and the write-off — "a way
  for one member to move another member's money" — move only fabricated balances.
- The consent conversation, and the disclosure about open registration and a single
  administrator, are void. Nothing is deployed to other people.
- The plan's closing line, "live on Railway with real data — and, from W1 on, with somebody
  else's", is no longer true. It should be corrected rather than left standing.

**What simulation does not remove** is the need for four distinct accounts. Every wave from W1
on is verified by two logged-in users watching each other's actions, and the largest-remainder
tie-break needs a concrete member count. Both still come from W0.

**The backup drill splits in two.** Its data-safety purpose is genuinely void — the only real
data is Alp's, it is disposable, and starting over costs nothing. Its second purpose is not:
W1, W2 and W3 each require their migration to be rehearsed against "a scratch Postgres seeded
with pre-U data", and there was no such dataset anywhere. The local dev database sits at two
migrations from March with one user and zero rows; production is on Railway and, for a
simulated cohort, irrelevant.

So the drill is replaced by a harness that *builds* that dataset deterministically, from
migrations plus the seeder. Same hour, used four times instead of once.

## The decisions W1 needs

| | |
|---|---|
| Members | **4** — Alp, Mara, Tomas, Iris. Simulated; join order is the tie-break order. |
| Pot currency | **EUR**, fixed at creation. Matches `UserDataSeeder`'s `DefaultBaseCurrency` and `UserPreferences.BaseCurrency`, so every member's base equals the pot currency and no member sees the W4 "one figure in Liquid, another in the pot" mismatch. |
| Low-balance default | **€50** on a new pot. Low enough that W4's alert actually fires against seeded spending during a browser pass. |

A €10.00 expense split four ways is 2.50 each with no remainder; €10.01 is 2.51 / 2.50 / 2.50 /
2.50, the extra cent going to the earliest-joined member — Alp, in the cohort's fixed order.

## Where the data lives, and why not in the local install

The harness runs Postgres **in a container**, not in the local PostgreSQL 18 install. Two
things forced that, and both are worth recording so nobody re-tries them:

- The local `alp` role is not a superuser and has no `CREATEDB`, so the harness cannot create
  databases there. The `postgres` superuser password is lost.
- Schemas inside the existing `ExpenseTracker` database were tried as a workaround and **do not
  work**. With no default schema declared in the model, Npgsql's migration-history probe looks
  for `__EFMigrationsHistory` *unqualified*: it finds whichever one exists first, concludes
  history is present, then fails to read it through a different search path. Two Cycle U schemas
  could never coexist. Fixing it would mean a `HasDefaultSchema` model change — out of scope,
  and it would change production.

The container costs nothing extra: Docker is a hard requirement for this cycle anyway — the
backend integration tests are Testcontainers-based, so the 327/327 gate cannot pass without it —
and the image is the same `postgres:16-alpine` that `docker-compose.yml` already declares. The
local install is never touched.

| | |
|---|---|
| Container | `et-u-pg`, `postgres:16-alpine`, host port **5433** (clear of the local install's 5432, as `DEPLOY.md` already does) |
| `et_u_dev` | The cycle's dev database. Built once, carried through W1–W4 as the target of every browser pass. |
| `et_u_scratch` | Throwaway. Rebuilt at the pre-U baseline before each wave's migration rehearsal. |

Credentials inside the container are `postgres` / `rehearsal` — not a secret; it is reachable
only from this machine and holds nothing but fabricated data. Throw the whole thing away with
`docker rm -f et-u-pg`.

## One-time setup

Start Docker Desktop, then build the dev database:

```powershell
cd ExpenseTracker
.\scripts\u0-database.ps1 -Database et_u_dev

$env:ConnectionStrings__PostgreSQL = 'Host=localhost;Port=5433;Database=et_u_dev;Username=postgres;Password=rehearsal;'
$env:RateLimiting__Auth__PermitLimit = '100'   # the seeder makes 8+ auth calls in a burst
dotnet run --project ExpenseTracker.API --no-launch-profile --urls http://localhost:2222
```

Then, from a second terminal:

```powershell
.\scripts\u0-seed-cohort.ps1 -BaseUrl http://localhost:2222
```

Every member's password is `SimMember1!`. Each ends up with the seeded EUR catalogue (4
currencies, 11 categories, one `Main` account), two salary incomes, five ordinary expenses, one
split expense carrying an `ExpectedReimbursement` (Stream R), and one savings goal with a
contribution (Stream V) — so the fixture covers both streams that landed on 3 August.

Amounts differ per member deliberately: in a two-browser pass you can tell whose session you are
looking at from the dashboard alone.

## Per-wave migration rehearsal

Run this before merging W1, W2 and W3. It is the thing the restore drill was standing in for.

```powershell
# 1. Scratch database at the pre-U baseline, populated as if the cycle had not started.
#    The baseline as of 2026-08-04 is SavingsEnvelopes — the 13th and last migration on
#    master. Confirm with: cd ExpenseTracker.Infrastructure
#                          dotnet ef migrations list --context PostgresContext
.\scripts\u0-database.ps1 -Database et_u_scratch -Migration SavingsEnvelopes

# 2. Seed it (start the API against et_u_scratch first, as above), then stop the API.
.\scripts\u0-seed-cohort.ps1 -BaseUrl http://localhost:2222

# 3. Apply the wave's migration on top of populated tables — the case an empty database
#    never exercises.
cd ExpenseTracker.Infrastructure
$env:ET_MIGRATIONS_CONNECTIONSTRING = 'Host=localhost;Port=5433;Database=et_u_scratch;Username=postgres;Password=rehearsal;'
dotnet ef database update --context PostgresContext

# 4. The snapshot check the plan requires after every merge. W3's xmin concurrency token
#    emits no DDL at all, so this is the only thing that proves the snapshot caught up.
dotnet ef migrations has-pending-model-changes --context PostgresContext
```

Both the `--context` and the working directory matter: `PostgresFactory` loads
`appsettings.Migrations.json` with `optional: false` relative to the current directory, so
`dotnet ef` has to run from `ExpenseTracker.Infrastructure\`. It reads
`ET_MIGRATIONS_CONNECTIONSTRING` ahead of that file, which is what lets these scripts point it
at an arbitrary database without editing tracked config.

## Also true, and not W0's problem

- **Docker is not running.** W1's gates need it — the backend integration tests use
  Testcontainers, and the 327/327 baseline is unreachable without it. Start Docker Desktop
  before the first wave.
- **A live MongoDB Atlas credential is still in the API project's user-secrets**
  (`ConnectionStrings:MongoDbConnection`). `DEPLOY.md` records that Atlas is no longer used by
  anything and that the cluster or its database user should be deleted; the secret is the last
  live thing pointing at it. Unrelated to this cycle, worth an hour of someone's time.
