using System;
using UiPath.Orchestrator.Extensions.SecureStores.Akeyless;
using UiPath.Orchestrator.Extensibility.SecureStores;
using Xunit;

namespace UiPath.Akeyless.SecureStore.Tests
{
    public class AkeylessPathHelperTests
    {
        [Theory]
        [InlineData("/applications/orchestrator", "robot01", "/applications/orchestrator/robot01")]
        [InlineData("applications/orchestrator", "assets/SAP", "/applications/orchestrator/assets/SAP")]
        [InlineData("/applications/orchestrator/", "robot01", "/applications/orchestrator/robot01")]
        public void BuildSecretPath_joins_prefix_and_key(string dataPath, string key, string expected)
        {
            var actual = AkeylessPathHelper.BuildSecretPath(dataPath, key);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BuildSecretPath_rejects_path_traversal()
        {
            Assert.Throws<SecureStoreException>(() =>
                AkeylessPathHelper.BuildSecretPath("/applications/orchestrator", "../secret"));
        }
    }

    public class AkeylessSecretValueParserTests
    {
        [Fact]
        public void ExtractRobotPassword_reads_value_field()
        {
            var password = AkeylessSecretValueParser.ExtractRobotPassword("{\"Value\":\"robot-pass\"}");
            Assert.Equal("robot-pass", password);
        }

        [Fact]
        public void ExtractRobotPassword_accepts_plain_string()
        {
            var password = AkeylessSecretValueParser.ExtractRobotPassword("plain-password");
            Assert.Equal("plain-password", password);
        }

        [Fact]
        public void ExtractCredentials_reads_username_and_password_fields()
        {
            var credential = AkeylessSecretValueParser.ExtractCredentials(
                "{\"Username\":\"svc-user\",\"Password\":\"svc-pass\"}");

            Assert.Equal("svc-user", credential.Username);
            Assert.Equal("svc-pass", credential.Password);
        }
    }

    public class AkeylessSecureStoreInfoTests
    {
        [Fact]
        public void ReadWrite_store_is_not_read_only()
        {
            var store = new AkeylessSecureStore();
            var info = store.GetStoreInfo();

            Assert.Equal(AkeylessSecureStore.NameIdentifier, info.Identifier);
            Assert.False(info.IsReadOnly);
        }

        [Fact]
        public void ReadOnly_store_is_read_only()
        {
            var store = new AkeylessReadOnlySecureStore();
            var info = store.GetStoreInfo();

            Assert.Equal("Akeyless (read-only)", info.Identifier);
            Assert.True(info.IsReadOnly);
        }

        [Fact]
        public void ReadOnly_store_rejects_create()
        {
            var store = new AkeylessReadOnlySecureStore();
            Assert.Throws<SecureStoreException>(() =>
                store.CreateValueAsync("{}", "robot01", "password").GetAwaiter().GetResult());
        }
    }

    public class AkeylessAwsIamSignerTests
    {
        [Fact]
        public void BuildCloudId_returns_base64_payload()
        {
            var cloudId = AkeylessAwsIamSigner.BuildCloudId(
                "AKIATEST",
                "secret",
                string.Empty,
                "us-east-1",
                "https://sts.amazonaws.com/");

            Assert.False(string.IsNullOrWhiteSpace(cloudId));
            Assert.DoesNotContain("secret", cloudId, StringComparison.Ordinal);
        }

        [Fact]
        public void NormalizeRegion_uses_default_when_empty()
        {
            Assert.Equal("us-east-1", AkeylessAwsIamSigner.NormalizeRegion(null));
        }
    }

    public class AkeylessContextParserTests
    {
        [Fact]
        public void Parse_accepts_aws_iam_with_access_id_only()
        {
            var contextJson = "{\"ApiUrl\":\"https://api.akeyless.io\",\"AuthenticationType\":\"AwsIam\",\"AccessId\":\"p-test\",\"DataPath\":\"/applications/orchestrator\"}";
            var context = AkeylessContextParser.Parse(contextJson);

            Assert.Equal("AwsIam", context.AuthenticationType);
            Assert.Equal("us-east-1", context.AwsRegion);
        }
    }
}
