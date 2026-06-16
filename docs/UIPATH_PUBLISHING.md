# Publishing the Akeyless UiPath integration publicly

UiPath Orchestrator **credential store plugins are not installed via UiPath Marketplace** like Studio activities. Customers download the DLL from your repository (or a release artifact) and deploy it to their Orchestrator `Plugins` folder.

Public visibility therefore comes from **three complementary channels**:

1. **GitHub / release artifacts** — open-source distribution (this repository)
2. **UiPath Technology Partner Program** — partner listing, co-marketing, internal sales catalog
3. **UiPath Marketplace listing** — discoverability as a Technology Partner solution (documentation + connector listing, not a one-click Orchestrator plugin install)

## What this integration is (set expectations)

| Channel | What gets listed | Install model |
| --- | --- | --- |
| **GitHub releases** | `UiPath.Akeyless.SecureStore.dll` + docs | Manual copy to `Orchestrator\Plugins` |
| **UiPath Marketplace** | Solution/integration listing with docs link | Points to GitHub; customer self-deploys DLL |
| **UiPath Partner Locator** | Akeyless company + integration description | Sales/discovery; links to docs |
| **Built-in Orchestrator store** | Azure KV, CyberArk, etc. | Requires UiPath engineering partnership (not typical for community plugins) |

The Akeyless plugin follows the same model as other third-party credential stores (for example Delinea Secret Server’s `SecretServer.SecureStore.dll`): **partner listing + customer deployment**, not Marketplace package install.

---

## Phase 1 — Publish the open-source repository (you are here)

### Repository checklist

- [x] Source code and MIT license
- [x] README with build/install instructions
- [x] [INTEGRATION.md](INTEGRATION.md) — configuration guide
- [x] [MIGRATION.md](MIGRATION.md) — migration guide
- [x] GitHub Actions build + test workflow
- [x] Release workflow attaching packaged DLL
- [ ] GitHub release with version tag (e.g. `v1.0.0`)
- [ ] Repository description and topics: `uipath`, `orchestrator`, `akeyless`, `credential-store`, `secrets-management`

### Create a GitHub release

```bash
git tag v1.0.0
git push origin v1.0.0
```

In GitHub → **Releases → Draft a new release**, publish the tag. The workflow uploads `uipath-akeyless-securestore-v1.0.0.zip` containing:

- `UiPath.Akeyless.SecureStore.dll`
- `docs/`
- `config/plugins.config.sample.xml`

### Customer-facing assets to add (recommended)

| Asset | Purpose |
| --- | --- |
| Logo (PNG/SVG) | Marketplace + Partner Locator |
| Architecture diagram | Listing page |
| 2–3 minute demo video | Marketplace curation |
| Support contact | Listing requirements |

---

## Phase 2 — Join the UiPath Technology Partner Program

This is the primary path to appear in **UiPath’s partner records** (Partner Locator, internal sales catalog, co-marketing).

### Apply

1. Go to [Become a UiPath Technology Partner](https://www.uipath.com/partners/become-a-partner/technology)
2. Complete the application with business contact details
3. Execute the Technology Partner Agreement when provided
4. Email **techpartners@uipath.com** with:
   - Link to this repository
   - Integration type: **Orchestrator Credential Store Plugin**
   - Supported deployments: Standalone Orchestrator, Automation Suite
   - Auth methods: AccessKey, JWT, AWS IAM

### After acceptance

You receive access to the **Technology Partner Portal** where you can:

- Register your solution for internal UiPath sales/presales catalog
- Request **Partner Locator** listing (company logo + integration summary)
- Access certification and marketing resources

Reference: [UiPath Technology Partner Program](https://www.uipath.com/partners/become-a-partner/technology)

---

## Phase 3 — UiPath Marketplace listing

Marketplace is for **discoverability and documentation**, not Orchestrator plugin distribution.

### Prerequisites

1. Accepted **UiPath Technology Partner** (or register as **Independent Vendor** if no partnership yet — less visibility)
2. Business email and **Company Profile** on Marketplace

### Register company profile

1. Sign up at [UiPath Marketplace](https://marketplace.uipath.com/)
2. **Publisher Account → Create a Business Profile**
3. Set **Provider Type** to **UiPath Technology Partner**
4. Complete vendor verification details

Docs: [Register a business profile](https://docs.uipath.com/marketplace/automation-cloud/latest/user-guide/register-a-company-profile)

### Submit a listing

Create a new listing with:

| Field | Suggested content |
| --- | --- |
| **Title** | Akeyless Secrets Manager for UiPath Orchestrator |
| **Category** | Integration / IT & Security (closest fit) |
| **Description** | External credential store plugin for Orchestrator robot passwords and credential assets |
| **UiPath products** | Orchestrator (Standalone + Automation Suite) |
| **Documentation URL** | `https://github.com/akeyless-community/uipath-akeyless-integration/blob/main/docs/INTEGRATION.md` |
| **Download / install** | Link to GitHub Releases DLL + install steps |
| **Support** | Akeyless support or community issues URL |

Follow: [Introduction for Marketplace partners](https://docs.uipath.com/marketplace/automation-cloud/latest/user-guide/introduction) and [Publishing guidelines](https://docs.uipath.com/marketplace/automation-cloud/latest/user-guide/publishing-guidelines).

### Listing documentation requirements

UiPath expects a user guide covering:

- Prerequisites (Orchestrator version, credential stores enabled)
- Installation (DLL + `UiPath.Orchestrator.dll.config`)
- Configuration (API URL, auth, data path)
- Robot and asset secret formats
- Migration steps
- Troubleshooting

This repository’s `docs/INTEGRATION.md` and `docs/MIGRATION.md` satisfy most requirements.

### Curation timeline

All listings undergo **UiPath curation** (security + functionality review). Allow several weeks. Incomplete listings are rejected.

---

## Phase 4 — Automation Suite specific notes

For **Automation Suite** (Kubernetes/Linux), customers use `uipathctl` instead of copying DLLs manually:

```bash
./bin/uipathctl config orchestrator upload --securestore-plugins-directory /path/to/plugins
```

Plugins must be **Linux-compatible** (.NET Standard 2.0). Document this in your Marketplace listing.

Docs: [Configuring credential stores (Automation Suite)](https://docs.uipath.com/automation-suite/automation-suite/latest/installation-guide/configuring-credential-stores)

---

## Phase 5 — Optional: native built-in Orchestrator support

Built-in stores (Azure Key Vault, AWS Secrets Manager, CyberArk) are shipped inside Orchestrator by **UiPath engineering**. That requires a deep OEM partnership and UiPath source integration — not achievable via Marketplace alone.

For most vendors, the standard path is:

**Technology Partner Program + credential store plugin + Marketplace documentation listing**

---

## Recommended timeline

| Week | Action |
| --- | --- |
| 1 | Publish GitHub repo + `v1.0.0` release |
| 1–2 | Apply to UiPath Technology Partner Program |
| 2–3 | Submit Marketplace listing (after partner profile approved) |
| 3–4 | Provide logo, demo video, joint customer reference if available |
| 4+ | Curation + Partner Locator go-live |

---

## Contacts

| Purpose | Contact |
| --- | --- |
| UiPath Technology Partners | techpartners@uipath.com |
| Partner application | https://www.uipath.com/partners/become-a-partner/technology |
| Marketplace publisher docs | https://docs.uipath.com/marketplace/automation-cloud/latest/user-guide/introduction |
| Orchestrator credential stores | https://docs.uipath.com/orchestrator/standalone/latest/user-guide/about-credential-stores |
| Plugin development reference | https://github.com/UiPath/Orchestrator-CredentialStorePlugins |

---

## Internal Akeyless checklist before go-live

- [ ] Akeyless docs site links to this repository
- [ ] Solutions engineering runbook references migration guide
- [ ] Sample tenant + auth method for UiPath joint QA (if UiPath requests validation)
- [ ] Security review of plugin DLL (no secrets in logs, HTTPS, path traversal checks)
