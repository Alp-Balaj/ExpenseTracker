# Deploying ExpenseTracker (Railway, single origin)

One deployed app: the API container serves the built SPA from `wwwroot`, the controllers
already live under `/api/*` so the SPA calls them same-origin, and Railway Postgres is the
only database — user preferences moved into it in Suggestion-10. The image is built by
`.github/workflows/deploy.yml` from **both** repos and pushed to GHCR; the workflow then points
the Railway service at that run's exact tag (`:deploy-<run_id>`) and starts a new deployment
(`.github/scripts/railway-deploy.sh`). It does not use `railway redeploy`: that re-runs the
previous deployment's image and never picks up a new `:latest`.

```
expense-tracker-front push (master) ──repository_dispatch──▶ deploy.yml ◀── ExpenseTracker push (master)
                                                                 │ docker build (SPA + API)
                                                                 ▼
                                                 ghcr.io/<owner>/expensetracker:latest
                                                                 │ pin :deploy-<run_id> + deploy
                                                                 ▼
                          Browser ──▶ Railway service ──▶ Railway Postgres (migrated at startup)
```

Single origin is what makes the auth cookie work without ceremony: `refresh_token` is set
`HttpOnly; Secure; SameSite=None; Path=/api/User`, which browsers increasingly distrust
cross-site. Served from one host it is a plain first-party cookie.

## 1. One-time provisioning

### GitHub — the two cross-repo PATs

Each repo's workflow reaches into the *other* repo, so each holds a PAT scoped to its
counterpart. **A secret is only readable by workflows running in the repo that holds it**,
which is why the token for reading the frontend lives on the backend. The names say what
the token opens, not where it is stored — name the PAT in your GitHub account settings
identically to the secret and there is nothing left to mix up.

| Secret name | Lives on (where the workflow runs) | Scoped to (what it opens) | Permission | Used by |
|---|---|---|---|---|
| `READ_FRONTEND_REPO` | `ExpenseTracker` | `expense-tracker-front` | Contents: **Read** | `deploy.yml` → Checkout frontend |
| `DISPATCH_TO_BACKEND_REPO` | `expense-tracker-front` | `ExpenseTracker` | Contents: **Read and write** | `notify-deploy.yml` → repository_dispatch |

Read is enough for the checkout; the dispatch needs write because `POST /repos/…/dispatches`
is a mutating call. Fine-grained PATs default to "Public repositories" with no permissions —
both repos are private, so **Repository access** must explicitly select the target repo and
**Repository permissions → Contents** must be set, or you get
`403 / Write access to repository not granted` even for a read.

### GitHub (backend repo `ExpenseTracker`) — Railway secrets

| Secret | Value |
|---|---|
| `RAILWAY_TOKEN` | Railway **project token** (project → Settings → Tokens). Until this exists, deploy.yml builds+pushes the image but skips the redeploy step. |
| `RAILWAY_SERVICE_ID` | The Railway service name or ID to redeploy. |

### Setting a secret without mixing it up

`gh secret set` does not strip a trailing newline, and an empty paste stores an empty value
that fails as `could not read Username` (git) or `set the GH_TOKEN environment variable`
(gh) rather than as a permission error. This checks the value reached the shell and can
actually see the target repo before storing it:

```sh
read -rs TOKEN
echo "length: ${#TOKEN}"                       # must be non-zero
curl -s -o /dev/null -w "%{http_code}\n" \     # must be 200
  -H "Authorization: Bearer $TOKEN" \
  https://api.github.com/repos/Alp-Balaj/expense-tracker-front
printf '%s' "$TOKEN" | gh secret set READ_FRONTEND_REPO --repo Alp-Balaj/ExpenseTracker
unset TOKEN
```

### MongoDB Atlas — no longer used

Suggestion-10 moved `UserPreferences` into Postgres and deleted the Mongo client, connection
string and driver packages. Nothing in the app connects to Atlas any more, so **delete the
cluster (or at minimum its database user)** — that also retires the credential that leaked into
git history, which is the cleanest available fix for it.

### Railway

1. New project → add **Postgres**.
2. Add a service → **Deploy from image** → `ghcr.io/<owner>/expensetracker:latest`. The
   GHCR package is private by default, so give Railway registry credentials (GitHub
   username + a PAT with `read:packages`).
3. Service settings:
   - **Healthcheck path:** `/health`
   - **Public networking:** enable, port `8080`.
   - No pre-deploy command: `Program.cs` runs `db.Database.Migrate()` at startup, so a new
     instance migrates itself before serving. (This is the one deliberate difference from
     the RecipeApp pipeline, which ships a separate `efbundle` and runs it pre-deploy for
     zero-downtime migrations. See "Recorded debt" below.)
4. Service variables:

| Variable | Value |
|---|---|
| `ConnectionStrings__PostgreSQL` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Require` — keyword form via Railway reference variables. (`DependencyInjection.cs` also accepts a `postgresql://` URL and converts it, but that path drops the SSL setting.) |
| `Jwt__Key` | fresh random (e.g. `openssl rand -base64 48`) — never the dev key. `AddAuthServices` throws at startup if this is missing or under 32 bytes, so a bad value fails the healthcheck rather than shipping. |
| `Jwt__Issuer` | e.g. `https://<service>.up.railway.app` (validated, so it only has to match what the API itself issues) |
| `Jwt__Audience` | e.g. `https://<service>.up.railway.app` |
| `RefreshToken__Pepper` | fresh random secret. **Changing it invalidates every stored refresh token** (hashes stop matching), so set it once. |
| `RefreshToken__Longevity` | `14` (days; already the default in appsettings) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

`PORT` is injected by Railway and `Program.cs` binds `http://0.0.0.0:$PORT`.
`FRONTEND_ORIGIN` is no longer needed — CORS only matters for the local Vite dev server now.

## 2. Local production rehearsal

From the directory that contains both clones (`~/Documents/Projects`):

```sh
docker build -f ExpenseTracker/Dockerfile \
  --build-arg BACKEND_DIR=ExpenseTracker --build-arg FRONTEND_DIR=expense-tracker-front \
  -t expensetracker .

# fresh local db
docker run -d --name expensetracker-pg -e POSTGRES_PASSWORD=rehearsal \
  -e POSTGRES_DB=ExpenseTracker -p 5433:5432 postgres:16-alpine

docker run --rm --network host \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__PostgreSQL="Host=localhost;Port=5433;Database=ExpenseTracker;Username=postgres;Password=rehearsal" \
  -e Jwt__Key="rehearsal-only-signing-key-0123456789abcdef-0123456789abcdef" \
  -e Jwt__Issuer="ExpenseTracker" -e Jwt__Audience="ExpenseTrackerUsers" \
  -e RefreshToken__Pepper="rehearsal-only-pepper" \
  expensetracker
```

`docker compose up --build` from `ExpenseTracker/` does the same with the Postgres
container wired in.

Click-through on `http://localhost:8080` — **no Vite dev server running**:

- [ ] signup + login
- [ ] the `refresh_token` cookie is set and a token refresh succeeds (dev tools → Network,
      `POST /api/User/refresh`). Note: `Secure=true` means the cookie is dropped over plain
      `http://localhost:8080` — for the cookie leg of the rehearsal, test on the Railway URL
      or temporarily front the container with TLS. Everything else works over http.
- [ ] accounts / expenses / income / categories CRUD
- [ ] dashboard charts and reports render
- [ ] currency conversion
- [ ] user preferences round-trip (theme + base currency, now a Postgres table)
- [ ] deep-link refresh on a client route (e.g. `/dashboard`) serves the SPA rather than 404
- [ ] `/health` returns 200 without a token
- [ ] migrations applied cleanly to the fresh, empty DB (check the startup logs)

## 3. First deploy + smoke

1. Push `master` (or run the Deploy workflow manually) with all secrets in place.
2. Repeat the click-through on the public Railway URL, cookie leg included.
3. Push a trivial frontend commit to `master`: dispatch → rebuild → redeploy should run
   without touching the backend repo.

The Vercel project was deleted on 2026-07-25, so Railway is the only front door: the SPA is
served by the API itself and there is no second, cross-origin deployment to keep in sync.
Until the Railway service is up, the app is not hosted anywhere.

## Recorded debt (deliberately out of scope)

- `db.Database.Migrate()` at startup instead of a pre-deploy migration bundle: a long
  migration delays the healthcheck, and two instances starting at once both try to migrate.
  Move to the RecipeApp's `dotnet ef migrations bundle` + Railway pre-deploy command when
  that starts to hurt.
- No test projects in the solution, so CI only restores and builds.
- Secrets are out of the tracked appsettings files (user-secrets in dev, env vars in
  production). The previously committed Mongo Atlas credential is still in git history, but
  the app no longer uses Atlas at all — deleting the cluster/user retires it for good.
- No custom domain/CDN in front of Railway.
