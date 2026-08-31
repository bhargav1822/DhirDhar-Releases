using System;

namespace DhirDhar.Application.Security;

public interface IDataEncryptionService
{
    string? EncryptField(string? plaintext);

    string? DecryptField(string? ciphertext);

    decimal? EncryptAmount(decimal? amount);

    decimal? DecryptAmount(decimal? amount);

    string ComputeSearchToken(string? input);
}
