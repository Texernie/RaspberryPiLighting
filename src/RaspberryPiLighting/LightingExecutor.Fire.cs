using System.Drawing;

namespace RaspberryPiLighting
{
    internal partial class LightingExecutor
    {
        /*
         * pscp -pw d780K97e-19d C:/Users/khend/source/repos/RaspberryPiLighting/src/RaspberryPiLighting/bin/Debug/net7.0/publish/* khend@rpi1.local:/home/khend/VSLinuxDbg/RaspberryPiLighting/
         * 
         * cd ~/VSLinuxDbg/RaspberryPiLighting && mv RaspberryPiLighting RaspberryPiLighting.exe && chmod 777 RaspberryPiLighting.exe && ./RaspberryPiLighting.exe
         */

        internal async Task ExecuteFireAsync()
        {
            Console.WriteLine("ExecuteFireAsync");
            _options = _optionsMonitor.CurrentValue;
            var ct2 = _hostApplicationLifetime.ApplicationStopping;

            _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x => Color.Black).ToArray();

            _size = _mirrored
                ? _options.NumberOfLEDs / 2
                : _options.NumberOfLEDs;

            _heat = Enumerable.Range(0, _size).Select(x => (byte)0).ToArray();
            var msPF = 1.0 / _options.TargetFps * 1000;

            var startTime = DateTime.UtcNow;
            var loopCount = 0;

            try
            {
                while (!ct2.IsCancellationRequested)
                {
                    var fpsTask = Task.Delay(TimeSpan.FromMilliseconds(msPF), ct2);

                    populateFireFrame();
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
            catch (TaskCanceledException)
            {
                //Expected
            }
            catch (ThreadAbortException)
            {
                //Expected
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                var stopTime = DateTime.UtcNow;
                Console.WriteLine("Exiting Fire Executor");
                _currentFrame = Enumerable.Range(0, _options.NumberOfLEDs).Select(x => Color.Black).ToArray();
                displayFrame();
                Console.WriteLine("LEDs cleared.");
                var ts = (stopTime - startTime).TotalSeconds;
                var actualFps = loopCount / ts;
                Console.WriteLine($"Actual FPS was {actualFps} vs target fps of {_options.TargetFps}.");

            }
        }

        private int _cooling => _options.Cooling;           // Rate at which the pixels cool off
        private int _sparks => _options.Sparks;             // How many sparks will be attempted each frame
        private int _sparkHeight => _options.SparkHeight;   // If created, max height for a spark
        private int _sparking => _options.Sparking;         // Probability of a spark each attempt
        private bool _reversed => _options.Reversed;        // if reversed, we draw from 0 outwards
        private bool _mirrored => _options.Mirrored;        // if mirrored, we split and duplicate the drawing
        private int _size;
        private byte[] _heat;

        const byte BlendSelf = 2;
        const byte VBlendNeighbor1 = 3;
        const byte VBlendNeighbor2 = 2;
        const byte VBlendNeighbor3 = 1;
        const byte VBlendTotal = (BlendSelf + VBlendNeighbor1 + VBlendNeighbor2 + VBlendNeighbor3);

        const byte HBlendNeighbor1 = 3;
        const byte HBlendNeighbor2 = 2;
        const byte HBlendNeighbor3 = 1;
        const byte HBlendTotal = (BlendSelf + 2 * (VBlendNeighbor1 + VBlendNeighbor2 + VBlendNeighbor3));

        //private Color[] _currentFrame;

        private void populateFireFrame()
        {
            var r = Random.Shared;

            //Cool off pixels
            for (int i = 0; i < _size; i++)
                _heat[i] = (byte) Math.Max(0, _heat[i] - r.Next(0, (_cooling * 10 / _size) + 2));

            //Diffuse heat
            if (_options.HorizontalFire)
            {
                for (int i = 0; i < _size; i++)
                    _heat[i] = (byte)((_heat[i] * BlendSelf +
                                        _heat[(i + 1) % _size] * HBlendNeighbor1 +
                                        _heat[(i + 2) % _size] * HBlendNeighbor2 +
                                        _heat[(i + 3) % _size] * HBlendNeighbor3 +
                                        _heat[(i - 1 + _size) % _size] * HBlendNeighbor1 +
                                        _heat[(i - 2 + _size) % _size] * HBlendNeighbor2 +
                                        _heat[(i - 3 + _size) % _size] * HBlendNeighbor3)
                                        / HBlendTotal);
            }
            else
            {
                for (int i = 0; i < _size; i++)
                    _heat[i] = (byte)((_heat[i] * BlendSelf +
                                        _heat[(i + 1) % _size] * VBlendNeighbor1 +
                                        _heat[(i + 2) % _size] * VBlendNeighbor2 +
                                        _heat[(i + 3) % _size] * VBlendNeighbor3)
                                        / VBlendTotal);
            }

            // Randomly ignite some new sparks
            for (int i = 0; i < _sparks; i++)
            {
                if (r.Next(255) < _sparking)
                {
                    var y = _size - 1 - r.Next(_sparkHeight);
                    _heat[y] += (byte)r.Next(160, 255);         // Can roll over which looks good?
                }
            }

            // Transfer heat values to frame
            for (int i = 0; i < _size; i++)
            {
                var color = heatColor(_heat[i]);
                var j = _reversed ? (_size - 1 - i) : i;
                _currentFrame[j] = color;
                if (_mirrored)
                {
                    var idx = !_reversed ? (2 * _size - 1 - i) : _size + i;
                    _currentFrame[idx] = color;
                }
            }
        }

        private static Color heatColor(byte v)
        {
            int t192 = scale8_video(v, 191);

            int heatramp = t192 & 0x3F; // 0..63
            heatramp <<= 2;  // scale up to 0..252

            var r = 0;
            var g = 0;
            var b = 0;

            if ((t192 & 0x80) != 0)
            {
                //Hottest 3rd
                r = 255;
                g = 255;
                b = heatramp;
            }
            else if ((t192 & 0x40) != 0)
            {
                //Middle 3rd
                r = 255;
                g = heatramp;
                b = 0;
            }
            else
            {
                //Coolest 3rd
                r = heatramp;
                g = 0;
                b = 0;
            }

            return Color.FromArgb(r, g, b);
        }

        private static int scale8_video(int i, int scale)
        {
            return (i == 0) ? 0 : ((i * scale) >> 8) + ((scale != 0) ? 1 : 0);
        }
    }
}
