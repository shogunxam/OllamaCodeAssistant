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
    private CancellationTokenSource _activeRequestCancellationTokenSource;
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
      if (IsRequestActive) {
        return;
      }

      EnsureInitialized();

      try {
        IsRequestActive = true;

        OnResponseReceived?.Invoke($"\n\nYou: {userPrompt}");

        userPrompt = PromptManager.BuildPrompt(userPrompt, includeSelection, includeFile, includeAllOpenFiles);
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        OnResponseReceived?.Invoke($"\n\nAssistant: ");

        var fullResponse = new StringBuilder();
        _activeRequestCancellationTokenSource?.Dispose();
        _activeRequestCancellationTokenSource = new CancellationTokenSource();

        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, cancellationToken: _activeRequestCancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_activeRequestCancellationTokenSource.Token);

        try {
          while (await enumerator.MoveNextAsync()) {
            if (_activeRequestCancellationTokenSource.Token.IsCancellationRequested) break;

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

        if (_activeRequestCancellationTokenSource.IsCancellationRequested) {
          fullResponse.Append("\n\n...This response was cut short because the user canceled the request.\n\n");
          OnResponseReceived?.Invoke("\n\nQuery Canceled");
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
      } finally {
        _activeRequestCancellationTokenSource?.Dispose();
        _activeRequestCancellationTokenSource = null;
        IsRequestActive = false;
      }
    }

    public async Task<string> GetOneShotResponseAsync(string prompt) {
      if (IsRequestActive) {
        return string.Empty;
      }

      EnsureInitialized();

      try {
        IsRequestActive = true;
        _activeRequestCancellationTokenSource?.Dispose();
        _activeRequestCancellationTokenSource = new CancellationTokenSource();
        _chatHistory.Add(new ChatMessage(ChatRole.User, prompt));
        var response = await _chatClient.GetResponseAsync(_chatHistory, cancellationToken: _activeRequestCancellationTokenSource.Token);
        return response.Text;
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
        return string.Empty;
      } finally {
        IsRequestActive = false;
        _activeRequestCancellationTokenSource?.Dispose();
        _activeRequestCancellationTokenSource = null;
      }
    }

    public void CancelCurrentRequest() {
      _activeRequestCancellationTokenSource?.Cancel();
    }
  }
}