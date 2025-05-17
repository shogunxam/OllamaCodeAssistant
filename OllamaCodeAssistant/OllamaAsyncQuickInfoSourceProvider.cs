using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;

namespace OllamaCodeAssistant {

  [Export(typeof(IAsyncQuickInfoSourceProvider))]
  [Name("OllamaAsyncQuickInfoSourceProvider")]
  [ContentType("CSharp")]
  [ContentType("code")]
  internal class OllamaAsyncQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider {

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) {
      return new OllamaAsyncQuickInfoSource(textBuffer);
    }
  }

  internal class OllamaAsyncQuickInfoSource : IAsyncQuickInfoSource, IDisposable {
    private readonly ITextBuffer _textBuffer;
    private bool _disposed;

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

      var wordSpan = GetWordSpan(snapshot, position);
      if (wordSpan == null)
        return null;

      string word = wordSpan.GetText(snapshot);

      // Simulate async LLM call with Task.FromResult or replace with actual async call:
      string llmSuggestion = await GetSuggestionFromLLMAsync(word, cancellationToken);

      var content = new ContainerElement(
          ContainerElementStyle.Stacked,
          new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, llmSuggestion))
      );

      return new QuickInfoItem(wordSpan, content);
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

    private Task<string> GetSuggestionFromLLMAsync(string word, CancellationToken cancellationToken) {
      // TODO: Replace this stub with actual async call to your LLM
      return Task.FromResult($"💡 LLM Suggestion for '{word}':\nConsider checking null or using more descriptive names.");
    }



    public void Dispose() {
      _disposed = true;
    }
  }
}