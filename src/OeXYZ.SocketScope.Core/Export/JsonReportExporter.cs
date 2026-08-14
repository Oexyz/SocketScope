using System.Text.Json;
using System.Text.Json.Serialization;

namespace OeXYZ.SocketScope.Core.Export;

public static class JsonReportExporter
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Export(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.SerializeToUtf8Bytes(report, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
