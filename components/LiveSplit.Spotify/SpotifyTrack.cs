using System;
using System.Drawing;

namespace LiveSplit.UI.Components
{
    internal sealed class SpotifyTrack : IDisposable
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int DurationMs { get; set; }
        public int ProgressMs { get; set; }
        public bool IsPlaying { get; set; }
        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
        public Image Artwork { get; set; }
        public string ArtworkUrl { get; set; } = "";

        public int CurrentProgressMs
        {
            get
            {
                if (!IsPlaying) return ProgressMs;
                var elapsed = (int)Math.Max(0, (DateTime.UtcNow - ReceivedAtUtc).TotalMilliseconds);
                return Math.Min(DurationMs, ProgressMs + elapsed);
            }
        }

        public void Dispose() => Artwork?.Dispose();
    }
}
