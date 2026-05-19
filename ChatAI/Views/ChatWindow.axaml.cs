using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ChatAI.ViewModels;

namespace ChatAI.Views;

public partial class ChatWindow : Window
{
    private readonly ChatWindowViewModel _viewModel;
    public ChatWindow()
    {
        InitializeComponent();
        _viewModel = new ChatWindowViewModel();
        DataContext = _viewModel;
    }
}