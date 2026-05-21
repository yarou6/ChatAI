using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ChatAI.ViewModels;

namespace ChatAI.Views;

public partial class ChatWindow : Window
{
    public ChatWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm)
        {
            await vm.DisposeAsync();
        }
    }

    private async void RegisterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.RegisterAsync();
    }

    private async void ConnectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.ConnectAsync();
    }

    private async void CreateChatClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.CreateChatAsync();
    }

    private async void RefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.LoadMessagesAsync();
    }
    
    private async void LoadChatsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.LoadChatsAsync();
    }

    private async void ChatsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ChatWindowViewModel vm) return;
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not ChatWindowViewModel.ChatListItem chat) return;
        
        await vm.SelectChatAsync(chat.Id);
    }

    private void ClearChatViewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) vm.ClearMessagesView();
    }

    private async void AddMembersClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.AddMembersAsync();
    }

    private async void SendPlainClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.SendMessagePlainAsync(anonymous: false);
    }

    private async void SendPlainAnonymousClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.SendMessagePlainAsync(anonymous: true);
    }

    private async void SendClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.SendMessageAsync(anonymous: false);
    }

    private async void SendAnonymousClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm) await vm.SendMessageAsync(anonymous: true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
