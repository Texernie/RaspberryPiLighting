namespace RaspberryPiLighting
{
    class LightingConfiguration
    {
        public int NumberOfLEDs { get; init; }
        public bool DoFire { get; init; }
        public bool HorizontalFire { get; init; }

        public int PatternOffsetPerFrame { get; init; }
        public int TargetFps { get; init; }
        public string[] PatternToRepeat { get; init; }
        public LedRange[] LedDefinitions { get; set; }

        public int Cooling { get; init; }       // Rate at which the pixels cool off
        public int Sparks { get; init; }        // How many sparks will be attempted each frame
        public int SparkHeight { get; init; }   // If created, max height for a spark
        public int Sparking { get; init; }      // Probability of a spark each attempt
        public bool Reversed { get; init; }     // if reversed, we draw from 0 outwards
        public bool Mirrored { get; init; }     // if mirrored, we split and duplicate the drawing
    }

    class LedRange
    {
        public int Index { get; init; }
        public int LedStart { get; init; }
        public int LedEnd { get; init; }
        public int PatternStart { get; init; }
    }
}