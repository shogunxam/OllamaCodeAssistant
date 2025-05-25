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
        [Order(Before = "Completion")] // Before IntelliCode's completion layer
        [Order(Before = "Suggestion")] // Before IntelliCode's suggestion layer
        public AdornmentLayerDefinition InlineSuggestionLayerDefinition = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            // This factory is responsible only for registering the adornment layer
            // The actual adornment is created by the TextViewEventListener
            System.Diagnostics.Debug.WriteLine("InlineSuggestionAdornmentFactory: TextViewCreated");
        }
    }
} }
}
