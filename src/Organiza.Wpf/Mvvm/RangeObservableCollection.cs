using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Organiza.Wpf.Mvvm;

/// <summary>
/// Atualiza listas extensas com uma única notificação visual. Evita milhares de
/// repinturas do WPF durante a carga de pastas grandes.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
