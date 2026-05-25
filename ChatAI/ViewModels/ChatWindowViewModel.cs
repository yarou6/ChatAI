using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ChatAI.Models;
using ChatAI.Tools;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChatAI.ViewModels;

public class ChatWindowViewModel : BaseVM, IAsyncDisposable
{
    
    
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    private HubConnection? _hubConnection;
    private string? _token;
    private readonly SemaphoreSlim _aiGenerationLock = new(1, 1);

    public ObservableCollection<MessageModel> Messages { get; } = new();
    public ObservableCollection<ChatListItem> Chats { get; } = new();

    private string _serverUrl = "http://localhost:5115";
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetField(ref _serverUrl, value);
    }

    private string _login = "bot1";
    public string Login
    {
        get => _login;
        set => SetField(ref _login, value);
    }

    private string _password = "bot123";
    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    private string _chatIdText = "1";
    public string ChatIdText
    {
        get => _chatIdText;
        set => SetField(ref _chatIdText, value);
    }

    private string _newChatTitle = "AI Group Chat";
    public string NewChatTitle
    {
        get => _newChatTitle;
        set => SetField(ref _newChatTitle, value);
    }

    private string _currentMessage = string.Empty;
    public string CurrentMessage
    {
        get => _currentMessage;
        set => SetField(ref _currentMessage, value);
    }

    private string _lmStudioUrl = "http://127.0.0.1:1234/v1/chat/completions";
    public string LmStudioUrl
    {
        get => _lmStudioUrl;
        set => SetField(ref _lmStudioUrl, value);
    }

    private string _lmModel = "local-model";
    public string LmModel
    {
        get => _lmModel;
        set => SetField(ref _lmModel, value);
    }

    private string _systemPrompt = "You are AI participant in group chat. Reply in Russian, very short (1-2 sentences), no reasoning, no thinking process, no markdown.";
    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetField(ref _systemPrompt, value);
    }

    private string _status = "Disconnected";
    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    private string _newMembersLogins = string.Empty;
    public string NewMembersLogins
    {
        get => _newMembersLogins;
        set => SetField(ref _newMembersLogins, value);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task ConnectAsync()
    {
        try
        {
            Status = "Авторизация...";
            _token = await EnsureAuthorizedAsync();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{ServerUrl.TrimEnd('/')}/chatHub", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_token);
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                    options.WebSocketConfiguration = ws =>
                    {
                        ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<List<ServerMessageDto>>("GenerateResponse", async messages =>
            {
                await GenerateAndSendAiAnswer(messages);
            });
            _hubConnection.On<ServerMessageDto>("ReceiveMessage", message =>
            {
                Dispatcher.UIThread.Post(() => Messages.Add(ToUiMessage(message)));
            });

            await _hubConnection.StartAsync();
            Status = "Подключенно";

            await LoadChatsAsync();
            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            Status = $"Ошибка подключения: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task RegisterAsync()
    {
        try
        {
            var response = await AuthApiGetAsync("Register");
            Status = response.Message;
        }
        catch (Exception ex)
        {
            Status = $"Ошибка регистрации: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task CreateChatAsync()
    {
        if (_hubConnection is null) return;

        try
        {
            var response = await _hubConnection.InvokeAsync<ServerResponse>("CreateChat", NewChatTitle, null);
            if (TryParseData<CreateChatResult>(response.Data, out var chat) && chat is not null)
            {
                ChatIdText = chat.Id.ToString();
                Status = $"Чат создан: {chat.Id}";
                await LoadChatsAsync();
            }
            else
            {
                Status = response.Message;
            }
        }
        catch (Exception ex)
        {
            Status = $"Ошибка создание чата: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task LoadMessagesAsync()
    {
        if (_hubConnection is null || !TryGetChatId(out var chatId)) return;

        try
        {
            var messages = await FetchMessagesAsync(chatId);
            if (messages is null)
            {
                return;
            }

            var ordered = messages.OrderBy(m => m.Timestamp).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Messages.Clear();
                foreach (var item in ordered)
                {
                    Messages.Add(ToUiMessage(item));
                }
            });
        }
        catch (Exception ex)
        {
            Status = $"Ошибка загрузки: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task LoadChatsAsync()
    {
        if (_hubConnection is null) return;

        try
        {
            var response = await _hubConnection.InvokeAsync<ServerResponse>("GetChats");
            if (!TryParseData<List<ServerChatDto>>(response.Data, out var chats) || chats is null)
            {
                Status = response.Message;
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Chats.Clear();
                foreach (var chat in chats.OrderBy(c => c.Title))
                {
                    Chats.Add(new ChatListItem(chat.Id, chat.Title));
                }
            });
        }
        catch (Exception ex)
        {
            Status = $"Ошибка загрузки чата: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task SelectChatAsync(ulong chatId)
    {
        ChatIdText = chatId.ToString();
        await LoadMessagesAsync();
    }

    public void ClearMessagesView()
    {
        Messages.Clear();
        Status = "Чат очищен.";
    }

    public async Task SendMessageAsync(bool anonymous)
    {
        if (_hubConnection is null || !TryGetChatId(out var chatId)) return;

        var text = CurrentMessage?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var payload = anonymous ? $"[ANON] {text}" : text;
            var response = await _hubConnection.InvokeAsync<ServerResponse>("SendMessageAi", payload, chatId);
            if (response.StatusCodeInt >= 400)
            {
                Status = response.Message;
                return;
            }

            CurrentMessage = string.Empty;
            await LoadMessagesAsync();

            var messages = await FetchMessagesAsync(chatId);
            if (messages is not null && messages.Count > 0)
            {
                await GenerateAndSendAiAnswer(messages);
            }
        }
        catch (Exception ex)
        {
            Status = $"Ошибка сообщения: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task SendMessagePlainAsync(bool anonymous)
    {
        if (_hubConnection is null || !TryGetChatId(out var chatId)) return;

        var text = CurrentMessage?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var payload = anonymous ? $"[ANON] {text}" : text;
            var response = await _hubConnection.InvokeAsync<ServerResponse>("SendMessage", payload, chatId);
            if (response.StatusCodeInt >= 400)
            {
                Status = response.Message;
                return;
            }

            CurrentMessage = string.Empty;
            await LoadMessagesAsync();
            Status = "Сообщение отправлено";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка сообщения: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task AddMembersAsync()
    {
        if (_hubConnection is null || !TryGetChatId(out var chatId)) return;

        var members = (NewMembersLogins ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (members.Count == 0)
        {
            Status = "ыыыыыыы";
            return;
        }

        try
        {
            var response = await _hubConnection.InvokeAsync<ServerResponse>("AddChatMembers", chatId, members);
            Status = response.Message;
        }
        catch (Exception ex)
        {
            Status = $"Добавить участников не удалось: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task GenerateAndSendAiAnswer(IReadOnlyCollection<ServerMessageDto> messages)
    {
        if (_hubConnection is null || !TryGetChatId(out var chatId)) return;

        if (!await _aiGenerationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            Status = "Генерация AI response...";
            var promptMessages = new List<LmChatMessage>
            {
                new("system", SystemPrompt)
            };

            foreach (var item in messages.OrderBy(m => m.Timestamp))
            {
                var displaySender = item.Text.StartsWith("[ANON] ", StringComparison.Ordinal) ? "ANON" : item.SenderLogin;
                var modelText = NormalizeTextForModel(item.Text);
                promptMessages.Add(new LmChatMessage("user", $"{displaySender}: {modelText}"));
            }

            promptMessages.Add(new LmChatMessage("user", "Reply with one short chat message in Russian. No reasoning or service text."));

            var model = await ResolveLmModelAsync();
            var request = new LmCompletionRequest(model, promptMessages, 220, 0.7);
            var aiResponse = await _httpClient.PostAsJsonAsync(LmStudioUrl, request);
            if (!aiResponse.IsSuccessStatusCode)
            {
                var details = await aiResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"LM Studio HTTP {(int)aiResponse.StatusCode} ({aiResponse.StatusCode}). {details}");
            }

            var content = await aiResponse.Content.ReadFromJsonAsync<LmCompletionResponse>(JsonOptions);
            var answer = CleanAiAnswer(ExtractLmAnswer(content))?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                Status = "LM Studio вернула пустой ответ.";
                return;
            }

            await _hubConnection.InvokeAsync<ServerResponse>("SendMessageAi", $"[AI] {answer}", chatId);
            await LoadMessagesAsync();
            Status = "Ответ AI отправлен";
        }
        catch (Exception ex)
        {
            Status = $"AI генерации ошибки: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _aiGenerationLock.Release();
        }
    }

    private HubConnection BuildTempHub()
    {
        return new HubConnectionBuilder()
            .WithUrl($"{ServerUrl.TrimEnd('/')}/chatHub", options =>
            {
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                options.WebSocketConfiguration = ws =>
                {
                    ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                };
            })
            .Build();
    }

    private async Task<string> EnsureAuthorizedAsync()
    {
        var response = await AuthApiGetAsync("Authorize");

        if (response.StatusCodeInt >= 400)
        {
            throw new InvalidOperationException(response.Message);
        }

        if (!TryParseData<string>(response.Data, out var token) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Server did not return JWT token.");
        }

        return token;
    }

    private async Task<ServerResponse> AuthApiGetAsync(string action)
    {
        var baseUrl = ServerUrl.TrimEnd('/');
        var login = Uri.EscapeDataString(Login);
        var password = Uri.EscapeDataString(Password);
        var url = $"{baseUrl}/api/Auth/{action}?login={login}&password={password}";

        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("Empty auth response.");
        }

        return payload;
    }

    private bool TryGetChatId(out ulong chatId)
    {
        if (ulong.TryParse(ChatIdText, out chatId)) return true;

        Status = "Идентификатор чата должен быть числом.";
        return false;
    }

    private async Task<List<ServerMessageDto>?> FetchMessagesAsync(ulong chatId)
    {
        if (_hubConnection is null) return null;

        var response = await _hubConnection.InvokeAsync<ServerResponse>("GetMessages", chatId);
        if (!TryParseData<List<ServerMessageDto>>(response.Data, out var messages) || messages is null)
        {
            Status = response.Message;
            return null;
        }

        return messages;
    }

    private async Task<string> ResolveLmModelAsync()
    {
        var current = LmModel?.Trim();
        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, "local-model", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var baseUri = GetLmBaseUri();
        if (baseUri is null)
        {
            throw new InvalidOperationException("LM Studio URL is invalid.");
        }

        var modelsUri = new Uri(baseUri, "models");
        var response = await _httpClient.GetAsync(modelsUri);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("LM Studio endpoint /v1/models not found.");
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LmModelsResponse>(JsonOptions);
        var first = payload?.Data?.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(first))
        {
            throw new InvalidOperationException("No loaded model in LM Studio. Load a model first.");
        }

        LmModel = first;
        Status = $"Использование модели LM: {LmModel}";
        return LmModel;
    }

    private Uri? GetLmBaseUri()
    {
        if (!Uri.TryCreate(LmStudioUrl, UriKind.Absolute, out var full))
        {
            return null;
        }

        var path = full.AbsolutePath;
        var suffix = "/chat/completions";
        var index = path.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var basePath = path.Substring(0, index + 1);
        return new UriBuilder(full.Scheme, full.Host, full.Port, basePath).Uri;
    }

    private MessageModel ToUiMessage(ServerMessageDto dto)
    {
        var isAi = dto.Text.StartsWith("[AI] ", StringComparison.Ordinal);
        var isAnon = dto.Text.StartsWith("[ANON] ", StringComparison.Ordinal);
        var cleanText = dto.Text;
        if (isAi) cleanText = cleanText[5..];
        if (isAnon) cleanText = cleanText[7..];

        return new MessageModel
        {
            Text = cleanText,
            Sender = dto.SenderLogin,
            DisplaySender = isAnon ? "ANON" : isAi ? "AI" : dto.SenderLogin,
            Timestamp = dto.Timestamp.LocalDateTime,
            IsMe = string.Equals(dto.SenderLogin, Login, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? CleanAiAnswer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        var markers = new[]
        {
            "Thinking Process:",
            "Reasoning:",
            "<think>",
            "</think>"
        };

        foreach (var marker in markers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                text = text[..idx].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return "Я на связи. Отвечу коротко и по делу.";
        }

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("1.") && !l.StartsWith("2.") && !l.StartsWith("3."))
            .ToArray();

        var compact = string.Join(" ", lines).Trim();
        return string.IsNullOrWhiteSpace(compact) ? text : compact;
    }

    private static bool TryParseData<T>(string? raw, out T? result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            result = JsonSerializer.Deserialize<T>(raw, JsonOptions);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }

        _aiGenerationLock.Dispose();
        _httpClient.Dispose();
    }

    private static string? ExtractLmAnswer(LmCompletionResponse? response)
    {
        var choice = response?.Choices?.FirstOrDefault();
        if (choice is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(choice.Text))
        {
            return choice.Text;
        }

        if (choice.Message.ValueKind == JsonValueKind.Object)
        {
            if (TryGetTextFromMessage(choice.Message, out var fromMessage))
            {
                return fromMessage;
            }
        }

        if (choice.Delta.ValueKind == JsonValueKind.Object)
        {
            if (TryGetTextFromMessage(choice.Delta, out var fromDelta))
            {
                return fromDelta;
            }
        }

        return null;
    }

    private static bool TryGetTextFromMessage(JsonElement messageElement, out string? text)
    {
        text = null;

        if (messageElement.TryGetProperty("content", out var contentElement))
        {
            text = ReadContent(contentElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
        }

        if (messageElement.TryGetProperty("reasoning_content", out var reasoningElement))
        {
            text = ReadContent(reasoningElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
        }

        if (messageElement.TryGetProperty("text", out var textElement))
        {
            text = ReadContent(textElement);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadContent(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => string.Join(" ", element.EnumerateArray()
                .Select(item => item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("text", out var textProp) => textProp.GetString(),
                    _ => null
                })
                .Where(s => !string.IsNullOrWhiteSpace(s))!),
            JsonValueKind.Object when element.TryGetProperty("text", out var textProp) => textProp.GetString(),
            _ => null
        };
    }

    private static string NormalizeTextForModel(string text)
    {
        var result = text;
        if (result.StartsWith("[ANON] ", StringComparison.Ordinal)) result = result[7..];
        if (result.StartsWith("[AI] ", StringComparison.Ordinal)) result = result[5..];
        return result.Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed record ServerResponse(string? Data, string Message, JsonElement StatusCode)
    {
        public int StatusCodeInt => StatusCode.ValueKind switch
        {
            JsonValueKind.Number => StatusCode.GetInt32(),
            JsonValueKind.String when int.TryParse(StatusCode.GetString(), out var num) => num,
            _ => 200
        };

        public static implicit operator int(ServerResponse response) => response.StatusCodeInt;
    }

    public sealed record ServerMessageDto(Guid Id, string Text, string SenderLogin, ulong ChatId, DateTimeOffset Timestamp);
    public sealed record CreateChatResult(ulong Id, string Title);
    public sealed record ServerChatDto(ulong Id, string Title);
    public sealed record ChatListItem(ulong Id, string Title);

    public sealed record LmChatMessage(string Role, string Content);
    public sealed record LmCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<LmChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature);

    public sealed record LmCompletionResponse([property: JsonPropertyName("choices")] List<LmChoice> Choices);
    public sealed record LmChoice(
        [property: JsonPropertyName("message")] JsonElement Message,
        [property: JsonPropertyName("delta")] JsonElement Delta,
        [property: JsonPropertyName("text")] string? Text);
    public sealed record LmModelsResponse([property: JsonPropertyName("data")] List<LmModelItem> Data);
    public sealed record LmModelItem([property: JsonPropertyName("id")] string Id);
}
