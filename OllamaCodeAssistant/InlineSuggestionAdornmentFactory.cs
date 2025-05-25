using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace OllamaCodeAssistant
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class InlineSuggestionAdornmentFactory : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("InlineSuggestion")]
        [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Caret)]
        [Order(Before = "Completion")] // Prima del layer di completamento di IntelliCode
        [Order(Before = "Suggestion")] // Prima del layer di suggerimenti di IntelliCode
        public AdornmentLayerDefinition InlineSuggestionLayerDefinition = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            // Questo factory si occupa solo di registrare il layer di adornment
            // L'adornment effettivo viene creato dal TextViewEventListener
            System.Diagnostics.Debug.WriteLine("InlineSuggestionAdornmentFactory: TextViewCreated");
        }
    }
}
