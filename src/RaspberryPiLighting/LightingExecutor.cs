using Iot.Device.Ws28xx;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Device.Spi;
using System.Drawing;

namespace RaspberryPiLighting
{
    internal partial class LightingExecutor
    {
        private readonly LightingConfiguration _options;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private int _startingPatternIndex;
        private Color[] _repeatingPattern;
        private Color[] _currentFrame;

        private SpiDevice _spi;

        public LightingExecutor(IOptionsMonitor<LightingConfiguration> options, IHostApplicationLifetime hostApplicationLifetime)
        {
            _options = options.CurrentValue;
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        ~LightingExecutor()
        {
            _spi.Dispose();
        }

        internal async Task ExecutePatternAsync()
        {
            Console.WriteLine("ExecutePatternAsync");
            var ct2 = _hostApplicationLifetime.ApplicationStopping;

            if (_options.PatternToRepeat == null)
                return;

            _startingPatternIndex = 0;

            _repeatingPattern = _options.PatternToRepeat
                .Select(x => int.Parse(x, System.Globalization.NumberStyles.HexNumber))
                .Select(x => Color.FromArgb(x))
                .ToArray();

            _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x=> Color.Black).ToArray();

            try
            {
                while (!ct2.IsCancellationRequested)
                {
                    populateFrame();
                    displayFrame();
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.FrameDelayInMs), ct2);
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
            finally
            {
                Console.WriteLine("Exiting Executor");
                _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x => Color.Black).ToArray();
                displayFrame();
                Console.WriteLine("LEDs cleared.");
            }
        }

        private void displayFrame()
        {
            if (_spi == null)
            {
                Console.WriteLine("Creating spi");
                var settings = new SpiConnectionSettings(0)
                {
                    ClockFrequency = 2_400_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8
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
                foreach(var fi in _options.PatternToPixelMap[i]) 
                    _currentFrame[fi] = c;
            }

            _startingPatternIndex -= _options.PatternOffsetPerFrame;

            if (_startingPatternIndex < 0)
                _startingPatternIndex += _repeatingPattern.Length;

            _startingPatternIndex %= _repeatingPattern.Length;
        }
    }
}