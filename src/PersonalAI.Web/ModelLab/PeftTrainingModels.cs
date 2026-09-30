namespace PersonalAI.Web.ModelLab;

public static class TrainingMethods
{
    public const string Full = "full";
    public const string NaiveBayes = "naive-bayes";
    public const string LoRa = "lora";
    public const string QLoRa = "qlora";
    public const string Adapter = "adapter";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Full, NaiveBayes, LoRa, QLoRa, Adapter],
            StringComparer.OrdinalIgnoreCase);

    public static bool IsPeft(string method) =>
        method is LoRa or QLoRa or Adapter;
}

public sealed record PeftTrainingConfiguration(
    int Rank,
    double Alpha,
    double Dropout,
    IReadOnlyList<string> TargetModules,
    double LearningRate,
    int Epochs,
    int BatchSize,
    int GradientAccumulationSteps,
    int MaximumSequenceLength);
