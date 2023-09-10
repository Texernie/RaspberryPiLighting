using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace RaspberryPiLighting
{
    internal partial class Program
    {
        private static IHostApplicationLifetime _ahl;

        static async Task Main(string[] args)
        {

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((bc, s) =>
                {
                    s.Configure<LightingConfiguration>(bc.Configuration.GetSection("LightingConfiguration"));
                    s.AddTransient<LightingExecutor>();
                    s.PostConfigure<LightingConfiguration>(x =>
                    {
                        var b = bc.Configuration
                                  .GetSection("LightingConfiguration")
                                  .GetChildren()
                                  .Where(y => y.Path.EndsWith("LedDefinitions"))
                                  .SelectMany(y => y.GetChildren())
                                  .ToArray();

                        var propIndex  = typeof(LedRange).GetProperty("Index");
                        var propLedStart  = typeof(LedRange).GetProperty("LedStart");
                        var propLedEnd  = typeof(LedRange).GetProperty("LedEnd");
                        var propPatternStart = typeof(LedRange).GetProperty("PatternStart");

                        var newLedRanges = new List<LedRange>();

                        foreach (var q in b)
                        {
                            var obj = new LedRange();

                            foreach(var p in q.GetChildren())
                            {
                                var prop = p.Key switch
                                {
                                    "Index" => propIndex,
                                    "LedStart" => propLedStart,
                                    "LedEnd" => propLedEnd,
                                    "PatternStart" => propPatternStart,
                                    _ => null,
                                };

                                prop?.SetValue(obj, int.Parse(p.Value));
                            }
                            newLedRanges.Add(obj);
                        }

                        x.LedDefinitions = newLedRanges.ToArray();
                    });
                });

            var host = builder.Build();

            _ahl = host.Services.GetRequiredService<IHostApplicationLifetime>();

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs args)
            {
                Console.WriteLine("CANCEL received! Stopping!");
                args.Cancel = true;
                _ahl.StopApplication();
                Console.WriteLine("CANCEL received! Stopped!");
            };

            var config = host.Services.GetRequiredService<IOptionsMonitor<LightingConfiguration>>();
            var ms = host.Services.GetRequiredService<LightingExecutor>();

            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(config.CurrentValue));
            Console.WriteLine(new string('-', 25));

            if (config.CurrentValue.DoFire)
                await ms.ExecuteFireAsync();
            else
                await ms.ExecutePatternAsync();

            Console.WriteLine("Exited");
        }
    }
}