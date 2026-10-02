namespace PersonalAI.Web.Models;

public static class DevelopmentLeaseResourceTypes
{
    public const string Repository = "repository";
    public const string Branch = "branch";
    public const string File = "file";
}

public sealed record DevelopmentLease(
    Guid Id,
    string WorkspaceId,
    string OwnerId,
    string ResourceType,
    string RepositoryPath,
    string? Branch,
    string? FilePath,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset UpdatedAt);

public sealed record AcquireDevelopmentLeaseRequest(
    string OwnerId,
    string ResourceType,
    string RepositoryPath,
    string? Branch = null,
    string? FilePath = null,
    int LeaseSeconds = 300);

public sealed record RenewDevelopmentLeaseRequest(
    Guid LeaseId,
    string OwnerId,
    int LeaseSeconds = 300);

public sealed record ReleaseDevelopmentLeaseRequest(
    Guid LeaseId,
    string OwnerId);

public sealed record DevelopmentLeaseStatus(
    string Version,
    string WorkspaceId,
    int ActiveLeases,
    int ExpiredLeasesIgnored,
    int MinimumLeaseSeconds,
    int MaximumLeaseSeconds,
    bool RepositoryScopeBlocksChildren,
    bool DuplicateWriterBlocked,
    bool ExpiryEnabled);

public sealed class DevelopmentLeaseValidationException(string message)
    : Exception(message);

public sealed class DevelopmentLeaseConflictException(string message)
    : Exception(message);
