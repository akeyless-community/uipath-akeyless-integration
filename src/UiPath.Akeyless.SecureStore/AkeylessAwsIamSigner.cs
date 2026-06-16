using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UiPath.Orchestrator.Extensibility.SecureStores;

namespace UiPath.Orchestrator.Extensions.SecureStores.Akeyless
{
    internal static class AkeylessAwsIamSigner
    {
        private const string DefaultRegion = "us-east-1";
        private const string DefaultStsUrl = "https://sts.amazonaws.com/";

        public static async Task<string> ResolveCloudIdAsync(
            string? configuredCloudId,
            string awsRegion,
            string stsUrl,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(configuredCloudId))
            {
                return configuredCloudId!.Trim();
            }

            var credentials = await ResolveAwsCredentialsAsync(cancellationToken).ConfigureAwait(false);
            return BuildCloudId(
                credentials.AccessKeyId,
                credentials.SecretAccessKey,
                credentials.SessionToken,
                awsRegion,
                stsUrl);
        }

        internal static string BuildCloudId(
            string accessKeyId,
            string secretAccessKey,
            string sessionToken,
            string region,
            string stsUrl)
        {
            const string service = "sts";
            const string method = "POST";
            const string body = "Action=GetCallerIdentity&Version=2011-06-15";

            var hostName = new Uri(stsUrl).Host;
            var amzDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            var dateStamp = amzDate.Substring(0, 8);
            var payloadHash = Sha256Hex(body);

            var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = hostName,
                ["content-type"] = "application/x-www-form-urlencoded; charset=utf-8",
                ["content-length"] = body.Length.ToString(CultureInfo.InvariantCulture),
                ["x-amz-date"] = amzDate,
            };

            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                headers["x-amz-security-token"] = sessionToken;
            }

            var canonicalHeaders = string.Join("\n", headers.Select(pair => $"{pair.Key}:{pair.Value}")) + "\n";
            var signedHeaders = string.Join(";", headers.Keys);
            var canonicalRequest = string.Join(
                "\n",
                method,
                "/",
                string.Empty,
                canonicalHeaders,
                signedHeaders,
                payloadHash);

            var credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
            var stringToSign = string.Join(
                "\n",
                "AWS4-HMAC-SHA256",
                amzDate,
                credentialScope,
                Sha256Hex(canonicalRequest));

            var signingKey = GetSigningKey(secretAccessKey, dateStamp, region, service);
            var signature = HmacSha256Hex(signingKey, stringToSign);
            var authorization =
                $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

            var requestHeaders = new Dictionary<string, string[]>
            {
                ["Authorization"] = new[] { authorization },
                ["Content-Length"] = new[] { body.Length.ToString(CultureInfo.InvariantCulture) },
                ["Host"] = new[] { hostName },
                ["Content-Type"] = new[] { "application/x-www-form-urlencoded; charset=utf-8" },
                ["X-Amz-Date"] = new[] { amzDate },
            };

            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                requestHeaders["X-Amz-Security-Token"] = new[] { sessionToken };
            }

            var payload = new Dictionary<string, string>
            {
                ["sts_request_method"] = method,
                ["sts_request_url"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(stsUrl)),
                ["sts_request_body"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(body)),
                ["sts_request_headers"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestHeaders))),
            };

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
        }

        private static async Task<AwsCredentials> ResolveAwsCredentialsAsync(CancellationToken cancellationToken)
        {
            var accessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
            var secretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            var sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

            if (!string.IsNullOrWhiteSpace(accessKeyId) && !string.IsNullOrWhiteSpace(secretAccessKey))
            {
                return new AwsCredentials(
                    accessKeyId.Trim(),
                    secretAccessKey.Trim(),
                    sessionToken?.Trim() ?? string.Empty);
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Put, "http://169.254.169.254/latest/api/token");
                tokenRequest.Headers.Add("X-aws-ec2-metadata-token-ttl-seconds", "21600");
                using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
                tokenResponse.EnsureSuccessStatusCode();
                var imdsToken = (await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();

                using var roleNameRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    "http://169.254.169.254/latest/meta-data/iam/security-credentials/");
                roleNameRequest.Headers.Add("X-aws-ec2-metadata-token", imdsToken);
                using var roleNameResponse = await client.SendAsync(roleNameRequest, cancellationToken).ConfigureAwait(false);
                roleNameResponse.EnsureSuccessStatusCode();
                var roleName = (await roleNameResponse.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();

                using var roleRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"http://169.254.169.254/latest/meta-data/iam/security-credentials/{roleName.Trim()}");
                roleRequest.Headers.Add("X-aws-ec2-metadata-token", imdsToken);
                using var roleResponse = await client.SendAsync(roleRequest, cancellationToken).ConfigureAwait(false);
                roleResponse.EnsureSuccessStatusCode();
                var roleJson = await roleResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var roleCredentials = JsonConvert.DeserializeObject<ImdsRoleCredentials>(roleJson)
                    ?? throw new SecureStoreException(
                        SecureStoreException.Type.InvalidCredential,
                        "EC2 instance metadata returned invalid IAM credentials.");

                if (string.IsNullOrWhiteSpace(roleCredentials.AccessKeyId))
                {
                    throw new SecureStoreException(
                        SecureStoreException.Type.InvalidCredential,
                        "EC2 instance metadata returned no IAM credentials.");
                }

                return new AwsCredentials(
                    roleCredentials.AccessKeyId,
                    roleCredentials.SecretAccessKey,
                    roleCredentials.Token ?? string.Empty);
            }
            catch (SecureStoreException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SecureStoreException(
                    SecureStoreException.Type.InvalidCredential,
                    "AWS credentials were not found in environment variables or EC2 instance metadata.",
                    ex);
            }
        }

        private static byte[] GetSigningKey(string secretKey, string dateStamp, string region, string service)
        {
            var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
            var kDate = HmacSha256(kSecret, dateStamp);
            var kRegion = HmacSha256(kDate, region);
            var kService = HmacSha256(kRegion, service);
            return HmacSha256(kService, "aws4_request");
        }

        private static byte[] HmacSha256(byte[] key, string data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        private static string HmacSha256Hex(byte[] key, string data)
        {
            return ToHex(HmacSha256(key, data));
        }

        private static string Sha256Hex(string data)
        {
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        internal sealed class AwsCredentials
        {
            public AwsCredentials(string accessKeyId, string secretAccessKey, string sessionToken)
            {
                AccessKeyId = accessKeyId;
                SecretAccessKey = secretAccessKey;
                SessionToken = sessionToken;
            }

            public string AccessKeyId { get; }

            public string SecretAccessKey { get; }

            public string SessionToken { get; }
        }

        private sealed class ImdsRoleCredentials
        {
            [JsonProperty("AccessKeyId")]
            public string AccessKeyId { get; set; } = string.Empty;

            [JsonProperty("SecretAccessKey")]
            public string SecretAccessKey { get; set; } = string.Empty;

            [JsonProperty("Token")]
            public string? Token { get; set; }
        }

        internal static string NormalizeRegion(string? region)
        {
            return string.IsNullOrWhiteSpace(region) ? DefaultRegion : region!.Trim();
        }

        internal static string NormalizeStsUrl(string? stsUrl)
        {
            return string.IsNullOrWhiteSpace(stsUrl) ? DefaultStsUrl : stsUrl!.Trim();
        }
    }
}
