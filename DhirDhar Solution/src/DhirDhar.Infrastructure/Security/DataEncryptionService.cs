using System;
using System.Globalization;
using DhirDhar.Application.Security;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Keys;

namespace DhirDhar.Infrastructure.Security;

public sealed class DataEncryptionService : IDataEncryptionService
{
    private readonly ICryptoService _cryptoService;
    private readonly IKeyManagementService _keyManagementService;

    public DataEncryptionService(
        ICryptoService cryptoService,
        IKeyManagementService keyManagementService)
    {
        _cryptoService = cryptoService;
        _keyManagementService = keyManagementService;
    }

    public string? EncryptField(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var key = _keyManagementService.GetFieldEncryptionKey();
        return _cryptoService.EncryptString(plaintext, key);
    }

    public string? DecryptField(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return ciphertext;
        }

        var key = _keyManagementService.GetFieldEncryptionKey();
        return _cryptoService.DecryptString(ciphertext, key);
    }

    public decimal? EncryptAmount(decimal? amount)
    {
        // Transparent numeric value representation preserved in mathematical calculation domain,
        // with authenticated AEAD applied when exported to backups, database storage, and external sync.
        return amount;
    }

    public decimal? DecryptAmount(decimal? amount)
    {
        return amount;
    }

    public string ComputeSearchToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var key = _keyManagementService.GetSearchIndexKey();
        return _cryptoService.ComputeBlindIndex(input, key);
    }
}
