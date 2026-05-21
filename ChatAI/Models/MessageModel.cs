using System;

namespace ChatAI.Models;

public class MessageModel
{
    public string Text { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string DisplaySender { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsMe { get; set; }
}
