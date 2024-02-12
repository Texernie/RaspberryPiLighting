using Iot.Device.Ws28xx;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Device.Gpio;
using System.Device.Spi;
using System.Drawing;

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

        private SpiDevice _spi1;
        private SpiDevice _spi2;
        private SpiDevice _spi3;
        private SpiDevice _spi4;

        const int gpio1Pin = 6; // Header pin 31
        private GpioController _gpio1;

        const int gpio2Pin = 13; // Header pin 33
        private double _lightLevel = -1;

        public LightingExecutor(IOptionsMonitor<LightingConfiguration> options, IHostApplicationLifetime hostApplicationLifetime)
        {
            _optionsMonitor = options;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        ~LightingExecutor()
        {
            _spi1.Dispose();
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
                var lightTask = measureLight(ct2);

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

                        Console.Write($"\rActual FPS: {actualFps:F3}     \tLightLevel: {_lightLevel:F2} uS          ");
                    }

                    await fpsTask;
                }

                await lightTask;
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
                _gpio1.Write(gpio1Pin, PinValue.Low);

                Console.WriteLine("LEDs cleared");
                var ts = (stopTime - startTime).TotalSeconds;
                var actualFps = loopCount / ts;
                Console.WriteLine($"Actual FPS was {actualFps} vs target fps of {_options.TargetFps}.");
            }
        }

        //https://pimylifeup.com/raspberry-pi-light-sensor/
        private async Task measureLight(CancellationToken ct2)
        {
            using var gpio2 = new GpioController();
            gpio2.OpenPin(gpio2Pin);

            while (!ct2.IsCancellationRequested)
            {
                gpio2.SetPinMode(gpio2Pin, PinMode.Output);
                gpio2.Write(gpio2Pin, PinValue.Low);

                await Task.Delay(TimeSpan.FromMilliseconds(250));

                var startTime = DateTime.UtcNow;
                gpio2.SetPinMode(gpio2Pin, PinMode.Input);

                while(gpio2.Read(gpio2Pin) == PinValue.Low)
                { }

                var stopTime = DateTime.UtcNow;

                _lightLevel = (stopTime - startTime).TotalMicroseconds;
            }
            gpio2.ClosePin(gpio2Pin);
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
            if (_spi1 == null)
            {
                Console.WriteLine("Creating spi1");
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

                _spi1 = SpiDevice.Create(settings);

                Console.WriteLine("Created spi1");
            }
            
            if (_spi2 == null)
            {
                Console.WriteLine("Creating spi2");

                var settings = new SpiConnectionSettings(5)
                {
                    ClockFrequency = 2_400_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8,
                };

                _spi2 = SpiDevice.Create(settings);

                Console.WriteLine("Created spi2");
            }

            if (_spi3 == null)
            {
                Console.WriteLine("Creating spi3");

                var settings = new SpiConnectionSettings(3)
                {
                    ClockFrequency = 2_400_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8,
                };

                _spi3 = SpiDevice.Create(settings);

                Console.WriteLine("Created spi3");
            }

            if (_spi4 == null)
            {
                Console.WriteLine("Creating spi4");

                var settings = new SpiConnectionSettings(6)
                {
                    ClockFrequency = 2_400_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8,
                };

                _spi4 = SpiDevice.Create(settings);

                Console.WriteLine("Created spi4");
            }

            if (_gpio1 == null)
            {
                Console.WriteLine("Creating gpio1");

                _gpio1 = new GpioController();
                _gpio1.OpenPin(gpio1Pin, PinMode.Output);

                Console.WriteLine("Created gpio1");
            }

            var ledStrip = new Ws2812b(_spi1, _options.NumberOfLEDs);
            var ledStrip2 = new Ws2812b(_spi2, _options.NumberOfLEDs);
            var ledStrip3 = new Ws2812b(_spi3, _options.NumberOfLEDs);
            var ledStrip4 = new Ws2812b(_spi4, _options.NumberOfLEDs);


            var img = ledStrip.Image;
            var img2 = ledStrip2.Image;
            var img3 = ledStrip3.Image;
            var img4 = ledStrip4.Image;
            for (int i = 0; i < _options.NumberOfLEDs; i++)
            {
                img.SetPixel(i, 0, _currentFrame[i]);
                img2.SetPixel(i, 0, _currentFrame[i]);
                img3.SetPixel(i, 0, _currentFrame[i]);
                img4.SetPixel(i, 0, _currentFrame[i]);
            }
            var t1 = Task.Run(() => ledStrip.Update());
            var t2 = Task.Run(() => ledStrip2.Update());
            var t3 = Task.Run(() => ledStrip3.Update());
            var t4 = Task.Run(() => ledStrip4.Update());

            Task.WhenAll(t1, t2, t3, t4).Wait();

            var v = DateTime.Now.Second % 2 == 0;

            _gpio1.Write(gpio1Pin, v ? PinValue.High : PinValue.Low);
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