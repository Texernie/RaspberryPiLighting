using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            var ms = host.Services.GetRequiredService<LightingExecutor>();
            await ms.ExecutePatternAsync();
            //await ms.ExecuteFireAsync();

            /*
             * cd ~/VSLinuxDbg/RaspberryPiLighting && mv RaspberryPiLighting RaspberryPiLighting.exe && chmod 777 RaspberryPiLighting.exe && ./RaspberryPiLighting.exe
             */
        }
    }
}