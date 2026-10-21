using System;
using LiveSplit.Model;

[assembly: LiveSplit.UI.Components.ComponentFactory(typeof(LiveSplit.UI.Components.N64InputComponentFactory))]

namespace LiveSplit.UI.Components
{
    public sealed class N64InputComponentFactory : IComponentFactory
    {
        public string ComponentName => "N64 Input Viewer";
        public string Description => "Shows live Nintendo 64 controller input from a console adapter connected over serial.";
        public ComponentCategory Category => ComponentCategory.Information;
        public IComponent Create(LiveSplitState state) => new N64InputComponent();
        public string UpdateName => ComponentName;
        public string XMLURL => "";
        public string UpdateURL => "";
        public Version Version => new Version(0, 1, 0);
    }
}
