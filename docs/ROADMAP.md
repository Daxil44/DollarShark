# 🦈 DollarShark roadmap

One path from today to v1 (friends can use it) and beyond. Work top to bottom; move on when a stage's **Done when** is true. Tick boxes as items land.

Hosting choice: **AWS Lightsail + Docker Compose + SQL Server Express container + Nginx**, S3 for files and backups, GitHub Actions for CI/CD. Keep costs low: set an AWS budget alert before creating anything.

---

## ✅ Stage 0 — Already done

- [x] Phase 1 — Project foundation, database, API, frontend
- [x] Phase 2 — Accounts, categories, transactions, filters
- [x] Phase 2.5 — Authentication and multi-user support
- [x] 3.1 — Document upload infrastructure
- [x] 3.2 — CSV statement imports
- [x] 3.3 — Import preview and validation
- [x] 3.4 — Duplicate detection
- [x] 3.4.2 — Scotiabank and Amex CSV compatibility
- [x] Dashboards: opening balances, linked transfers, account dashboards, balance history, consolidated dashboard, financial insights
- [x] UI polish: landing page, sign-in/sign-up redesign, tabbed dashboard
- [x] Email confirmation and password reset (SMTP / Gmail, dev log mode)
- [x] Continue with Google (code ready; Google Cloud keys added at deployment)
- [x] Rate limiting on sign-in and email endpoints

---

## 🚀 Road to v1

### Stage 1 — Launch hardening (code)

**Security and reliability**
- [x] Production HTTPS and hosting setup: HTTPS redirect + HSTS, persisted login-cookie (data-protection) keys, forwarded headers for running behind Nginx/a load balancer, exact `FrontendOrigin`
- [x] Global error handling: `UseExceptionHandler` + ProblemDetails on the API (no stack traces); React error boundary so one failing chart doesn't blank the page
- [x] Rate limit on statement upload
- [ ] Delete my account: remove the user's data and stored statement files (PIPEDA)

**Product**
- [x] Auto-categorization rules (e.g. "description contains FRESHMART → Groceries"), applied on import
- [x] First-run guide: add account → upload statement → review

**Quality**
- [ ] CI pipeline: GitHub Action running `dotnet test`, frontend build and lint on every PR
- [ ] Update the docs: README "out of scope" list, `architecture.md` (multi-user + auth), short deployment guide

Suggested PRs: (1) production setup + error handling + upload limit → (2) CI → (3) delete account → (4) auto-categorization + first-run guide → (5) docs.

**Done when:** all tests pass, CI is green, and the PRs are merged.

### Stage 2 — Deployment readiness audit *(hosting Phase 0)*

- [ ] Inspect repository, database, authentication, configuration and deployment requirements — written report, no code changes

**Done when:** there is a checklist of everything Docker and AWS will need.

### Stage 3 — Dockerize DollarShark *(hosting Phase 1)*

- [ ] Production Dockerfile for the ASP.NET Core API
- [ ] Nginx image serving the built React app and proxying `/api` to the API (same origin)
- [ ] SQL Server Express container (`MSSQL_PID=Express`)
- [ ] Docker Compose with persistent volumes: database, statement files, data-protection keys
- [ ] Database migration step as part of deployment
- [ ] Test the complete application locally

**Done when:** `docker compose up` runs the whole app locally.

### Stage 4 — Production configuration and security *(hosting Phase 2)*

- [ ] Separate development and production settings
- [ ] Environment variables and secrets (DB password, SMTP, Google keys) — never in git
- [ ] Frontend production build uses same-origin API URLs
- [ ] Verify secure cookies, CSRF and CORS in production mode
- [ ] SQL Server not publicly exposed (no published port 1433)
- [ ] Verify authentication and multi-user isolation

**Done when:** the Docker stack runs in Production mode and two accounts cannot see each other's data.

### Stage 5 — AWS fundamentals and Lightsail *(hosting Phase 3)*

- [ ] Create AWS account and a billing budget alert (e.g. $5/month)
- [ ] Understand IAM; use an IAM user/role instead of the root account
- [ ] Create a Lightsail Ubuntu instance with at least 2 GB RAM (SQL Server needs it; 4 GB is comfortable)
- [ ] Static IP, SSH key access, firewall (only 22, 80, 443)
- [ ] Basic Linux server administration
- [ ] Install Docker and Docker Compose

**Done when:** you can SSH in and run `docker --version`.

### Stage 6 — Deploy DollarShark *(hosting Phase 4)*

- [ ] Deploy the containers
- [ ] Configure Nginx
- [ ] Domain name and HTTPS (Let's Encrypt)
- [ ] Email provider: Gmail App Password or Amazon SES
- [ ] Turn on Continue with Google with the production redirect URI
- [ ] Test registration, email confirmation, login and transactions

**Done when:** friends can open `https://<your-domain>`, sign up, confirm and add transactions.

### Stage 7 — AWS S3 and backups *(hosting Phase 5)*

- [ ] Private S3 bucket with least-privilege IAM permissions
- [ ] Store statement uploads in S3 (new `IStatementStorage` implementation)
- [ ] Encrypted, scheduled database backups to S3
- [ ] Test database restoration

**Done when:** a test database has been deleted and restored from backup.

### Stage 8 — Continuous deployment *(hosting Phase 6)*

- [ ] Build Docker images automatically
- [ ] Push images to a registry
- [ ] Deploy changes to Lightsail
- [ ] Deployment health checks (`/health`)
- [ ] Rollback procedure

**Done when:** merging to `main` deploys automatically and a rollback has been practised.

### Stage 9 — Production verification and launch 🎉 *(hosting Phase 7)*

- [ ] Test two or more separate user accounts
- [ ] Test file upload and access permissions
- [ ] Test data persistence after container restart
- [ ] Test backup recovery
- [ ] Review logs, resource usage and security

**Done when:** everything passes — invite friends.

---

## 📈 After v1

### Stage 10 — Quick wins
- [ ] Export to CSV
- [ ] Smaller page download (load chart code only on the Dashboard)
- [ ] Production logging without SQL text or personal data

### Stage 11 — Smarter imports
- [ ] More statement formats: TD, RBC, CIBC, BMO
- [ ] "Map my columns" screen for unknown CSVs
- [ ] XLSX statement parsing

### Stage 12 — Receipts *(3.5)*
- [ ] Upload, view and organize receipts (stored in S3)

### Stage 13 — PDF statements *(3.6 + 3.7)*
- [ ] Extract transactions from text-based PDFs
- [ ] Support different banks and credit cards

### Stage 14 — Receipt intelligence *(3.8 + 3.9)*
- [ ] Extract merchant, date, taxes and total
- [ ] Match receipts to bank transactions

### Stage 15 — OCR for scanned documents *(3.10)*
- [ ] Handle scanned statements and receipts (e.g. Amazon Textract — billed per page, set a usage cap)

### Stage 16 — Money insights
- [ ] Monthly budgets per category with progress bars
- [ ] Recurring charge and subscription detection
- [ ] Edit many transactions at once

### Stage 17 — Telegram integration *(original Phase 5)*
- [ ] Telegram bot for balances and quick expense entry (good place to learn AWS Lambda)

### Stage 18 — Polish and extra security
- [ ] Dark mode
- [ ] Two-factor login (authenticator apps) — move earlier if many users join
