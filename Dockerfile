# syntax=docker/dockerfile:1
# Single-origin production image: the API serves the built SPA from wwwroot, so one Railway
# service answers both the app and /api/* — no CORS, no cross-site cookies.
#
# The build context is a PARENT directory holding BOTH repos — this file only references
# the context through the two ARGs below:
#   CI (deploy.yml):  checks out backend/ + frontend/ and builds with the defaults.
#   Local rehearsal:  from the folder containing both clones:
#     docker build -f ExpenseTracker/Dockerfile \
#       --build-arg BACKEND_DIR=ExpenseTracker --build-arg FRONTEND_DIR=expense-tracker-front \
#       -t expensetracker .
ARG BACKEND_DIR=backend
ARG FRONTEND_DIR=frontend

FROM node:22-alpine AS frontend-build
ARG FRONTEND_DIR
WORKDIR /src
COPY ${FRONTEND_DIR}/package.json ${FRONTEND_DIR}/package-lock.json ./
# The lockfile is written by npm 11 on the dev machines; the image ships npm 10, whose
# ci mishandles npm-11 locks (nested platform optionals install as required →
# EBADPLATFORM). Same npm major as the lock author = deterministic installs.
RUN npm install -g npm@11 && npm ci
COPY ${FRONTEND_DIR}/ ./
# vite.config.ts sets outDir: "build" (not dist), and only reads the local mkcert certs in
# `serve` mode — a production build needs no *.pem. .env.production pins the API base URL
# to same-origin.
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
ARG BACKEND_DIR
WORKDIR /src
COPY ${BACKEND_DIR}/ ./
RUN dotnet restore ExpenseTracker.API/ExpenseTracker.API.csproj
RUN dotnet publish ExpenseTracker.API/ExpenseTracker.API.csproj \
    -c Release -o /out/app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=backend-build /out/app ./
COPY --from=frontend-build /src/build ./wwwroot
# Program.cs binds http://0.0.0.0:$PORT (Railway injects PORT); this is the local default.
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseTracker.API.dll"]
