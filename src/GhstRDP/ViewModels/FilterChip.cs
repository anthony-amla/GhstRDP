using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GhstRDP.ViewModels;

public enum TypeFilter { Todos, Vps, Dedicado, Outro }
public enum StatusFilter { Todos, Online, Offline }

/// <summary>Chip de cliente nos filtros (cor igual à etiqueta do card).</summary>
public partial class ClientFilter : ObservableObject
{
    public ClientFilter(string name, int count, Brush background, Brush foreground, Brush dot)
    {
        Name = name;
        Count = count;
        TagBackground = background;
        TagForeground = foreground;
        Dot = dot;
    }

    public string Name { get; }
    public int Count { get; }
    public Brush TagBackground { get; }
    public Brush TagForeground { get; }
    public Brush Dot { get; }

    [ObservableProperty] private bool _isSelected;
}
