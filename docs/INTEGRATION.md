# Akeyless integration with UiPath Orchestrator

This guide explains how to use Akeyless as an external credential store for UiPath Orchestrator, including configuration, RBAC, secret formats, and operational guidance.

## Overview

UiPath Orchestrator credential stores let you keep robot passwords and credential assets outside the Orchestrator database. Orchestrator loads third-party credential store plugins from the `Plugins` folder and exposes them in the tenant administration UI.

The Akeyless plugin provides two store types:

| Store type | Identifier | Behavior |
| --- | --- | --- |
| Akeyless | `Akeyless` | Read-write — Orchestrator creates and updates secrets in Akeyless |
| Akeyless (read-only) | `Akeyless (read-only)` | Read-only — secrets are created in Akeyless and only read by Orchestrator |

## Prerequisites

- UiPath Orchestrator with credential stores enabled for your tenant
- Network connectivity from the Orchestrator server to your Akeyless API or Gateway
- An Akeyless auth method (Access Key, JWT, or AWS IAM) with permissions on the Orchestrator data path
- .NET Standard 2.0-compatible Orchestrator installation (standard for current Orchestrator releases)

## Install the plugin

1. Build or obtain `UiPath.Akeyless.SecureStore.dll`.
2. Copy the DLL to `<OrchestratorInstallPath>\Plugins`.
3. Add the plugin to `UiPath.Orchestrator.dll.config`:

```xml
<add key="Plugins.SecureStores" value="UiPath.Akeyless.SecureStore.dll" />
```

4. Restart Orchestrator.
5. Verify the plugin appears when creating a new credential store.

## Create the credential store in Orchestrator

1. Open **Orchestrator → Tenant → Credential Stores**.
2. Click **Add Credential Store**.
3. Select **Akeyless** or **Akeyless (read-only)**.
4. Configure the fields:

| Field | Example | Notes |
| --- | --- | --- |
| Name | `Production Akeyless` | Display name in Orchestrator |
| API URL | `https://api.akeyless.io` | Or your Gateway URL |
| Authentication Type | `AccessKey` | Or `Jwt` or `AwsIam` |
| Access ID | `p-xxxxxxxx` | From your Akeyless auth method |
| Access Key | `••••••••` | Required for AccessKey auth |
| JWT Token | `••••••••` | Required for Jwt auth |
| Cloud ID | _(empty)_ | Optional for AwsIam; auto-detected on EC2 |
| AWS Region | `us-east-1` | For AwsIam STS signing |
| STS URL | `https://sts.amazonaws.com/` | For AwsIam |
| Data Path | `/applications/orchestrator` | Prefix for all secrets |

5. Click **Create**. Orchestrator validates connectivity (and read-write permissions for the read-write plugin).

## Akeyless setup

### 1. Choose a data path

Pick a dedicated folder prefix for Orchestrator secrets, for example:

- `/applications/orchestrator/robots` — unattended robot passwords
- `/applications/orchestrator/assets` — credential assets

You can use one data path for both by setting Data Path to `/applications/orchestrator` and letting Orchestrator append the external name.

### 2. Create an auth method

**Access Key (recommended for Orchestrator servers):**

```bash
akeyless create-auth-method --name /applications/orchestrator/uipath
```

Bind an access key and note the Access ID.

**JWT:**

Use an existing JWT/OIDC auth method and provide the Access ID plus bearer token in the credential store configuration.

**AWS IAM (recommended when Orchestrator runs on AWS EC2):**

```bash
akeyless create-auth-method --name /applications/orchestrator/uipath-aws \
  --aws-iam-auth true
```

Bind the auth method to the EC2 instance profile role ARN. In Orchestrator, set **Authentication Type** to `AwsIam`, provide **Access ID**, and leave **Cloud ID** empty to auto-detect credentials from the instance profile or `AWS_*` environment variables.

### 3. Grant RBAC

Example policy for read-only access to robot and asset secrets:

```bash
akeyless set-role --name orchestrator-readonly --rules '[
  {
    "path": "/applications/orchestrator/*",
    "capabilities": ["read"]
  }
]'
```

Example policy for read-write access:

```bash
akeyless set-role --name orchestrator-rw --rules '[
  {
    "path": "/applications/orchestrator/*",
    "capabilities": ["read", "create", "update", "delete"]
  }
]'
```

Associate the role with the Orchestrator auth method.

## Secret formats

### Unattended robot password (read-only)

Path:

`/data-path/{ExternalName}`

Example with Data Path `/applications/orchestrator/robots` and External Name `robot01`:

`/applications/orchestrator/robots/robot01`

Value:

```json
{"Value":"SuperSecretPassword"}
```

The `Value` field name is case-sensitive. A plain string secret value is also supported.

### Credential asset (read-only)

Path:

`/data-path/{ExternalName}`

Example with Data Path `/applications/orchestrator/assets` and External Name `SAPCredentials`:

`/applications/orchestrator/assets/SAPCredentials`

Value:

```json
{"Username":"SAP_USER","Password":"SAP_PASSWORD"}
```

`Username` and `Password` are case-sensitive.

### Read-write mode

When using the **Akeyless** plugin, Orchestrator stores robot passwords and credential assets in the same formats automatically. External Name (or asset name) determines the secret name under the configured Data Path.

## AWS IAM authentication

When Orchestrator runs on AWS EC2 (or another host with AWS instance metadata / `AWS_ACCESS_KEY_ID` env vars):

1. Create an Akeyless AWS IAM auth method bound to the host's IAM role.
2. Set **Authentication Type** to `AwsIam`.
3. Provide **Access ID** from the auth method.
4. Leave **Cloud ID** empty unless you need to supply a pre-generated cloud ID.
5. Set **AWS Region** and **STS URL** if not using defaults.

The plugin resolves AWS credentials from, in order:

1. Pre-configured **Cloud ID** (if set)
2. `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` / `AWS_SESSION_TOKEN` environment variables
3. EC2 instance metadata (IMDSv2)

## Link robots and assets

### Robots

1. Edit or create an unattended robot.
2. Set **Credential Store** to your Akeyless store.
3. Optionally set **External Name** to map to a pre-provisioned secret (read-only) or to control the secret name (read-write).

### Assets

1. Create a credential asset.
2. Choose your Akeyless credential store.
3. Set **External Name** when using per-robot values or a custom secret name.

## Validation behavior

When you create a credential store:

- **Read-only plugin** — authenticates to Akeyless and verifies credentials.
- **Read-write plugin** — additionally creates a temporary test secret under the data path, reads it back, and deletes it.

If validation fails, check Orchestrator logs, Akeyless audit events, and RBAC on the data path.

## Troubleshooting

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Plugin not listed in UI | DLL not registered or Orchestrator not restarted | Confirm `Plugins.SecureStores` and restart IIS |
| Invalid credentials | Wrong Access ID/Key or expired JWT | Re-test auth with `akeyless auth` |
| Secret not found | Wrong data path or external name | Verify secret path in Akeyless matches Data Path + External Name |
| Unauthorized on create | Read-only plugin or missing RBAC | Use read-write plugin and grant create/update/delete |
| Robot password parse error | Missing `Value` field | Provision secret with `{"Value":"..."}` |

## Architecture

```text
UiPath Orchestrator
  └─ Credential Store Plugin (UiPath.Akeyless.SecureStore.dll)
       └─ Akeyless REST API / Gateway
            └─ Static secrets under /applications/orchestrator/*
```

## References

- [UiPath credential stores](https://docs.uipath.com/orchestrator/standalone/latest/user-guide/about-credential-stores)
- [UiPath credential store plugin development](https://github.com/UiPath/Orchestrator-CredentialStorePlugins)
- [Akeyless REST API](https://docs.akeyless.io/)
- [Migration guide](MIGRATION.md)
- [UiPath publishing guide](UIPATH_PUBLISHING.md)
