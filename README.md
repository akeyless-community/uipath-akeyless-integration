# UiPath Orchestrator Akeyless Credential Store Plugin

Credential store plugins for [UiPath Orchestrator](https://www.uipath.com/product/orchestrator) that use [Akeyless](https://www.akeyless.io) as an external secrets backend for robot credentials and credential assets.

This follows the same Orchestrator credential store model used by Azure Key Vault, AWS Secrets Manager, and CyberArk: administrators configure a tenant-level credential store, then robots and assets resolve secrets from Akeyless at runtime.

**Repository:** `uipath-akeyless-plugin`

## Plugins

| Plugin class | Orchestrator type | Mode |
| --- | --- | --- |
| `AkeylessSecureStore` | **Akeyless** | Read-write — Orchestrator can create, read, update, and delete secrets in Akeyless |
| `AkeylessReadOnlySecureStore` | **Akeyless (read-only)** | Read-only — secrets are provisioned in Akeyless and only read by Orchestrator |

Both plugins ship in a single assembly: `UiPath.Akeyless.SecureStore.dll`.

## Build

Requirements:

- .NET SDK 8+ (to build and test)
- Target output: .NET Standard 2.0 (compatible with Orchestrator credential store plugins)

```bash
cd uipath-akeyless-plugin
dotnet restore
dotnet build -c Release
dotnet test
```

The plugin DLL is written to:

`src/UiPath.Akeyless.SecureStore/bin/Release/UiPath.Akeyless.SecureStore.dll`

## Install on Orchestrator

1. Copy `UiPath.Akeyless.SecureStore.dll` to the Orchestrator `Plugins` folder (for example `C:\Program Files (x86)\UiPath\Orchestrator\Plugins`).
2. Register the plugin in `UiPath.Orchestrator.dll.config`:

```xml
<add key="Plugins.SecureStores" value="UiPath.Akeyless.SecureStore.dll" />
```

If you already use other credential store plugins, separate DLL names with semicolons.

3. Restart Orchestrator (restart IIS on Windows deployments).
4. Confirm the plugin loaded successfully in Orchestrator logs.
5. In Orchestrator, open **Tenant → Credential Stores → Add Credential Store** and choose **Akeyless** or **Akeyless (read-only)**.

See [docs/INTEGRATION.md](docs/INTEGRATION.md) for full configuration, RBAC, AWS IAM auth, and secret layout guidance.

Additional guides:

- [docs/MIGRATION.md](docs/MIGRATION.md) — migrate robots and assets from Orchestrator DB or another credential store
- [docs/UIPATH_PUBLISHING.md](docs/UIPATH_PUBLISHING.md) — publish on UiPath Partner records and Marketplace

## Credential store configuration

| Field | Description |
| --- | --- |
| **API URL** | Akeyless API or Gateway URL (default `https://api.akeyless.io`) |
| **Authentication Type** | `AccessKey` (default), `Jwt`, or `AwsIam` |
| **Access ID** | Akeyless Access ID used to authenticate Orchestrator |
| **Access Key** | Required when Authentication Type is `AccessKey` |
| **JWT Token** | Required when Authentication Type is `Jwt` |
| **Cloud ID** | Optional for `AwsIam`; auto-detected from EC2/instance profile when empty |
| **AWS Region** | STS signing region for `AwsIam` (default `us-east-1`) |
| **STS URL** | STS endpoint for `AwsIam` (default `https://sts.amazonaws.com/`) |
| **Data Path** | Path prefix in Akeyless for all Orchestrator-managed secrets (for example `/applications/orchestrator`) |

### Recommended Akeyless RBAC

Grant the Orchestrator auth method access under the configured data path:

| Plugin | Required capabilities |
| --- | --- |
| **Akeyless (read-only)** | `read` on static secrets under the data path |
| **Akeyless** | `read`, `create`, `update`, `delete` on static secrets under the data path |

## Secret layout

Orchestrator resolves secrets using the credential store **Data Path** plus the asset or robot **External Name** (or asset name when External Name is empty).

### Unattended robot passwords (read-only)

Provision a static secret at:

`/data-path/robots/{ExternalName}`

Secret value must include a `Value` field:

```json
{"Value":"robot-password"}
```

A plain string value is also accepted.

### Credential assets (read-only)

Provision a static secret at:

`/data-path/assets/{ExternalName}`

Secret value must include case-sensitive `Username` and `Password` fields:

```json
{"Username":"svc-account","Password":"svc-password"}
```

### Read-write mode

When using the **Akeyless** plugin, Orchestrator creates and updates secrets in the same layout automatically. You do not need to pre-provision secrets unless you prefer a read-only workflow.

## Typical deployment flow

```text
1. Build UiPath.Akeyless.SecureStore.dll
2. Deploy DLL to Orchestrator/Plugins
3. Register plugin in UiPath.Orchestrator.dll.config
4. Create Akeyless auth method with RBAC on /applications/orchestrator/*
5. Add Orchestrator credential store (Akeyless or Akeyless read-only)
6. Link robots/assets to the credential store
```

## Security notes

- Store Access ID, Access Key, and JWT values only in Orchestrator credential store configuration (treat as sensitive).
- Use HTTPS for production API URLs.
- Restrict who can edit credential stores and linked robots/assets in Orchestrator.
- Prefer read-only mode when secrets are managed centrally in Akeyless by a secrets team.

## Related integrations

Other Akeyless integrations in this workspace:

- [octopus-akeyless-plugin](../octopus-akeyless-plugin)
- [teamcity-akeyless-plugin](../teamcity-akeyless-plugin)
- [JenkinsSecretsManagerProvider](../JenkinsSecretsManagerProvider)
- [buildkite-akeyless-plugin](../buildkite-akeyless-plugin)

## License

MIT — see [LICENSE](LICENSE).
