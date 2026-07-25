# Deploying ExpenseTracker (Railway, single origin)

One deployed app: the API container serves the built SPA from `wwwroot`, the controllers
already live under `/api/*` so the SPA calls them same-origin, the relational database is
Railway Postgres, and user preferences stay in MongoDB Atlas. The image is built by
`.github/workflows/deploy.yml` from **both** repos and pushed to GHCR; Railway redeploys
from `ghcr.io/<owner>/expensetracker:latest`.

```
expense-tracker-front push (master) ──repository_dispatch──▶ deploy.yml ◀── ExpenseTracker push (master)
                                                                 │ docker build (SPA + API)
                                                                 ▼
                                                 ghcr.io/<owner>/expensetracker:latest
                                                                 │ railway redeploy
                                                                 ▼
                          Browser ──▶ Railway service ──▶ Railway Postgres (migrated at startup)
                                            └───────────▶ MongoDB Atlas (UserPreferences)
```

Single origin is what makes the auth cookie work without ceremony: `refresh_token` is set
`HttpOnly; Secure; SameSite=None; Path=/api/User`, which browsers increasingly distrust
cross-site. Served from one host it is a plain first-party cookie.

## 1. One-time provisioning

### GitHub (backend repo `ExpenseTracker`)

Actions secrets:

| Secret | Value |
|---|---|
| `FRONTEND_REPO_TOKEN` | Fine-grained PAT, `expense-tracker-front` only, Contents: read. Lets deploy.yml check out the frontend. |
| `RAILWAY_TOKEN` | Railway **project token** (project → Settings → Tokens). Until this exists, deploy.yml builds+pushes the image but skips the redeploy step. |
| `RAILWAY_SERVICE_ID` | The Railway service name or ID to redeploy. |

### GitHub (frontend repo `expense-tracker-front`)

Actions secret `BACKEND_DISPATCH_TOKEN`: fine-grained PAT, `ExpenseTracker` only,
Contents: read-write (repository_dispatch requires write). The workflow itself
(`.github/workflows/notify-deploy.yml`) is already committed in that repo.

### MongoDB Atlas

The existing cluster is fine. Add Railway's egress to the Atlas IP access list — Railway
does not publish static egress IPs on the trial/hobby plans, so in practice this means
`0.0.0.0/0` plus a strong database user, or a dedicated egress add-on. The tracked
appsettings files no longer carry the credential, but the old one is still in git history —
rotate it before going live.

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
| `DatabaseProvider` | `PostgreSQL` |
| `ConnectionStrings__PostgreSQL` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Require` — keyword form via Railway reference variables. (`DependencyInjection.cs` also accepts a `postgresql://` URL and converts it, but that path drops the SSL setting.) |
| `ConnectionStrings__MongoDbConnection` | the Atlas `mongodb+srv://…` string |
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
  -e DatabaseProvider=PostgreSQL \
  -e ConnectionStrings__PostgreSQL="Host=localhost;Port=5433;Database=ExpenseTracker;Username=postgres;Password=rehearsal" \
  -e ConnectionStrings__MongoDbConnection="<atlas connection string>" \
  -e Jwt__Key="rehearsal-only-signing-key-0123456789abcdef-0123456789abcdef" \
  -e Jwt__Issuer="ExpenseTracker" -e Jwt__Audience="ExpenseTrackerUsers" \
  -e RefreshToken__Pepper="rehearsal-only-pepper" \
  expensetracker
```

`docker compose up --build` from `ExpenseTracker/` does the same with the Postgres
container wired in (set `MONGO_CONNECTION` in the shell first).

Click-through on `http://localhost:8080` — **no Vite dev server running**:

- [ ] signup + login
- [ ] the `refresh_token` cookie is set and a token refresh succeeds (dev tools → Network,
      `POST /api/User/refresh`). Note: `Secure=true` means the cookie is dropped over plain
      `http://localhost:8080` — for the cookie leg of the rehearsal, test on the Railway URL
      or temporarily front the container with TLS. Everything else works over http.
- [ ] accounts / expenses / income / categories CRUD
- [ ] dashboard charts and reports render
- [ ] currency conversion
- [ ] user preferences round-trip (this is the MongoDB path)
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
  production), but the previously committed Mongo Atlas credential remains in git history
  and still needs rotating.
- No custom domain/CDN in front of Railway.
