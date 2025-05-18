using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

namespace OllamaCodeAssistant {

  internal class OllamaAsyncQuickInfoSource : IAsyncQuickInfoSource, IDisposable {
    public static ChatToolWindowControl ChatToolWindowControl { get; set; }

    private readonly ITextBuffer _textBuffer;
    private bool _disposed;

    // Track suggestions per session to avoid duplicate calls
    private readonly Dictionary<string, string> _suggestions = new Dictionary<string, string>();

    public OllamaAsyncQuickInfoSource(ITextBuffer textBuffer) {
      _textBuffer = textBuffer;
    }

    public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken) {
      if (_disposed)
        return null;

      var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
      if (triggerPoint == null)
        return null;

      var snapshot = triggerPoint.Value.Snapshot;
      int position = triggerPoint.Value.Position;

      var line = snapshot.GetLineFromPosition(position);
      var lineText = line.GetText();

      int start = position - line.Start.Position;
      int wordStart = start;
      int wordEnd = start;

      while (wordStart > 0 && char.IsLetterOrDigit(lineText[wordStart - 1]))
        wordStart--;

      while (wordEnd < lineText.Length && char.IsLetterOrDigit(lineText[wordEnd]))
        wordEnd++;

      if (wordStart == wordEnd)
        return null;

      var wordSpan = snapshot.CreateTrackingSpan(line.Start + wordStart, wordEnd - wordStart, SpanTrackingMode.EdgeInclusive);

      if (wordSpan == null)
        return null;

      // Get the word text
      string word = wordSpan.GetText(session.TextView.TextSnapshot);

      if (_suggestions.TryGetValue(word, out string suggestion)) {
        var content = new ContainerElement(
          ContainerElementStyle.Stacked,
          new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, suggestion)
          )
        );

        return new QuickInfoItem(wordSpan, content);
      }

      var prompt = $"Please explain the usage of \"{word}\" within the following line of code: ```{lineText}```";

      // Show clickable link using navigationAction
      var runs = new List<ClassifiedTextRun> {
      new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, "💡 "),
        new ClassifiedTextRun(
          PredefinedClassificationTypeNames.Keyword,
          "Ask Ollama for explanation",
          navigationAction: () => {
            _ = FetchSuggestionAndRefreshAsync(session, prompt);
          }
        )
      };

      var contentWithLink = new ContainerElement(
        ContainerElementStyle.Stacked,
        new ClassifiedTextElement(runs)
      );

      return new QuickInfoItem(wordSpan, contentWithLink);
    }

    private ITrackingSpan GetWordSpan(ITextSnapshot snapshot, int position) {
      var line = snapshot.GetLineFromPosition(position);
      var lineText = line.GetText();

      int start = position - line.Start.Position;
      int wordStart = start;
      int wordEnd = start;

      while (wordStart > 0 && char.IsLetterOrDigit(lineText[wordStart - 1]))
        wordStart--;

      while (wordEnd < lineText.Length && char.IsLetterOrDigit(lineText[wordEnd]))
        wordEnd++;

      if (wordStart == wordEnd)
        return null;

      return snapshot.CreateTrackingSpan(line.Start + wordStart, wordEnd - wordStart, SpanTrackingMode.EdgeInclusive);
    }

    private async Task FetchSuggestionAndRefreshAsync(IAsyncQuickInfoSession session, string prompt) {
      if (_disposed)
        return;

      // Fire this off to the LLM asynchronously
      ChatToolWindowControl.AskLLM(prompt);

      // Close the quick info
      await session.DismissAsync();
    }

    public void Dispose() {
      _disposed = true;
      _suggestions.Clear();
    }
  }
}