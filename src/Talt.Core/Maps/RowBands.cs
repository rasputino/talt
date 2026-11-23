namespace Talt.Core.Maps;

/// <summary>
/// Runs work over fixed-size bands of rows. The bands depend only on the map and the band height,
/// never on the number of cores, so a stage whose bands are independent gives identical results
/// with any thread count (constitution III, research R9).
/// </summary>
internal static class RowBands
{
    public static int Count(int rows, int bandRows) => (rows + bandRows - 1) / bandRows;

    /// <summary>Calls <paramref name="body"/> with (band index, first row, row after the last) for every band.</summary>
    public static void For(int rows, int bandRows, int? maxDegreeOfParallelism, Action<int, int, int> body, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        if (maxDegreeOfParallelism is { } limit)
        {
            options.MaxDegreeOfParallelism = limit;
        }

        Parallel.For(0, Count(rows, bandRows), options, band =>
        {
            var start = band * bandRows;
            body(band, start, Math.Min(rows, start + bandRows));
        });
    }
}
