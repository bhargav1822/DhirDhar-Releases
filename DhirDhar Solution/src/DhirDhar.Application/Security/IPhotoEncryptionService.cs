using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Security;

public interface IPhotoEncryptionService
{
    Task<string> EncryptAndStorePhotoAsync(string sourcePlaintextFilePath, string photoCategory = "borrower", CancellationToken cancellationToken = default);

    Task<Stream> DecryptPhotoToStreamAsync(string encryptedPhotoPath, CancellationToken cancellationToken = default);

    Task<byte[]> DecryptPhotoToBytesAsync(string encryptedPhotoPath, CancellationToken cancellationToken = default);

    bool IsPhotoEncrypted(string photoPath);
}
