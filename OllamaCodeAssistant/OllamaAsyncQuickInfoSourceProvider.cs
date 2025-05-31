using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace OllamaCodeAssistant
{

    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("OllamaAsyncQuickInfoSourceProvider")]
    [ContentType("code")]
    internal class OllamaAsyncQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new OllamaAsyncQuickInfoSource(textBuffer);
        }
    }
}