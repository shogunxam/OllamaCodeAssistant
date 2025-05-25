using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using System;

namespace OllamaCodeAssistant
{
    internal class OllamaCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly IOleCommandTarget _nextCommandTarget;
        private InlineSuggestionAdornment _suggestionAdornment;

        public OllamaCommandFilter(IWpfTextView textView, IOleCommandTarget nextCommandTarget)
        {
            _textView = textView;
            _nextCommandTarget = nextCommandTarget;
            System.Diagnostics.Debug.WriteLine("OllamaCommandFilter created");
        }

        public void SetSuggestionAdornment(InlineSuggestionAdornment adornment)
        {
            _suggestionAdornment = adornment;
            System.Diagnostics.Debug.WriteLine("SuggestionAdornment set in CommandFilter");
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return _nextCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            System.Diagnostics.Debug.WriteLine($"OllamaCommandFilter.Exec: Group={pguidCmdGroup}, ID={nCmdID}");

            if (_suggestionAdornment != null && _suggestionAdornment.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion visible, checking command: {nCmdID}");

                // Check that the cursor is still at the correct position
                var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                if (!_suggestionAdornment.IsCaretAtSuggestionPosition(currentCaretPosition))
                {
                    System.Diagnostics.Debug.WriteLine("Cursor not at suggestion position, hiding suggestion");
                    _suggestionAdornment.HideSuggestion();
                    return _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }

                // Check if it's a command we are interested in
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch (nCmdID)
                    {
                        case (uint)VSConstants.VSStd2KCmdID.TAB:
                            System.Diagnostics.Debug.WriteLine("Tab intercepted by CommandFilter");
                            if (_suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggestion accepted with Tab - BLOCKING PROPAGATION");
                                return VSConstants.S_OK; // Block propagation
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.RETURN:
                            _suggestionAdornment.HideSuggestion();
                            break;
                        /*
                        System.Diagnostics.Debug.WriteLine("Enter intercepted by CommandFilter");
                        if (_suggestionAdornment.AcceptSuggestion())
                        {
                            System.Diagnostics.Debug.WriteLine("Suggestion accepted with Enter - BLOCKING PROPAGATION");
                            return VSConstants.S_OK; // Block propagation
                        }
                        break;
                        */
                        case (uint)VSConstants.VSStd2KCmdID.RIGHT:
                            System.Diagnostics.Debug.WriteLine("Right arrow intercepted by CommandFilter");
                            if (IsAtEndOfLine() && _suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggestion accepted with Right arrow - BLOCKING PROPAGATION");
                                return VSConstants.S_OK; // Block propagation
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.CANCEL:
                            System.Diagnostics.Debug.WriteLine("Escape intercepted by CommandFilter");
                            _suggestionAdornment.HideSuggestion();
                            return VSConstants.S_OK;

                        case (uint)VSConstants.VSStd2KCmdID.END:
                            System.Diagnostics.Debug.WriteLine("End intercepted by CommandFilter");
                            if (_suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggestion accepted with End - BLOCKING PROPAGATION");
                                return VSConstants.S_OK; // Block propagation
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.BACKSPACE:
                        case (uint)VSConstants.VSStd2KCmdID.DELETE:
                            System.Diagnostics.Debug.WriteLine("Backspace/Delete intercepted, hiding suggestion");
                            _suggestionAdornment.HideSuggestion();
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.TYPECHAR:
                            System.Diagnostics.Debug.WriteLine("Character typed, hiding suggestion");
                            _suggestionAdornment.HideSuggestion();
                            break;
                    }
                }
            }

            // Pass the command to the next handler
            return _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        private bool IsAtEndOfLine()
        {
            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition.Position;
                var line = _textView.TextSnapshot.GetLineFromPosition(caretPosition);
                var positionInLine = caretPosition - line.Start.Position;
                var lineText = line.GetText();

                return positionInLine >= lineText.Length ||
                       string.IsNullOrWhiteSpace(lineText.Substring(positionInLine));
            }
            catch
            {
                return false;
            }
        }
    }
}