using System;

namespace ChatAI.Models;

public class MessageModel
{
    public string Text { get; set; }
    public string Sender { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsMe { get; set; } 
}