using System;
using Microsoft.Extensions.Configuration;

namespace MathIA;

class FilterData
{
    public static void Main(string[] args)
    {
        FilterSettings settings = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("Settings.json").Build()
            .GetSection("FilterSettings")
            .Get<FilterSettings>()
            ?? throw new Exception("No settings found.");

        foreach(string path in Directory.GetFiles(settings.RawDataFolder))
        {
            Console.WriteLine(path);
        }
    }
}

internal class FilteredDataResult
{
    public string DateGathered { get; set; } = null!;
    public string DepartureDate
}

internal class FilterSettings
{
    public string Airline { get; set; } = null!;
    public string StopCount { get; set; } = null!;
    public string CabinClass { get; set; } = null!;
    public string RawDataFolder { get; set; } = null!;
    public string OutputLocation { get; set; } = null!;
}