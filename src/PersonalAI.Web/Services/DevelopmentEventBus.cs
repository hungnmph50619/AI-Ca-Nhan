using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentEventBus
{
    IReadOnlyList<DevelopmentEventEnvelope> GetRecent(int maximum = 100);
    DevelopmentEventEnvelope Publish(DevelopmentEventEnvelope envelope);
    bool HasDelivery(string deliveryId);
}

public sealed class DevelopmentEventBus(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace) : IDevelopmentEventBus
{
    public const int MaximumEvents = 2_000;
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<DevelopmentEventEnvelope> GetRecent(int maximum = 100)
    {
        var safeMaximum = Math.Clamp(maximum, 1, 500);
        lock (_gate)
        {
            return Load()
                .OrderByDescending(x => x.ReceivedAt)
                .Take(safeMaximum)
                .ToArray();
        }
    }

    public bool HasDelivery(string deliveryId)
    {
        var normalized = NormalizeDelivery(deliveryId);
        lock (_gate)
        {
            return Load().Any(x =>
                x.DeliveryId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DevelopmentEventEnvelope Publish(DevelopmentEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var delivery = NormalizeDelivery(envelope.DeliveryId);

        lock (_gate)
        {
            var all = Load();
            if (all.Any(x =>
                x.DeliveryId.Equals(delivery, StringComparison.OrdinalIgnoreCase)))
            {
                return all.First(x =>
                    x.DeliveryId.Equals(delivery, StringComparison.OrdinalIgnoreCase));
            }

            all.Add(envelope with { DeliveryId = delivery });

            if (all.Count > MaximumEvents)
            {
                all = all
                    .OrderByDescending(x => x.ReceivedAt)
                    .Take(MaximumEvents)
                    .OrderBy(x => x.ReceivedAt)
                    .ToList();
            }

            Save(all);
            return envelope with { DeliveryId = delivery };
        }
    }

    private List<DevelopmentEventEnvelope> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentEventEnvelope>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentEventEnvelope> events)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(events, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"development-events-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:EventRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Events");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string NormalizeDelivery(string? value)
    {
        var delivery = (value ?? string.Empty).Trim();
        if (delivery.Length is < 1 or > 120 ||
            delivery.Any(c =>
                !(char.IsLetterOrDigit(c) || c is '-' or '_')))
        {
            throw new GitHubWebhookValidationException(
                "X-GitHub-Delivery không hợp lệ.");
        }
        return delivery;
    }
}
