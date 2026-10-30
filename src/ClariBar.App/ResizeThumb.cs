using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ClariBar.App;

internal sealed class ResizeThumb:UserControl
{
    private readonly Thumb thumb=new(){MinWidth=0,MinHeight=0};
    internal string Anchor {get;}
    internal InputSystemCursorShape CursorShape=>((InputSystemCursor)ProtectedCursor).CursorShape;
    public new Brush Background {get=>thumb.Background;set=>thumb.Background=value;}
    public ControlTemplate HandleTemplate {get=>thumb.Template;set=>thumb.Template=value;}
    public event DragStartedEventHandler DragStarted {add=>thumb.DragStarted+=value;remove=>thumb.DragStarted-=value;}
    public event DragDeltaEventHandler DragDelta {add=>thumb.DragDelta+=value;remove=>thumb.DragDelta-=value;}
    public event DragCompletedEventHandler DragCompleted {add=>thumb.DragCompleted+=value;remove=>thumb.DragCompleted-=value;}
    internal ResizeThumb(string anchor)
    {
        // Thumb is sealed; its cursor comes from this host during hover and pointer capture.
        Anchor=anchor;Content=thumb;HorizontalContentAlignment=HorizontalAlignment.Stretch;VerticalContentAlignment=VerticalAlignment.Stretch;
        ProtectedCursor=InputSystemCursor.Create(anchor switch
        {
            "NW" or "SE"=>InputSystemCursorShape.SizeNorthwestSoutheast,
            "NE" or "SW"=>InputSystemCursorShape.SizeNortheastSouthwest,
            "N" or "S"=>InputSystemCursorShape.SizeNorthSouth,
            "W" or "E"=>InputSystemCursorShape.SizeWestEast,
            _=>throw new ArgumentOutOfRangeException(nameof(anchor))
        });
    }
}
