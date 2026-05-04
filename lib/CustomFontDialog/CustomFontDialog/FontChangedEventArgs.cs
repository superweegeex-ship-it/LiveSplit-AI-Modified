using System.Drawing;
using System;

namespace CustomFontDialog;

public class FontChangedEventArgs : EventArgs
{
    public Font NewFont { get; set; }
}
