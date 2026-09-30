using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDataCleaningService
{
    DataCleaningReport Clean(CleanModelLabDatasetRequest request);
}

public sealed partial class DataCleaningService(
    IModelLabDatasetStore datasets,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDataCleaningService
{
    public const int MaximumReportedRemovals = 250;
    public const int MaximumDepth = 32;
    public const int MaximumStringCharactersPerItem = 200_000;

    private static readonly HashSet<string> SecretPropertyNames = new(
        [
            "password",
            "passwd",
            "pwd",
            "secret",
            "clientsecret",
            "client_secret",
            "apikey",
            "api_key",
            "access_token",
            "refreshtoken",
            "refresh_token",
            "authorization",
            "privatekey",
            "private_key"
        ],
        StringComparer.OrdinalIgnoreCase);

    public DataCleaningReport Clean(CleanModelLabDatasetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceVersion < 1)
            throw new DataCleaningValidationException(
                "SourceVersion phải lớn hơn hoặc bằng 1.");

        if (request.CreateCleanedVersion &&
            !request.ConfirmCreateCleanedVersion)
        {
            throw new DataCleaningValidationException(
                "Tạo cleaned version cần confirmCreateCleanedVersion=true.");
        }

        var source = datasets.GetVersion(
            request.DatasetId,
            request.SourceVersion)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy dataset version nguồn.");

        if (!string.Equals(
            source.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new DataCleaningValidationException(
                "Dataset version không thuộc workspace hiện tại.");
        }

        var kept = new List<JsonElement>(source.Items.Count);
        var removals = new List<DataCleaningRemoval>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var duplicates = 0;
        var bad = 0;
        var secrets = 0;
        var corrupt = 0;

        for (var index = 0; index < source.Items.Count; index++)
        {
            var item = source.Items[index];

            if (IsCorrupt(item, out var corruptReason))
            {
                corrupt++;
                AddRemoval(
                    removals,
                    index,
                    corruptReason,
                    DataCleaningCategories.Corrupt,
                    Fingerprint(item));
                continue;
            }

            if (ContainsSecret(item, out var secretReason))
            {
                secrets++;
                AddRemoval(
                    removals,
                    index,
                    secretReason,
                    DataCleaningCategories.Secret,
                    Fingerprint(item));
                continue;
            }

            if (IsBadExample(item, out var badReason))
            {
                bad++;
                AddRemoval(
                    removals,
                    index,
                    badReason,
                    DataCleaningCategories.BadExample,
                    Fingerprint(item));
                continue;
            }

            var canonical = Canonicalize(item);
            var fingerprint = Sha256(canonical);
            if (!seen.Add(fingerprint))
            {
                duplicates++;
                AddRemoval(
                    removals,
                    index,
                    "Exact canonical duplicate of an earlier item.",
                    DataCleaningCategories.Duplicate,
                    fingerprint);
                continue;
            }

            kept.Add(item.Clone());
        }

        int? createdVersion = null;
        string? createdSha = null;

        if (request.CreateCleanedVersion)
        {
            var note = string.IsNullOrWhiteSpace(request.VersionNote)
                ? $"Data Cleaning from v{source.Version}: removed {source.Items.Count - kept.Count} item(s)."
                : request.VersionNote.Trim();

            var version = datasets.CreateVersion(
                source.DatasetId,
                new CreateModelLabDatasetVersionRequest(
                    kept,
                    note,
                    request.ExpectedLatestVersion));

            createdVersion = version.Version;
            createdSha = version.ContentSha256;

            audit.Record(
                AuditAgents.User,
                "model-lab.data-cleaning.create-version",
                $"model-lab-dataset:{source.DatasetId}:v{version.Version}",
                $"source:v{source.Version};removed:{source.Items.Count - kept.Count}",
                AuditResults.Succeeded);
        }
        else
        {
            audit.Record(
                AuditAgents.User,
                "model-lab.data-cleaning.preview",
                $"model-lab-dataset:{source.DatasetId}:v{source.Version}",
                $"removed:{source.Items.Count - kept.Count}",
                AuditResults.Succeeded);
        }

        return new DataCleaningReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            source.DatasetId,
            source.Version,
            source.Items.Count,
            kept.Count,
            source.Items.Count - kept.Count,
            duplicates,
            bad,
            secrets,
            corrupt,
            removals,
            PreviewOnly: !request.CreateCleanedVersion,
            createdVersion,
            createdSha,
            DateTimeOffset.UtcNow);
    }

    private static bool IsBadExample(
        JsonElement item,
        out string reason)
    {
        switch (item.ValueKind)
        {
            case JsonValueKind.Null:
                reason = "Top-level item is null.";
                return true;

            case JsonValueKind.String:
                if (string.IsNullOrWhiteSpace(item.GetString()))
                {
                    reason = "Top-level string is empty or whitespace.";
                    return true;
                }
                break;

            case JsonValueKind.Array:
                if (item.GetArrayLength() == 0)
                {
                    reason = "Top-level array is empty.";
                    return true;
                }
                break;

            case JsonValueKind.Object:
                if (!item.EnumerateObject().Any())
                {
                    reason = "Top-level object is empty.";
                    return true;
                }
                break;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsCorrupt(
        JsonElement item,
        out string reason)
    {
        if (item.ValueKind == JsonValueKind.Undefined)
        {
            reason = "JSON item is Undefined.";
            return true;
        }

        var totalStringCharacters = 0;
        if (!ValidateStructure(
            item,
            depth: 0,
            ref totalStringCharacters,
            out reason))
        {
            return true;
        }

        return false;
    }

    private static bool ValidateStructure(
        JsonElement element,
        int depth,
        ref int totalStringCharacters,
        out string reason)
    {
        if (depth > MaximumDepth)
        {
            reason = $"JSON depth exceeds {MaximumDepth}.";
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (HasSuspiciousText(property.Name))
                    {
                        reason = "Property name contains replacement/control characters.";
                        return false;
                    }

                    if (!ValidateStructure(
                        property.Value,
                        depth + 1,
                        ref totalStringCharacters,
                        out reason))
                    {
                        return false;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (!ValidateStructure(
                        child,
                        depth + 1,
                        ref totalStringCharacters,
                        out reason))
                    {
                        return false;
                    }
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                totalStringCharacters += value.Length;
                if (totalStringCharacters > MaximumStringCharactersPerItem)
                {
                    reason =
                        $"Total string content exceeds {MaximumStringCharactersPerItem} characters.";
                    return false;
                }

                if (HasSuspiciousText(value))
                {
                    reason = "String contains replacement/control characters.";
                    return false;
                }
                break;
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasSuspiciousText(string value)
    {
        foreach (var character in value)
        {
            if (character == '\uFFFD')
                return true;

            if (char.IsControl(character) &&
                character is not ('\r' or '\n' or '\t'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSecret(
        JsonElement item,
        out string reason)
    {
        if (ScanSecret(item, out reason))
            return true;

        reason = string.Empty;
        return false;
    }

    private static bool ScanSecret(
        JsonElement element,
        out string reason)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (SecretPropertyNames.Contains(
                        NormalizeSecretPropertyName(property.Name)))
                    {
                        reason =
                            $"Sensitive field name detected: '{property.Name}'.";
                        return true;
                    }

                    if (ScanSecret(property.Value, out reason))
                        return true;
                }
                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (ScanSecret(child, out reason))
                        return true;
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if (PrivateKeyRegex().IsMatch(value))
                {
                    reason = "Private key material detected.";
                    return true;
                }

                if (BearerTokenRegex().IsMatch(value))
                {
                    reason = "Bearer token pattern detected.";
                    return true;
                }

                if (KnownApiKeyRegex().IsMatch(value))
                {
                    reason = "High-confidence API key pattern detected.";
                    return true;
                }
                break;
        }

        reason = string.Empty;
        return false;
    }

    private static string NormalizeSecretPropertyName(string value) =>
        new(value
            .Where(character =>
                char.IsLetterOrDigit(character) || character == '_')
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string Fingerprint(JsonElement item)
    {
        try
        {
            return Sha256(Canonicalize(item));
        }
        catch
        {
            return "unavailable";
        }
    }

    private static void AddRemoval(
        ICollection<DataCleaningRemoval> removals,
        int sourceIndex,
        string reason,
        string category,
        string fingerprint)
    {
        if (removals.Count >= MaximumReportedRemovals)
            return;

        removals.Add(new DataCleaningRemoval(
            sourceIndex,
            reason,
            category,
            fingerprint));
    }

    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                    .EnumerateObject()
                    .OrderBy(
                        property => property.Name,
                        StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray())
                    WriteCanonical(writer, child);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    [GeneratedRegex(
        @"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{16,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        @"\b(?:sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{30,})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex KnownApiKeyRegex();
}
