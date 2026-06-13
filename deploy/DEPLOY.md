# Deployment

The stack: a single **Hetzner Cloud** server provisioned by **Pulumi**, running the app + a
**Caddy** reverse proxy via **Docker Compose**. Images are built and shipped by **GitHub Actions**
to **GHCR**; updates are pulled onto the server over SSH.

```
GitHub push ──▶ CI (test) ──▶ Deploy workflow
                                 ├─ buildx → GHCR (multi-arch image)
                                 └─ ssh → server: git pull + docker compose pull + up -d
Pulumi (day-0) ──▶ Hetzner server + firewall + cloud-init (docker, clone, compose up)
Server: Caddy :443/:80  ──▶  app :8080  ──▶  SQLite (docker volume)
```

## Components
| Path | Purpose |
|---|---|
| `Dockerfile` | Multi-stage build of the ASP.NET app → `aspnet:10.0` runtime on `:8080` |
| `deploy/docker-compose.yml` | `app` (from GHCR) + `caddy`; persistent volumes for the DB and certs |
| `deploy/Caddyfile` | Reverse proxy + auto-HTTPS (domain) or HTTP (IP), WebSocket passthrough |
| `.github/workflows/ci.yml` | `dotnet test` on every push/PR |
| `.github/workflows/deploy.yml` | Build+push multi-arch image to GHCR, then SSH pull+restart |
| `infra/` | Pulumi (C#) — Hetzner server, firewall, cloud-init bootstrap |

## One-time setup

### 1. Make the GHCR image public (simplest) — or use a pull token
After the first Deploy run creates the package, set
`github.com/users/sossenbinder/packages/container/osrsarbitrage` → **Package settings → Visibility → Public**.
(Private alternative: create a read-only PAT and `docker login ghcr.io` in cloud-init.)

### 2. Provision the server with Pulumi
```bash
cd infra
pulumi stack init prod
pulumi config set hcloud:token <YOUR_HETZNER_API_TOKEN> --secret
pulumi config set osrs-infra:sshPublicKey "$(cat ~/.ssh/id_ed25519.pub)"
pulumi config set osrs-infra:domain arb.example.com   # or leave unset for HTTP on the IP
pulumi up
```
Outputs `serverIp`, `url`, `sshCommand`. cloud-init installs Docker, clones the repo, and runs
`docker compose up` automatically (first boot takes a couple of minutes).

### 3. Point DNS (only if using a domain)
Create an **A record** `arb.example.com → <serverIp>`. Caddy fetches a Let's Encrypt cert
automatically once it resolves (it retries until DNS propagates).

### 4. Wire up CI/CD secrets
In the GitHub repo → **Settings → Secrets and variables → Actions**:
| Secret | Value |
|---|---|
| `SSH_HOST` | the `serverIp` from Pulumi |
| `SSH_USER` | `root` |
| `SSH_KEY` | the **private** key matching the public key you gave Pulumi |

`GITHUB_TOKEN` is automatic (used to push to GHCR).

## Day-to-day
- **Push to `main`** → CI tests, the image rebuilds and ships, the server pulls and restarts.
- **Change infra** (server size, domain) → `pulumi up` in `infra/`.
- **Logs**: `ssh root@<ip> 'cd /opt/app/deploy && docker compose logs -f --tail=100'`.
- **DB persists** in the `arbdata` Docker volume across deploys; it's also re-derivable from the API.

## Notes
- The DB volume survives container/image updates but **not** a server destroy. It's mostly
  re-derivable (state rebuilds from the API on boot), so snapshots are optional.
- Default server is `cx33` (x86). The image is built multi-arch (amd64 + arm64), so you can
  switch to a cheaper ARM box (`cax11`/`cax21`) anytime via `osrs-infra:serverType` — no rebuild
  needed. The CX line is EU-only (nbg1/fsn1/hel1).
