#nullable enable
namespace MajdataPlay.Settings
{
    public class WasapiOptions
    {
        public bool Exclusive { get; set; } = true;

        public bool RawMode { get; set; } = true;

        public bool AsyncMode { get; set; } = true;

        public float BufferSize { get; set; } = 0.02f;

        public float Period { get; set; } = 0.005f;
    }
}
