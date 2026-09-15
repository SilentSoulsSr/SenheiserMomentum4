namespace SenheiserControl.Services;

public static class EqPresets
{
    public static readonly IReadOnlyList<(string Name, double[] GainsDb)> FiveBand =
    [
        ("Flat", [0, 0, 0, 0, 0]),
        ("Rock", [0, 2, 2.5, 1.5, -2]),
        ("Pop", [0, -2.5, 0, 2.5, 0]),
        ("Dance", [3.5, 2, -1.5, 1.5, 3]),
        ("Hip Hop", [3, 1.5, -1.5, 0, -1.5]),
        ("Classical", [-2, -1.5, 0, 3.5, 4]),
        ("Movie", [0, 0, 2, 2, -2]),
        ("Jazz", [-3.2, 0, 2.2, 2.2, 0])
    ];

    public static readonly IReadOnlyDictionary<int, string[]> BandLabels = new Dictionary<int, string[]>
    {
        [3] = ["Bass", "Mid", "Treble"],
        [5] = ["63 Hz", "250 Hz", "1k Hz", "4k Hz", "8k Hz"]
    };

    public static string[] LabelsFor(int bandCount) =>
        BandLabels.TryGetValue(bandCount, out string[]? labels)
            ? labels
            : [.. Enumerable.Range(1, bandCount).Select(i => $"Band {i}")];
}
