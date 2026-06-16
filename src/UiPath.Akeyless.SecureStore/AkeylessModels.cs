using System;

namespace UiPath.Orchestrator.Extensions.SecureStores.Akeyless
{
    internal static class AkeylessAuthTypes
    {
        public const string AccessKey = "AccessKey";
        public const string Jwt = "Jwt";
        public const string AwsIam = "AwsIam";
    }

    internal sealed class AkeylessContext
    {
        public string ApiUrl { get; set; } = "https://api.akeyless.io";

        public string AuthenticationType { get; set; } = AkeylessAuthTypes.AccessKey;

        public string AccessId { get; set; } = string.Empty;

        public string AccessKey { get; set; } = string.Empty;

        public string JwtToken { get; set; } = string.Empty;

        public string CloudId { get; set; } = string.Empty;

        public string AwsRegion { get; set; } = "us-east-1";

        public string StsUrl { get; set; } = "https://sts.amazonaws.com/";

        public string DataPath { get; set; } = string.Empty;
    }

    internal sealed class AkeylessStorageKey
    {
        public string SecretPath { get; set; } = string.Empty;

        public static AkeylessStorageKey FromAugmentedKey(string? augmentedKey, string dataPath, string logicalKey)
        {
            if (!string.IsNullOrWhiteSpace(augmentedKey))
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<AkeylessStorageKey>(augmentedKey!);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.SecretPath))
                {
                    return parsed;
                }
            }

            return new AkeylessStorageKey
            {
                SecretPath = AkeylessPathHelper.BuildSecretPath(dataPath, logicalKey),
            };
        }

        public string Serialize()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this);
        }
    }

    internal static class AkeylessPathHelper
    {
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            var trimmed = path.Trim().Replace('\\', '/');
            while (trimmed.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                trimmed = trimmed.Replace("//", "/");
            }

            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                trimmed = "/" + trimmed;
            }

            return trimmed.TrimEnd('/');
        }

        public static void ValidateLogicalKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new System.ArgumentNullException(nameof(key));
            }

            if (key.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "Secret key must not contain path traversal sequences.");
            }
        }

        public static string BuildSecretPath(string dataPath, string logicalKey)
        {
            ValidateLogicalKey(logicalKey);
            var prefix = NormalizePath(dataPath);
            var suffix = logicalKey.Trim().TrimStart('/');
            return NormalizePath($"{prefix}/{suffix}");
        }
    }

    internal static class AkeylessSecretValueParser
    {
        public static string ExtractRobotPassword(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.SecretNotFound,
                    "Secret value is empty.");
            }

            var trimmed = rawValue.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return rawValue;
            }

            var document = Newtonsoft.Json.Linq.JObject.Parse(trimmed);
            var valueToken = document["Value"] ?? document["value"];
            if (valueToken == null || valueToken.Type == Newtonsoft.Json.Linq.JTokenType.Null)
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.SecretNotFound,
                    "Robot password secret must contain a Value field.");
            }

            return valueToken.ToString();
        }

        public static UiPath.Orchestrator.Extensibility.SecureStores.Credential ExtractCredentials(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.SecretNotFound,
                    "Secret value is empty.");
            }

            var trimmed = rawValue.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var document = Newtonsoft.Json.Linq.JObject.Parse(trimmed);
                var username = document["Username"] ?? document["username"];
                var password = document["Password"] ?? document["password"];
                if (username != null && password != null)
                {
                    return new UiPath.Orchestrator.Extensibility.SecureStores.Credential
                    {
                        Username = username.ToString(),
                        Password = password.ToString(),
                    };
                }

                var credential = Newtonsoft.Json.JsonConvert.DeserializeObject<UiPath.Orchestrator.Extensibility.SecureStores.Credential>(trimmed);
                if (credential != null && !string.IsNullOrEmpty(credential.Username))
                {
                    return credential;
                }
            }

            throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.SecretNotFound,
                "Credential secret must contain Username and Password fields.");
        }

        public static string FormatRobotPassword(string password)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { Value = password });
        }

        public static string FormatCredentials(UiPath.Orchestrator.Extensibility.SecureStores.Credential credential)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(credential);
        }
    }

    internal static class AkeylessContextParser
    {
        public static AkeylessContext Parse(string contextJson)
        {
            if (string.IsNullOrWhiteSpace(contextJson))
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "Credential store configuration is missing.");
            }

            var context = Newtonsoft.Json.JsonConvert.DeserializeObject<AkeylessContext>(contextJson)
                ?? throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "Credential store configuration is invalid.");

            context.ApiUrl = string.IsNullOrWhiteSpace(context.ApiUrl)
                ? "https://api.akeyless.io"
                : context.ApiUrl.TrimEnd('/');

            ValidateApiUrl(context.ApiUrl);

            if (string.IsNullOrWhiteSpace(context.AuthenticationType))
            {
                context.AuthenticationType = AkeylessAuthTypes.AccessKey;
            }

            if (string.Equals(context.AuthenticationType, AkeylessAuthTypes.AccessKey, System.StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(context.AccessId) || string.IsNullOrWhiteSpace(context.AccessKey))
                {
                    throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                        UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                        "Access ID and Access Key are required for AccessKey authentication.");
                }
            }
            else if (string.Equals(context.AuthenticationType, AkeylessAuthTypes.Jwt, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(context.AccessId) || string.IsNullOrWhiteSpace(context.JwtToken))
                {
                    throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                        UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                        "Access ID and JWT token are required for Jwt authentication.");
                }
            }
            else if (string.Equals(context.AuthenticationType, AkeylessAuthTypes.AwsIam, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(context.AccessId))
                {
                    throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                        UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                        "Access ID is required for AwsIam authentication.");
                }

                context.AwsRegion = AkeylessAwsIamSigner.NormalizeRegion(context.AwsRegion);
                context.StsUrl = AkeylessAwsIamSigner.NormalizeStsUrl(context.StsUrl);
            }
            else
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "Authentication type must be AccessKey, Jwt, or AwsIam.");
            }

            if (string.IsNullOrWhiteSpace(context.DataPath))
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "Data path is required.");
            }

            context.DataPath = AkeylessPathHelper.NormalizePath(context.DataPath);
            return context;
        }

        private static void ValidateApiUrl(string apiUrl)
        {
            if (!System.Uri.TryCreate(apiUrl, System.UriKind.Absolute, out var uri))
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "API URL must be a valid absolute URL.");
            }

            if (uri.Scheme != System.Uri.UriSchemeHttps && uri.Scheme != System.Uri.UriSchemeHttp)
            {
                throw new UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException(
                    UiPath.Orchestrator.Extensibility.SecureStores.SecureStoreException.Type.InvalidConfiguration,
                    "API URL must use http or https.");
            }
        }
    }
}
