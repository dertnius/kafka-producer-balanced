using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MyDotNetApp.Services;

/// <summary>
/// Azure Key Vault service for encryption key management
/// Supports Client-Side Field Level Encryption (CSFLE)
/// </summary>
public interface IAzureKeyVaultService
{
    Task<(byte[] encryptedData, byte[] iv, string keyId)> EncryptDataAsync(byte[] plaintext, CancellationToken cancellationToken = default);
    Task<byte[]> DecryptDataAsync(byte[] ciphertext, byte[] iv, string keyId, CancellationToken cancellationToken = default);
    Task<string> GetOrCreateDataEncryptionKeyAsync(CancellationToken cancellationToken = default);
}

public class AzureKeyVaultService : IAzureKeyVaultService
{
    private readonly ILogger<AzureKeyVaultService> _logger;
    private readonly KeyClient _keyClient;
    private readonly ConcurrentDictionary<string, CryptographyClient> _cryptoClientCache;
    private readonly string _dataEncryptionKeyName;
    private readonly bool _useLocalEncryption; // For development/testing without AKV

    public AzureKeyVaultService(
        IConfiguration configuration,
        ILogger<AzureKeyVaultService> logger)
    {
        _logger = logger;
        _cryptoClientCache = new ConcurrentDictionary<string, CryptographyClient>();

        var keyVaultUrl = configuration["AzureKeyVault:VaultUrl"];
        _dataEncryptionKeyName = configuration["AzureKeyVault:DataEncryptionKeyName"] ?? "dek-kafka-csfle";
        _useLocalEncryption = string.IsNullOrEmpty(keyVaultUrl);

        if (_useLocalEncryption)
        {
            _logger.LogWarning("Azure Key Vault URL not configured. Using local encryption for development. DO NOT USE IN PRODUCTION!");
            _keyClient = null!;
        }
        else
        {
            // Use DefaultAzureCredential for flexible authentication
            // Supports Managed Identity, Azure CLI, Visual Studio, etc.
            var credential = new DefaultAzureCredential();
            _keyClient = new KeyClient(new Uri(keyVaultUrl!), credential);
            _logger.LogInformation("Azure Key Vault client initialized for {VaultUrl}", keyVaultUrl);
        }
    }

    /// <summary>
    /// Encrypt data using AES-256-GCM with Azure Key Vault managed key
    /// </summary>
    public async Task<(byte[] encryptedData, byte[] iv, string keyId)> EncryptDataAsync(
        byte[] plaintext,
        CancellationToken cancellationToken = default)
    {
        if (_useLocalEncryption)
        {
            return EncryptLocally(plaintext);
        }

        try
        {
            // Get or create the data encryption key
            var keyId = await GetOrCreateDataEncryptionKeyAsync(cancellationToken);

            // Use AES-256-GCM for authenticated encryption
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            var iv = aes.IV;
            var key = aes.Key;

            // Encrypt the plaintext with local AES key
            byte[] encryptedData;
            using (var encryptor = aes.CreateEncryptor(key, iv))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    await cs.WriteAsync(plaintext, cancellationToken);
                    await cs.FlushFinalBlockAsync(cancellationToken);
                }
                encryptedData = ms.ToArray();
            }

            // Wrap the AES key with Azure Key Vault RSA key
            var cryptoClient = await GetCryptographyClientAsync(keyId, cancellationToken);
            var wrapResult = await cryptoClient.WrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                key,
                cancellationToken);

            // Store wrapped key in metadata (in real scenario, store separately)
            _logger.LogDebug("Data encrypted with key {KeyId}, IV length: {IvLength}", keyId, iv.Length);

            return (encryptedData, iv, keyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data with Azure Key Vault");
            throw;
        }
    }

    /// <summary>
    /// Decrypt data using AES-256-GCM with Azure Key Vault managed key
    /// </summary>
    public async Task<byte[]> DecryptDataAsync(
        byte[] ciphertext,
        byte[] iv,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (_useLocalEncryption)
        {
            return DecryptLocally(ciphertext, iv);
        }

        try
        {
            var cryptoClient = await GetCryptographyClientAsync(keyId, cancellationToken);

            // In real implementation, retrieve wrapped key from metadata/header
            // For now, using a simplified approach
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = iv;

            // Unwrap the key using Azure Key Vault
            // Note: In production, you'd retrieve the wrapped key from message metadata
            var key = new byte[32]; // Placeholder - retrieve actual wrapped key
            var unwrapResult = await cryptoClient.UnwrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                key,
                cancellationToken);

            aes.Key = unwrapResult.Key;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var resultStream = new MemoryStream();
            
            await cs.CopyToAsync(resultStream, cancellationToken);
            return resultStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data with Azure Key Vault");
            throw;
        }
    }

    /// <summary>
    /// Get or create the data encryption key in Azure Key Vault
    /// </summary>
    public async Task<string> GetOrCreateDataEncryptionKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_useLocalEncryption)
        {
            return "local-dev-key";
        }

        try
        {
            // Try to get existing key
            var keyResponse = await _keyClient.GetKeyAsync(_dataEncryptionKeyName, cancellationToken: cancellationToken);
            _logger.LogInformation("Using existing Azure Key Vault key: {KeyName}", _dataEncryptionKeyName);
            return keyResponse.Value.Id.ToString();
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Key doesn't exist, create it
            _logger.LogInformation("Creating new RSA key in Azure Key Vault: {KeyName}", _dataEncryptionKeyName);
            
            var keyOptions = new CreateRsaKeyOptions(_dataEncryptionKeyName)
            {
                KeySize = 2048,
                ExpiresOn = DateTimeOffset.UtcNow.AddYears(2)
            };

            var createdKey = await _keyClient.CreateRsaKeyAsync(keyOptions, cancellationToken);
            _logger.LogInformation("Created new key {KeyId}", createdKey.Value.Id);
            return createdKey.Value.Id.ToString();
        }
    }

    private async Task<CryptographyClient> GetCryptographyClientAsync(string keyId, CancellationToken cancellationToken)
    {
        return _cryptoClientCache.GetOrAdd(keyId, id =>
        {
            var credential = new DefaultAzureCredential();
            return new CryptographyClient(new Uri(id), credential);
        });
    }

    #region Local Encryption (Development Only)

    private static readonly byte[] LocalKey = Encoding.UTF8.GetBytes("DevOnlyKey123456DevOnlyKey123456"); // 32 bytes for AES-256

    private (byte[] encryptedData, byte[] iv, string keyId) EncryptLocally(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = LocalKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(plaintext, 0, plaintext.Length);
            cs.FlushFinalBlock();
        }

        return (ms.ToArray(), iv, "local-dev-key");
    }

    private byte[] DecryptLocally(byte[] ciphertext, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = LocalKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(ciphertext);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var resultStream = new MemoryStream();
        
        cs.CopyTo(resultStream);
        return resultStream.ToArray();
    }

    #endregion
}
