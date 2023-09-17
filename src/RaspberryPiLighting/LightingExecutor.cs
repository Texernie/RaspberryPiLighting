using Iot.Device.Ws28xx;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Device.Spi;
using System.Drawing;

/*
 * pscp -pw d780K97e-19d C:/Users/khend/source/repos/RaspberryPiLighting/src/RaspberryPiLighting/bin/Debug/net7.0/publish/* khend@rpi1.local:/home/khend/VSLinuxDbg/RaspberryPiLighting/
 * 
 * cd ~/VSLinuxDbg/RaspberryPiLighting && mv RaspberryPiLighting RaspberryPiLighting.exe && chmod 777 RaspberryPiLighting.exe && ./RaspberryPiLighting.exe
 */

namespace RaspberryPiLighting
{
    internal partial class LightingExecutor
    {
        private readonly IOptionsMonitor<LightingConfiguration> _optionsMonitor;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private LightingConfiguration _options;
        private int _startingPatternIndex;
        private Color[] _repeatingPattern;
        private Color[] _currentFrame;
        private Dictionary<int, int> _ledToPatternMap;

        private SpiDevice _spi;

        public LightingExecutor(IOptionsMonitor<LightingConfiguration> options, IHostApplicationLifetime hostApplicationLifetime)
        {
            _optionsMonitor = options;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        ~LightingExecutor()
        {
            _spi.Dispose();
        }

        internal async Task ExecutePatternAsync()
        {
            _options = _optionsMonitor.CurrentValue;

            Console.WriteLine("ExecutePatternAsync");
            var ct2 = _hostApplicationLifetime.ApplicationStopping;

            if (_options.PatternToRepeat == null)
            {
                Console.WriteLine("No pattern to repeat");
                return;
            }

            if (_options.PatternToRepeat.Length == 0)
            {
                Console.WriteLine("Pattern to repeat was empty");
                return;
            }

            if (_options.LedDefinitions == null)
            {
                Console.WriteLine("Led map was missing");
                return;
            }

            if (_options.LedDefinitions.Length == 0)
            {
                Console.WriteLine("Led map was empty");
                return;
            }

            _startingPatternIndex = 0;

            _repeatingPattern = _options.PatternToRepeat
                .Select(x => int.Parse(x, System.Globalization.NumberStyles.HexNumber))
                .Select(x => Color.FromArgb(x))
                .ToArray();

            populateLedMap();

            _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x=> Color.Black).ToArray();
            var msPF = 1.0 / _options.TargetFps * 1000.0;

            var startTime = DateTime.UtcNow;
            var loopCount = 0;

            try
            {
                while (!ct2.IsCancellationRequested)
                {
                    var fpsTask = Task.Delay(TimeSpan.FromMilliseconds(msPF), ct2);

                    populateFrame();
                    displayFrame();
                    loopCount++;

                    if (loopCount == 1)
                        Console.WriteLine($"Target FPS is {_options.TargetFps}"); 
                    if (loopCount % 10 == 0)
                    {
                        var stopTime = DateTime.UtcNow;
                        var ts = (stopTime - startTime).TotalSeconds;
                        var actualFps = loopCount / ts;

                        Console.Write($"\rActual FPS: {actualFps:F3}     ");
                    }

                    await fpsTask;
                }
            }
            catch(TaskCanceledException) 
            { 
                //Expected
            }
            catch(ThreadAbortException)
            {
                //Expected
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                var stopTime = DateTime.UtcNow;
                Console.WriteLine("Exiting Pattern Executor");
                _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x => Color.Black).ToArray();
                displayFrame();
                Console.WriteLine("LEDs cleared");
                var ts = (stopTime - startTime).TotalSeconds;
                var actualFps = loopCount / ts;
                Console.WriteLine($"Actual FPS was {actualFps} vs target fps of {_options.TargetFps}.");
            }
        }

        private void populateLedMap()
        {
            //Initialize all to "black"
            _ledToPatternMap = Enumerable.Range(0, _options.NumberOfLEDs)
                .ToDictionary(x => x, x => -1);

            foreach(var lr in _options.LedDefinitions.OrderBy(x => x.Index))
            {
                if (lr.LedStart < lr.LedEnd)
                {
                    var currentLedIndex = lr.LedStart;
                    var currentPatternIndex = 0;

                    while (currentLedIndex <= lr.LedEnd)
                    {
                        _ledToPatternMap[currentLedIndex++] = currentPatternIndex++ % _repeatingPattern.Length;
                    }
                }
                else
                {
                    var currentLedIndex = lr.LedStart;
                    var currentPatternIndex = 0;

                    while (currentLedIndex >= lr.LedEnd)
                    {
                        _ledToPatternMap[currentLedIndex--] = currentPatternIndex++ % _repeatingPattern.Length;
                    }

                }
            }
        }

        private void displayFrame()
        {
            if (_spi == null)
            {
                Console.WriteLine("Creating spi");
                // https://pinout.xyz/pinout/spi
                // https://blog.stabel.family/raspberry-pi-4-multiple-spis-and-the-device-tree/
                // Bus 0 = 19 (MOSI)      spi0-0cs
                // Bus 1 = 38 (GPIO 20)   spi1-1cs
                // Bus 2 = ?              spi2-1cs NOT FOUND
                // Bus 3 = 03 (SCL1)      spi3-1cs
                // Bus 4 = 31             spi4-1cs ERROR
                // Bus 5 = 08 (TXD0)      spi5-1cs
                // Bus 6 = 35             spi6-1cs NOT FOUND
                var settings = new SpiConnectionSettings(0)
                {
                    ClockFrequency = 2_400_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8,
                };

                _spi = SpiDevice.Create(settings);

                Console.WriteLine("Created spi");
            }
            
            var ledStrip = new Ws2812b(_spi, _options.NumberOfLEDs);

            var img = ledStrip.Image;
            for (int i = 0; i < _options.NumberOfLEDs; i++)
            {
                img.SetPixel(i, 0, _currentFrame[i]);
            }
            ledStrip.Update();
        }

        private void populateFrame()
        {
            for (int i = 0; i < _repeatingPattern.Length; i++)
            {
                var c = _repeatingPattern[(i + _startingPatternIndex) % _repeatingPattern.Length];
                foreach(var fi in _ledToPatternMap.Where(x=> x.Value == i).Select(x=>x.Key))
                    _currentFrame[fi] = c;
            }

            _startingPatternIndex -= _options.PatternOffsetPerFrame;

            if (_startingPatternIndex < 0)
                _startingPatternIndex += _repeatingPattern.Length;

            _startingPatternIndex %= _repeatingPattern.Length;
        }
    }
}