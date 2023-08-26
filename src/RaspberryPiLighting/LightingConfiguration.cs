namespace RaspberryPiLighting
{
    class LightingConfiguration
    {
        public int PatternOffsetPerFrame { get; set; }
        public int NumberOfLEDs { get; set; }
        public int FrameDelayInMs { get; set; }
        public string[] PatternToRepeat { get; set; }
        public Dictionary<int, int[]> PatternToPixelMap { get; set; }
    }
}