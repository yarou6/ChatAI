using System;
using System.Collections.ObjectModel;
using ChatAI.Models;
using ChatAI.Tools;

namespace ChatAI.ViewModels;

public class ChatWindowViewModel: BaseVM
{
    public ObservableCollection<MessageModel> Messages { get; set; } = new();

    private string _currentMessage;
    public string CurrentMessage
    {
        get => _currentMessage;
        set { _currentMessage = value; OnPropertyChanged(); }
    }

    public ChatWindowViewModel()
    {
        SendMessage();
    }
    public void SendMessage()
    {
        if (!string.IsNullOrWhiteSpace(CurrentMessage))
        {
            Messages.Add(new MessageModel 
            { 
                Text = CurrentMessage, 
                Sender = "Вы", 
                Timestamp = DateTime.Now, 
                IsMe = true 
            });
            CurrentMessage = string.Empty; 
        }
    }
   
}