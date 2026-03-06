namespace Sekta.Shared.DTOs;

public record PublicKeyDto(Guid UserId, string PublicKeyBase64);

public record EncryptedMessageDto(
    string CiphertextBase64,
    string NonceBase64,
    string TagBase64,
    string SenderPublicKeyBase64
);
