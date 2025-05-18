using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OllamaCodeAssistant.Options;

namespace OllamaCodeAssistant {

  public class LLMInteractionManager {
    private IChatClient _chatClient;
    private readonly List<ChatMessage> _chatHistory;
    private readonly OllamaOptionsPage _options;
    private CancellationTokenSource _cancellationTokenSource;
    private string _lastChatClientUrl;
    private string _lastChatClientModelName;

    public event Action<string> OnResponseReceived;

    public event Action<string> OnErrorOccurred;

    public bool IsRequestActive { get; private set; }

    public LLMInteractionManager(OllamaOptionsPage options) {
      _options = options ?? throw new ArgumentNullException(nameof(options));
      _chatHistory = new List<ChatMessage>();
    }

    private void EnsureInitialized() {
      if (IsRequestActive) return;

      if (_chatClient == null || _options.OllamaApiUrl != _lastChatClientUrl || _options.DefaultModel != _lastChatClientModelName) {
        // reinitialize the chat client
        _chatClient?.Dispose();
        _chatClient = new OllamaChatClient(new Uri(_options.OllamaApiUrl), _options.DefaultModel);

        _lastChatClientUrl = _options.OllamaApiUrl;
        _lastChatClientModelName = _options.DefaultModel;
      }
    }

    public async Task HandleUserMessageAsync(string userPrompt, bool includeSelection, bool includeFile, bool includeAllOpenFiles) {
      EnsureInitialized();

      if (IsRequestActive) return;

      try {
        IsRequestActive = true;

        OnResponseReceived?.Invoke($"\n\nYou: {userPrompt}");

        userPrompt = PromptManager.BuildPrompt(userPrompt, includeSelection, includeFile, includeAllOpenFiles);
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        OnResponseReceived?.Invoke($"\n\nAssistant: ");

        var fullResponse = new StringBuilder();
        _cancellationTokenSource = new CancellationTokenSource();

        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationTokenSource.Token);

        try {
          while (await enumerator.MoveNextAsync()) {
            if (_cancellationTokenSource.Token.IsCancellationRequested) break;

            var response = enumerator.Current ?? throw new ApplicationException("Chat response was null");
            fullResponse.Append(response.Text);
            OnResponseReceived?.Invoke(response.Text);
          }
        } catch (OperationCanceledException) {
          // Handle gracefully
        } finally {
          await enumerator.DisposeAsync();
        }

        int codeBlockCount = Regex.Matches(fullResponse.ToString(), "```").Count;
        if (codeBlockCount % 2 != 0) {
          fullResponse.Append("\n```");
          OnResponseReceived?.Invoke("\n```");
        }

        if (_cancellationTokenSource.IsCancellationRequested) {
          fullResponse.Append("\n\n...This response was cut short because the user canceled the request.\n\n");
          OnResponseReceived?.Invoke("\n\nQuery Canceled");
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
      } finally {
        _cancellationTokenSource = null;
        IsRequestActive = false;
      }
    }

    public async Task<string> GetOneShotResponseAsync(string prompt) {
      EnsureInitialized();

      if (IsRequestActive) {
        return string.Empty;
      }

      try {
        IsRequestActive = true;

        _cancellationTokenSource = new CancellationTokenSource();
        var response = await _chatClient.GetResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        return response.Text;
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
        return string.Empty;
      } finally {
        _cancellationTokenSource = null;
        IsRequestActive = false;
      }
    }

    public void CancelCurrentRequest() {
      _cancellationTokenSource?.Cancel();
    }
  }
}