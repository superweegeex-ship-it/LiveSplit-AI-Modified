using LiveSplit.Model;
using System;

[assembly: LiveSplit.UI.Components.ComponentFactory(typeof(LiveSplit.UI.Components.SpotifyComponentFactory))]

namespace LiveSplit.UI.Components
{
    public sealed class SpotifyComponentFactory : IComponentFactory
    {
        public string ComponentName => "Spotify Now Playing";
        public string Description => "Displays the currently playing Spotify track, artist, artwork, and progress.";
        public ComponentCategory Category => ComponentCategory.Information;
        public IComponent Create(LiveSplitState state) => new SpotifyComponent(state);
        public string UpdateName => ComponentName;
        public string XMLURL => "";
        public string UpdateURL => "";
        public Version Version => new Version(1, 2, 2);
    }
}
