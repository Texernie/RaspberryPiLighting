namespace RaspberryPiLighting
{
    class LightingConfiguration
    {
        public int NumberOfLEDs { get; set; }
        public bool DoFire { get; set; }
        public bool HorizontalFire { get; set; }

        public int PatternOffsetPerFrame { get; set; }
        public int TargetFps { get; set; }
        public string[] PatternToRepeat { get; set; }
        public Dictionary<int, int[]> PatternToPixelMap { get; set; }

        public int Cooling { get; set; }       // Rate at which the pixels cool off
        public int Sparks { get; set; }        // How many sparks will be attempted each frame
        public int SparkHeight { get; set; }   // If created, max height for a spark
        public int Sparking { get; set; }      // Probability of a spark each attempt
        public bool Reversed { get; set; }     // if reversed, we draw from 0 outwards
        public bool Mirrored { get; set; }     // if mirrored, we split and duplicate the drawing
    }
}