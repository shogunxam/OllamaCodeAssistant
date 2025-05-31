using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
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
    private long _cumulativeTokenCount;
    private CancellationTokenSource _activeRequestCancellationTokenSource;
    private string _lastChatClientUrl;
    private string _lastChatClientModelName;

    public event Action<string> OnResponseReceived;

    public event Action<string> OnErrorOccurred;

    public event Action<string> OnLogEntryReceived;

    public bool IsRequestActive { get; private set; }

    public OllamaOptionsPage Options { get; }

    public int MinimumContextWindowTokens = 2049;
    public int MaximumContextWindowTokens = 32768;

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
    private string FormatUserMessage(string message)
    {
        string escapedMessage = WebUtility.HtmlEncode(message);
        return $"<div class=\"user-prompt\"><b>You:</b> {escapedMessage}</div>";
    }

    public async Task HandleUserMessageAsync(string userPrompt, string fullPrompt) {
      if (IsRequestActive) {
        return;
      }

      EnsureInitialized();

      try {
        IsRequestActive = true;

        Debug.WriteLine($"User Prompt: {userPrompt}");
        OnResponseReceived?.Invoke(FormatUserMessage(userPrompt));

        Debug.WriteLine($"Full Prompt: {fullPrompt}");
        _chatHistory.Add(new ChatMessage(ChatRole.User, fullPrompt));

        OnResponseReceived?.Invoke("<p><b>Assistant: </b>");

        var fullResponse = new StringBuilder();
        _activeRequestCancellationTokenSource?.Dispose();
        _activeRequestCancellationTokenSource = new CancellationTokenSource();

        var options = new ChatOptions() { AdditionalProperties = new AdditionalPropertiesDictionary() };

        // Adjust context window size the same way Aider does
        //num_ctx = int(self.token_count(messages) * 1.25) + 8192
        var contextWindowSize = Math.Max(MinimumContextWindowTokens, Math.Min(MaximumContextWindowTokens, (int)(_cumulativeTokenCount * 1.25) + 8192));
        options.AdditionalProperties.Add("num_ctx", contextWindowSize);
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
                if (usage.Details.TotalTokenCount.HasValue) {
                  _cumulativeTokenCount += usage.Details.TotalTokenCount.Value;
                }

                var logEntry = new StringBuilder();
                logEntry.AppendLine($"Model: {Options.DefaultModel}");
                logEntry.AppendLine($"Context Window Size: {contextWindowSize:N0}");
                logEntry.AppendLine($"Input tokens: {usage.Details.InputTokenCount}");
                logEntry.AppendLine($"Output tokens: {usage.Details.OutputTokenCount}");
                logEntry.AppendLine($"Total tokens: {usage.Details.TotalTokenCount}");
                logEntry.AppendLine($"Load duration: {usage.Details.AdditionalCounts["load_duration"]}");
                logEntry.AppendLine($"Total duration: {usage.Details.AdditionalCounts["total_duration"]}");
                logEntry.AppendLine($"Prompt eval duration: {usage.Details.AdditionalCounts["prompt_eval_duration"]}");
                logEntry.AppendLine($"Eval duration: {usage.Details.AdditionalCounts["eval_duration"]}");
                OnLogEntryReceived?.Invoke(logEntry.ToString());
              } else {
                OnLogEntryReceived?.Invoke($"Unknown content type: {content.GetType()}");
              }
            }
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

        public async Task<string> GetCodeCompletionAsync(string codeBefore, string codeAfter, string language = null)
        {
            if (IsRequestActive)
            {
                return string.Empty;
            }

            EnsureInitialized();

            try
            {
                IsRequestActive = true;
                _activeRequestCancellationTokenSource?.Dispose();
                _activeRequestCancellationTokenSource = new CancellationTokenSource();

                // Prompt ottimizzato per il completamento del codice

                string prompt = $@"You are an expert {language} code autocompleter. Given the full context of the code — both before and after the cursor — generate only the minimal and logically correct completion at the current cursor position. Do not repeat existing keywords or syntax already present.

Do not add explanations, comments, or markdown formatting. Output only the new code needed to complete the current statement or structure.

Existing code before cursor:
{codeBefore}

Existing code after cursor:
{codeAfter}

Completion:";

                var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, prompt) };
                var response = await _chatClient.GetResponseAsync(messages, cancellationToken: _activeRequestCancellationTokenSource.Token);
                return response.Text?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                OnErrorOccurred?.Invoke(ex.Message);
                return string.Empty;
            }
            finally
            {
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