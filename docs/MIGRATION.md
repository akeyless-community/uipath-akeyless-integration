# Migrating Orchestrator secrets to Akeyless

This guide helps you move UiPath Orchestrator robot passwords and credential assets from the Orchestrator database or another external credential store into Akeyless, then connect Orchestrator using the [Akeyless credential store plugin](INTEGRATION.md).

## Migration overview

```text
1. Inventory robots/assets currently using Orchestrator DB or another credential store
2. Create Akeyless folder structure and RBAC
3. Export existing secrets (or re-provision)
4. Import secrets into Akeyless with Orchestrator-compatible formats
5. Install the Akeyless Orchestrator plugin
6. Create Akeyless credential store(s) in Orchestrator
7. Re-link robots/assets and validate jobs
8. Decommission old secret storage
```

## Before you start

| Item | Recommendation |
| --- | --- |
| **Data path** | `/applications/orchestrator` (split `robots/` and `assets/` subfolders) |
| **Plugin mode** | Start with **Akeyless (read-only)** during migration; switch to **Akeyless** read-write after validation |
| **Downtime** | Plan a maintenance window for re-linking robots/assets |
| **Audit** | Enable Akeyless audit logs before migration |

## Step 1 — Inventory Orchestrator secrets

Export a list of:

- Unattended robots using a credential store (or Orchestrator DB)
- Credential assets with external names
- Current credential store type and data path prefix (if migrating from another vault)

Record for each item:

| Orchestrator object | External Name | Secret type |
| --- | --- | --- |
| Robot `PROD\robot01` | `robot01` | Robot password |
| Asset `SAPCredentials` | `SAPCredentials` | Username + password |

## Step 2 — Create Akeyless structure and RBAC

```bash
# Auth method for Orchestrator
akeyless create-auth-method --name /applications/orchestrator/uipath

# Read-only role for migration validation
akeyless create-role --name orchestrator-readonly
akeyless set-role-rule --role-name orchestrator-readonly \
  --path "/applications/orchestrator/*" \
  --capabilities read

# Read-write role (after cutover)
akeyless create-role --name orchestrator-rw
akeyless set-role-rule --role-name orchestrator-rw \
  --path "/applications/orchestrator/*" \
  --capabilities read,create,update,delete
```

Bind the auth method to the appropriate role.

## Step 3 — Secret formats in Akeyless

Orchestrator expects these formats when using the read-only plugin.

### Robot passwords

Path: `/applications/orchestrator/robots/{ExternalName}`

```json
{"Value":"the-robot-password"}
```

CLI example:

```bash
akeyless create-secret \
  --name /applications/orchestrator/robots/robot01 \
  --value '{"Value":"the-robot-password"}'
```

### Credential assets

Path: `/applications/orchestrator/assets/{ExternalName}`

```json
{"Username":"svc-user","Password":"svc-password"}
```

CLI example:

```bash
akeyless create-secret \
  --name /applications/orchestrator/assets/SAPCredentials \
  --value '{"Username":"svc-user","Password":"svc-password"}'
```

Field names are **case-sensitive** (`Value`, `Username`, `Password`).

## Step 4 — Bulk import

### Option A — CLI loop from CSV

CSV columns: `type,external_name,username,password`

```bash
while IFS=, read -r type external_name username password; do
  if [ "$type" = "robot" ]; then
    akeyless create-secret \
      --name "/applications/orchestrator/robots/${external_name}" \
      --value "{\"Value\":\"${password}\"}"
  else
    akeyless create-secret \
      --name "/applications/orchestrator/assets/${external_name}" \
      --value "{\"Username\":\"${username}\",\"Password\":\"${password}\"}"
  fi
done < orchestrator-secrets.csv
```

### Option A — PowerShell (Windows Orchestrator admins)

```powershell
Import-Csv .\orchestrator-secrets.csv | ForEach-Object {
    if ($_.type -eq 'robot') {
        $value = @{ Value = $_.password } | ConvertTo-Json -Compress
        akeyless create-secret --name "/applications/orchestrator/robots/$($_.external_name)" --value $value
    } else {
        $value = @{ Username = $_.username; Password = $_.password } | ConvertTo-Json -Compress
        akeyless create-secret --name "/applications/orchestrator/assets/$($_.external_name)" --value $value
    }
}
```

### Option B — Read-write plugin (no pre-import)

If you use the **Akeyless** read-write plugin:

1. Install the plugin and create the credential store.
2. Re-create or edit each robot/asset in Orchestrator and assign the Akeyless store.
3. Orchestrator writes secrets to Akeyless automatically.

Use this when you can afford Orchestrator-driven secret creation and do not need a bulk import.

## Step 5 — Install plugin and create credential store

See [INTEGRATION.md](INTEGRATION.md). Summary:

1. Deploy `UiPath.Akeyless.SecureStore.dll` to `Orchestrator\Plugins`.
2. Register in `UiPath.Orchestrator.dll.config`.
3. Create credential store with:
   - **Data Path:** `/applications/orchestrator/robots` (robots) or `/applications/orchestrator/assets` (assets), or a shared parent path
   - **Authentication:** AccessKey, Jwt, or AwsIam (recommended on AWS EC2)

You can create **separate credential stores** for robots and assets with different data paths.

## Step 6 — Re-link Orchestrator objects

For each robot:

1. Edit robot → set **Credential Store** to your Akeyless store.
2. Set **External Name** to match the Akeyless secret suffix (e.g. `robot01`).
3. Run a test job.

For each credential asset:

1. Edit asset → set **Credential Store** to your Akeyless store.
2. Set **External Name** if using per-robot values.
3. Validate in Studio or a test process.

## Step 7 — Validation checklist

- [ ] Credential store validation succeeds in Orchestrator UI
- [ ] Test robot job retrieves password and logs in
- [ ] Test credential asset resolves in Studio/workflow
- [ ] Akeyless audit shows `get-secret-value` from Orchestrator auth method
- [ ] No secrets remain in old store that are still referenced by Orchestrator

## Step 8 — Decommission old storage

After a successful validation period:

1. Remove robots/assets from the old credential store.
2. Delete or rotate secrets in the previous system.
3. Update runbooks and disaster-recovery docs to reference Akeyless paths.

## Migrating from another external credential store

If you are moving from Azure Key Vault, AWS Secrets Manager, CyberArk, or another Orchestrator-supported store:

| Concern | Action |
| --- | --- |
| **Path mapping** | Map old `Data Path + External Name` to Akeyless `/applications/orchestrator/...` |
| **Secret shape** | Convert to `Value` / `Username`+`Password` JSON as above |
| **Dual-run** | Use read-only Akeyless store on a test tenant before production cutover |
| **Rollback** | Keep old credential store until Akeyless path is verified |

## AWS EC2 / IAM authentication

When Orchestrator runs on AWS EC2 with an instance profile:

1. Create an Akeyless **AWS IAM** auth method bound to the instance role.
2. Set credential store **Authentication Type** to `AwsIam`.
3. Provide **Access ID** only; leave **Cloud ID** empty for automatic detection.

See [INTEGRATION.md](INTEGRATION.md#aws-iam-authentication).

## Troubleshooting

| Issue | Fix |
| --- | --- |
| Secret not found | Verify `Data Path` + `External Name` matches Akeyless item path |
| Parse error on robot password | Ensure JSON has `Value` key |
| Parse error on asset | Ensure JSON has `Username` and `Password` keys |
| Auth failure with AwsIam | Confirm instance profile and Akeyless auth method role ARN match |

## Related docs

- [INTEGRATION.md](INTEGRATION.md) — install and configure the plugin
- [UIPATH_PUBLISHING.md](UIPATH_PUBLISHING.md) — partner and marketplace listing process
