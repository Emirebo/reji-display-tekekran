namespace RejiDisplay.Models
{
    public enum ScaleMode
    {
        Fit,     // Preserves aspect ratio, letterbox if needed (WPF Uniform)
        Fill,    // Preserves aspect ratio, crops if needed (WPF UniformToFill)
        Stretch  // Fills entire display without preserving aspect ratio (WPF Fill)
    }
}
