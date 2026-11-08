namespace WarehouseRent.Infrastructure.Documents;

public sealed class PrivateDocument
{
    public Guid Id { get; set; }
    public Guid LeaseId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public required string StorageKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required string Sha256 { get; set; }
    public byte[] Content { get; set; } = [];
    public required string UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
