using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UiPath.Orchestrator.Extensibility.SecureStores;

namespace UiPath.Orchestrator.Extensions.SecureStores.Akeyless
{
    internal sealed class AkeylessApiClient
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private static readonly ConcurrentDictionary<string, TokenCacheEntry> TokenCache = new ConcurrentDictionary<string, TokenCacheEntry>();

        private readonly AkeylessContext _context;

        public AkeylessApiClient(AkeylessContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<string> GetSecretValueAsync(string secretPath, CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                ["token"] = token,
                ["names"] = new[] { secretPath },
            };

            var response = await PostAsync<JObject>("get-secret-value", body, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                throw new SecureStoreException(SecureStoreException.Type.SecretNotFound, "Secret was not found.");
            }

            if (response.TryGetValue(secretPath, out var direct))
            {
                return direct?.ToString() ?? string.Empty;
            }

            foreach (var property in response.Properties())
            {
                if (!string.IsNullOrWhiteSpace(property.Value?.ToString()))
                {
                    return property.Value?.ToString() ?? string.Empty;
                }
            }

            throw new SecureStoreException(SecureStoreException.Type.SecretNotFound, "Secret was not found.");
        }

        public async Task CreateSecretAsync(string secretPath, string value, CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                ["token"] = token,
                ["name"] = secretPath,
                ["value"] = value,
            };

            await PostAsync<JObject>("create-secret", body, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateSecretAsync(string secretPath, string value, CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                ["token"] = token,
                ["name"] = secretPath,
                ["value"] = value,
            };

            await PostAsync<JObject>("update-secret-val", body, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteSecretAsync(string secretPath, CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                ["token"] = token,
                ["name"] = secretPath,
            };

            await PostAsync<JObject>("delete-item", body, cancellationToken).ConfigureAwait(false);
        }

        public async Task ValidateConnectivityAsync(CancellationToken cancellationToken = default)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new SecureStoreException(
                    SecureStoreException.Type.InvalidCredential,
                    "Authentication succeeded but no token was returned.");
            }
        }

        public async Task ValidateReadWriteAsync(CancellationToken cancellationToken = default)
        {
            var testPath = AkeylessPathHelper.BuildSecretPath(
                _context.DataPath,
                $"uipath-test-{Guid.NewGuid():N}");

            var testValue = AkeylessSecretValueParser.FormatRobotPassword("connectivity-test");
            await CreateSecretAsync(testPath, testValue, cancellationToken).ConfigureAwait(false);

            try
            {
                var readValue = await GetSecretValueAsync(testPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(
                        AkeylessSecretValueParser.ExtractRobotPassword(readValue),
                        "connectivity-test",
                        StringComparison.Ordinal))
                {
                    throw new SecureStoreException("Connectivity validation failed while reading the test secret.");
                }
            }
            finally
            {
                try
                {
                    await DeleteSecretAsync(testPath, cancellationToken).ConfigureAwait(false);
                }
                catch (SecureStoreException ex) when (ex.ErrorType == SecureStoreException.Type.SecretNotFound)
                {
                    // Ignore cleanup failures for missing secrets.
                }
            }
        }

        private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            var cacheKey = BuildTokenCacheKey();
            if (TokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return cached.Token;
            }

            var body = await BuildAuthBodyAsync(cancellationToken).ConfigureAwait(false);
            var response = await PostAsync<AuthResponse>("auth", body, cancellationToken).ConfigureAwait(false);
            if (response == null || string.IsNullOrWhiteSpace(response.Token))
            {
                throw new SecureStoreException(
                    SecureStoreException.Type.InvalidCredential,
                    "Authentication with Akeyless failed.");
            }

            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            if (response.Expiration > 0)
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(response.Expiration).UtcDateTime;
            }

            TokenCache[cacheKey] = new TokenCacheEntry(response.Token, expiresAt);
            return response.Token;
        }

        private async Task<Dictionary<string, object>> BuildAuthBodyAsync(CancellationToken cancellationToken)
        {
            if (string.Equals(_context.AuthenticationType, AkeylessAuthTypes.Jwt, StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object>
                {
                    ["access-id"] = _context.AccessId,
                    ["access-type"] = "jwt",
                    ["jwt"] = _context.JwtToken,
                };
            }

            if (string.Equals(_context.AuthenticationType, AkeylessAuthTypes.AwsIam, StringComparison.OrdinalIgnoreCase))
            {
                var cloudId = await AkeylessAwsIamSigner.ResolveCloudIdAsync(
                    _context.CloudId,
                    _context.AwsRegion,
                    _context.StsUrl,
                    cancellationToken).ConfigureAwait(false);

                return new Dictionary<string, object>
                {
                    ["access-id"] = _context.AccessId,
                    ["access-type"] = "aws_iam",
                    ["cloud-id"] = cloudId,
                };
            }

            return new Dictionary<string, object>
            {
                ["access-id"] = _context.AccessId,
                ["access-type"] = "access_key",
                ["access-key"] = _context.AccessKey,
            };
        }

        private string BuildTokenCacheKey()
        {
            var secretMaterial = _context.AuthenticationType switch
            {
                var type when string.Equals(type, AkeylessAuthTypes.Jwt, StringComparison.OrdinalIgnoreCase) => _context.JwtToken,
                var type when string.Equals(type, AkeylessAuthTypes.AwsIam, StringComparison.OrdinalIgnoreCase) =>
                    string.Join("|", _context.CloudId, _context.AwsRegion, _context.StsUrl),
                _ => _context.AccessKey,
            };

            return string.Join(
                "|",
                _context.ApiUrl,
                _context.AuthenticationType,
                _context.AccessId,
                secretMaterial);
        }

        private async Task<T?> PostAsync<T>(string path, Dictionary<string, object> body, CancellationToken cancellationToken)
            where T : class
        {
            var uri = $"{_context.ApiUrl}/{path.TrimStart('/')}";
            var json = JsonConvert.SerializeObject(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await SharedHttpClient.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new SecureStoreException($"Failed to call Akeyless API endpoint '{path}'.", ex);
            }

            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(JObject))
                {
                    return (JsonConvert.DeserializeObject<T>(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody))!;
                }

                return JsonConvert.DeserializeObject<T>(responseBody);
            }

            throw MapApiError(response.StatusCode, responseBody, path);
        }

        private static SecureStoreException MapApiError(HttpStatusCode statusCode, string responseBody, string path)
        {
            var detail = ExtractErrorMessage(responseBody);
            switch (statusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new SecureStoreException(
                        SecureStoreException.Type.InvalidCredential,
                        string.IsNullOrWhiteSpace(detail)
                            ? "Akeyless rejected the configured credentials."
                            : detail);
                case HttpStatusCode.NotFound:
                    return new SecureStoreException(
                        SecureStoreException.Type.SecretNotFound,
                        string.IsNullOrWhiteSpace(detail)
                            ? "Secret was not found."
                            : detail);
                default:
                    return new SecureStoreException(
                        $"Akeyless API call to '{path}' failed with status {(int)statusCode}: {detail}");
            }
        }

        private static string ExtractErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            try
            {
                var json = JObject.Parse(responseBody);
                return json["error"]?.ToString()
                    ?? json["message"]?.ToString()
                    ?? responseBody;
            }
            catch (JsonException)
            {
                return responseBody;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            return client;
        }

        private sealed class AuthResponse
        {
            [JsonProperty("token")]
            public string Token { get; set; } = string.Empty;

            [JsonProperty("token_expiration")]
            public long Expiration { get; set; }
        }

        private sealed class TokenCacheEntry
        {
            public TokenCacheEntry(string token, DateTime expiresAtUtc)
            {
                Token = token;
                ExpiresAtUtc = expiresAtUtc;
            }

            public string Token { get; }

            public DateTime ExpiresAtUtc { get; }
        }
    }
}
