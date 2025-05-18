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
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isRequestActive;
    private string _lastChatClientUrl;
    private string _lastChatClientModelName;

    public event Action<string> OnResponseReceived;

    public event Action<string> OnErrorOccurred;

    public LLMInteractionManager() {
      _chatHistory = new List<ChatMessage>();
    }

    private void EnsureInitialized() {
      if (_isRequestActive) return;

      var options = GetExtensionOptions();

      if (_chatClient == null || options.OllamaApiUrl != _lastChatClientUrl || options.DefaultModel != _lastChatClientModelName) {
        // reinitialize the chat client
        _chatClient?.Dispose();
        _chatClient = new OllamaChatClient(new Uri(options.OllamaApiUrl), options.DefaultModel);

        _lastChatClientUrl = options.OllamaApiUrl;
        _lastChatClientModelName = options.DefaultModel;
      }
    }

    public async Task HandleUserMessageAsync(string userPrompt, bool includeSelection, bool includeFile, bool includeAllOpenFiles) {
      if (_isRequestActive) return;

      try {
        _isRequestActive = true;
        userPrompt = PromptManager.BuildPrompt(userPrompt, includeSelection, includeFile, includeAllOpenFiles);
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        OnResponseReceived?.Invoke($"\n\nYou: {userPrompt}");
        OnResponseReceived?.Invoke($"\n\nAssistant: ");

        EnsureInitialized();

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
          fullResponse.Append("\n\n...This response was cut short because the user canceled the request.\n\n");
          OnResponseReceived?.Invoke("\n\nQuery Canceled");
        } finally {
          await enumerator.DisposeAsync();
        }

        int codeBlockCount = Regex.Matches(fullResponse.ToString(), "```").Count;
        if (codeBlockCount % 2 != 0) {
          fullResponse.Append("\n```");
          OnResponseReceived?.Invoke("\n```");
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
      } finally {
        _cancellationTokenSource = null;
        _isRequestActive = false;
      }
    }

    public async Task<string> GetOneShotResponseAsync(string prompt) {
      if (_isRequestActive) return string.Empty;

      try {
        _isRequestActive = true;

        EnsureInitialized();

        _cancellationTokenSource = new CancellationTokenSource();
        var response = await _chatClient.GetResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        return response.Text;
      } catch (Exception ex) {
        OnErrorOccurred?.Invoke(ex.Message);
        return string.Empty;
      } finally {
        _cancellationTokenSource = null;
        _isRequestActive = false;
      }
    }

    public void CancelCurrentRequest() {
      _cancellationTokenSource?.Cancel();
    }

    private OllamaOptionsPage GetExtensionOptions() {
      var package = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(OllamaCodeAssistantPackage)) as OllamaCodeAssistantPackage;
      return package?.GetDialogPage(typeof(OllamaOptionsPage)) as OllamaOptionsPage ?? throw new ApplicationException("Unable to load settings");
    }
  }
}