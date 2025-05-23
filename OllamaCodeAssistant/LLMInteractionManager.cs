using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private CancellationTokenSource _activeRequestCancellationTokenSource;
    private string _lastChatClientUrl;
    private string _lastChatClientModelName;

    public event Action<string> OnResponseReceived;

    public event Action<string> OnErrorOccurred;

    public event Action<string> OnLogEntryReceived;

    public bool IsRequestActive { get; private set; }

    public OllamaOptionsPage Options { get; }

    public LLMInteractionManager(OllamaOptionsPage options) {
      Options = options ?? throw new ArgumentNullException(nameof(options));
      _chatHistory = new List<ChatMessage>();
    }

    private void EnsureInitialized() {
      if (IsRequestActive) return;

      if (_chatClient == null || Options.OllamaApiUrl != _lastChatClientUrl || Options.DefaultModel != _lastChatClientModelName) {
        // reinitialize the chat client
        _chatClient?.Dispose();
        _chatClient = new OllamaChatClient(new Uri(Options.OllamaApiUrl), Options.DefaultModel);

        _lastChatClientUrl = Options.OllamaApiUrl;
        _lastChatClientModelName = Options.DefaultModel;
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

        var options = new ChatOptions() { AdditionalProperties = new AdditionalPropertiesDictionary() };
        //options.AdditionalProperties.Add("num_ctx", 32768);
        //num_ctx = int(self.token_count(messages) * 1.25) + 8192
        //kwargs["num_ctx"] = num_ctx
        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, options, cancellationToken: _activeRequestCancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_activeRequestCancellationTokenSource.Token);

        try {
          while (await enumerator.MoveNextAsync()) {
            if (_activeRequestCancellationTokenSource.Token.IsCancellationRequested) break;

            var response = enumerator.Current ?? throw new ApplicationException("Chat response was null");

            foreach (var content in response.Contents) {
              if (content is TextContent textContent) {
                fullResponse.Append(textContent.Text);
                OnResponseReceived?.Invoke(textContent.Text);
              } else if (content is UsageContent usage) {
                var logEntry = new StringBuilder();
                logEntry.AppendLine($"Input tokens: {usage?.Details.InputTokenCount}");
                logEntry.AppendLine($"Output tokens: {usage?.Details.OutputTokenCount}");
                logEntry.AppendLine($"Total tokens: {usage?.Details.TotalTokenCount}");
                logEntry.AppendLine($"Load duration: {usage?.Details.AdditionalCounts["load_duration"]}");
                logEntry.AppendLine($"Total duration: {usage?.Details.AdditionalCounts["total_duration"]}");
                logEntry.AppendLine($"Prompt eval duration: {usage?.Details.AdditionalCounts["prompt_eval_duration"]}");
                logEntry.AppendLine($"Eval duration: {usage?.Details.AdditionalCounts["eval_duration"]}");
                OnLogEntryReceived?.Invoke(logEntry.ToString());
              } else {
                Debug.WriteLine($"Unknown content type: {content.GetType()}");
              }
            }

            //fullResponse.Append(response.Text);
            //OnResponseReceived?.Invoke(response.Text);
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