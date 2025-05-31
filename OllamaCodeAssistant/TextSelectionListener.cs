using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace OllamaCodeAssistant
{

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")] // or "code" for just code files
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public class TextSelectionListener : IWpfTextViewCreationListener
    {

        public static event EventHandler<string> SelectionChanged;

        public void TextViewCreated(IWpfTextView textView)
        {
            textView.Selection.SelectionChanged += (s, e) =>
            {
                var selectedText = textView.Selection.SelectedSpans.Count > 0
                    ? textView.Selection.SelectedSpans[0].GetText()
                    : string.Empty;

                SelectionChanged?.Invoke(this, selectedText);
            };
        }
    }
}