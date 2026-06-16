using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UiPath.Orchestrator.Extensibility.Configuration;
using UiPath.Orchestrator.Extensibility.SecureStores;

namespace UiPath.Orchestrator.Extensions.SecureStores.Akeyless
{
    /// <summary>
    /// Read-write Akeyless credential store for UiPath Orchestrator.
    /// </summary>
    public sealed class AkeylessSecureStore : AkeylessSecureStoreBase
    {
        public const string NameIdentifier = "Akeyless";

        public AkeylessSecureStore()
            : base(NameIdentifier, isReadOnly: false)
        {
        }
    }

    /// <summary>
    /// Read-only Akeyless credential store for UiPath Orchestrator.
    /// </summary>
    public sealed class AkeylessReadOnlySecureStore : AkeylessSecureStoreBase
    {
        public const string NameIdentifier = "Akeyless (read-only)";

        public AkeylessReadOnlySecureStore()
            : base(NameIdentifier, isReadOnly: true)
        {
        }
    }

    public abstract class AkeylessSecureStoreBase : ISecureStore
    {
        private readonly string _identifier;
        private readonly bool _isReadOnly;

        protected AkeylessSecureStoreBase(string identifier, bool isReadOnly)
        {
            _identifier = identifier;
            _isReadOnly = isReadOnly;
        }

        public void Initialize(Dictionary<string, string> hostSettings)
        {
            // Host-level settings are optional for this plugin.
        }

        public SecureStoreInfo GetStoreInfo()
        {
            return new SecureStoreInfo
            {
                Identifier = _identifier,
                IsReadOnly = _isReadOnly,
            };
        }

        public IEnumerable<ConfigurationEntry> GetConfiguration()
        {
            return new List<ConfigurationEntry>
            {
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "ApiUrl",
                    DisplayName = "API URL",
                    IsMandatory = true,
                    DefaultValue = "https://api.akeyless.io",
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "AuthenticationType",
                    DisplayName = "Authentication Type (AccessKey, Jwt, or AwsIam)",
                    IsMandatory = true,
                    DefaultValue = AkeylessAuthTypes.AccessKey,
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "AccessId",
                    DisplayName = "Access ID",
                    IsMandatory = true,
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "AccessKey",
                    DisplayName = "Access Key",
                    IsMandatory = false,
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "JwtToken",
                    DisplayName = "JWT Token",
                    IsMandatory = false,
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "CloudId",
                    DisplayName = "Cloud ID (optional for AwsIam; auto-detected from EC2/instance profile when empty)",
                    IsMandatory = false,
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "AwsRegion",
                    DisplayName = "AWS Region (for AwsIam)",
                    IsMandatory = false,
                    DefaultValue = "us-east-1",
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "StsUrl",
                    DisplayName = "STS URL (for AwsIam)",
                    IsMandatory = false,
                    DefaultValue = "https://sts.amazonaws.com/",
                },
                new ConfigurationValue(ConfigurationValueType.String)
                {
                    Key = "DataPath",
                    DisplayName = "Data Path",
                    IsMandatory = true,
                },
            };
        }

        public async Task ValidateContextAsync(string context)
        {
            var parsed = AkeylessContextParser.Parse(context);
            var client = new AkeylessApiClient(parsed);
            await client.ValidateConnectivityAsync().ConfigureAwait(false);

            if (!_isReadOnly)
            {
                await client.ValidateReadWriteAsync().ConfigureAwait(false);
            }
        }

        public async Task<string> GetValueAsync(string context, string key)
        {
            key = key ?? throw new SecureStoreException();
            var parsed = AkeylessContextParser.Parse(context);
            var storageKey = AkeylessStorageKey.FromAugmentedKey(key, parsed.DataPath, key);
            var client = new AkeylessApiClient(parsed);
            var rawValue = await client.GetSecretValueAsync(storageKey.SecretPath).ConfigureAwait(false);
            return AkeylessSecretValueParser.ExtractRobotPassword(rawValue);
        }

        public async Task<Credential> GetCredentialsAsync(string context, string key)
        {
            key = key ?? throw new SecureStoreException();
            var parsed = AkeylessContextParser.Parse(context);
            var storageKey = AkeylessStorageKey.FromAugmentedKey(key, parsed.DataPath, key);
            var client = new AkeylessApiClient(parsed);
            var rawValue = await client.GetSecretValueAsync(storageKey.SecretPath).ConfigureAwait(false);
            return AkeylessSecretValueParser.ExtractCredentials(rawValue);
        }

        public Task<string> CreateValueAsync(string context, string key, string value)
        {
            EnsureWritable();
            value = value ?? throw new ArgumentNullException(nameof(value));
            return CreateValueInternalAsync(
                context,
                key,
                AkeylessSecretValueParser.FormatRobotPassword(value));
        }

        public Task<string> CreateCredentialsAsync(string context, string key, Credential value)
        {
            EnsureWritable();
            value = value ?? throw new ArgumentNullException(nameof(value));
            return CreateValueInternalAsync(
                context,
                key,
                AkeylessSecretValueParser.FormatCredentials(value));
        }

        public Task<string> UpdateValueAsync(string context, string key, string oldAugmentedKey, string value)
        {
            EnsureWritable();
            value = value ?? throw new ArgumentNullException(nameof(value));
            return UpdateValueInternalAsync(
                context,
                key,
                oldAugmentedKey,
                AkeylessSecretValueParser.FormatRobotPassword(value));
        }

        public Task<string> UpdateCredentialsAsync(string context, string key, string oldAugmentedKey, Credential value)
        {
            EnsureWritable();
            value = value ?? throw new ArgumentNullException(nameof(value));
            return UpdateValueInternalAsync(
                context,
                key,
                oldAugmentedKey,
                AkeylessSecretValueParser.FormatCredentials(value));
        }

        public async Task RemoveValueAsync(string context, string key)
        {
            EnsureWritable();
            key = key ?? throw new ArgumentNullException(nameof(key));
            var parsed = AkeylessContextParser.Parse(context);
            var storageKey = AkeylessStorageKey.FromAugmentedKey(key, parsed.DataPath, key);
            var client = new AkeylessApiClient(parsed);

            try
            {
                await client.DeleteSecretAsync(storageKey.SecretPath).ConfigureAwait(false);
            }
            catch (SecureStoreException ex) when (ex.ErrorType == SecureStoreException.Type.SecretNotFound)
            {
                // Ignore missing secrets during delete.
            }
        }

        private async Task<string> CreateValueInternalAsync(string context, string key, string storedValue)
        {
            key = key ?? throw new ArgumentNullException(nameof(key));
            storedValue = storedValue ?? throw new ArgumentNullException(nameof(storedValue));

            var parsed = AkeylessContextParser.Parse(context);
            var logicalKey = ResolveLogicalKey(key);
            var storageKey = new AkeylessStorageKey
            {
                SecretPath = AkeylessPathHelper.BuildSecretPath(parsed.DataPath, logicalKey),
            };

            var client = new AkeylessApiClient(parsed);
            await client.CreateSecretAsync(storageKey.SecretPath, storedValue).ConfigureAwait(false);
            return storageKey.Serialize();
        }

        private async Task<string> UpdateValueInternalAsync(string context, string key, string oldAugmentedKey, string storedValue)
        {
            key = key ?? throw new ArgumentNullException(nameof(key));
            oldAugmentedKey = oldAugmentedKey ?? throw new ArgumentNullException(nameof(oldAugmentedKey));
            storedValue = storedValue ?? throw new ArgumentNullException(nameof(storedValue));

            var parsed = AkeylessContextParser.Parse(context);
            var logicalKey = ResolveLogicalKey(key);
            var storageKey = AkeylessStorageKey.FromAugmentedKey(oldAugmentedKey, parsed.DataPath, logicalKey);
            var client = new AkeylessApiClient(parsed);
            await client.UpdateSecretAsync(storageKey.SecretPath, storedValue).ConfigureAwait(false);

            if (!string.Equals(logicalKey, key, StringComparison.Ordinal))
            {
                storageKey = new AkeylessStorageKey
                {
                    SecretPath = AkeylessPathHelper.BuildSecretPath(parsed.DataPath, logicalKey),
                };
            }

            return storageKey.Serialize();
        }

        private static string ResolveLogicalKey(string key)
        {
            try
            {
                var storageKey = Newtonsoft.Json.JsonConvert.DeserializeObject<AkeylessStorageKey>(key);
                if (storageKey != null && !string.IsNullOrWhiteSpace(storageKey.SecretPath))
                {
                    var segments = storageKey.SecretPath.Trim('/').Split('/');
                    return segments[segments.Length - 1];
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Fall back to the provided key.
            }

            return key;
        }

        private void EnsureWritable()
        {
            if (_isReadOnly)
            {
                throw new SecureStoreException(
                    SecureStoreException.Type.UnsupportedOperation,
                    "This credential store is read-only.");
            }
        }
    }
}
