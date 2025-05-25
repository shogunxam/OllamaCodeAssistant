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
            System.Diagnostics.Debug.WriteLine("OllamaCommandFilter creato");
        }

        public void SetSuggestionAdornment(InlineSuggestionAdornment adornment)
        {
            _suggestionAdornment = adornment;
            System.Diagnostics.Debug.WriteLine("SuggestionAdornment impostato nel CommandFilter");
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
                System.Diagnostics.Debug.WriteLine($"Suggerimento visibile, controllo comando: {nCmdID}");

                // Verifica che il cursore sia ancora nella posizione corretta
                var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                if (!_suggestionAdornment.IsCaretAtSuggestionPosition(currentCaretPosition))
                {
                    System.Diagnostics.Debug.WriteLine("Cursore non nella posizione del suggerimento, nascondo");
                    _suggestionAdornment.HideSuggestion();
                    return _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }

                // Verifica se è un comando che ci interessa
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch (nCmdID)
                    {
                        case (uint)VSConstants.VSStd2KCmdID.TAB:
                            System.Diagnostics.Debug.WriteLine("Tab intercettato dal CommandFilter");
                            if (_suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggerimento accettato con Tab - BLOCCANDO PROPAGAZIONE");
                                return VSConstants.S_OK; // Blocca la propagazione
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.RETURN:
                            System.Diagnostics.Debug.WriteLine("Enter intercettato dal CommandFilter");
                            if (_suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggerimento accettato con Enter - BLOCCANDO PROPAGAZIONE");
                                return VSConstants.S_OK; // Blocca la propagazione
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.RIGHT:
                            System.Diagnostics.Debug.WriteLine("Freccia destra intercettata dal CommandFilter");
                            if (IsAtEndOfLine() && _suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggerimento accettato con freccia destra - BLOCCANDO PROPAGAZIONE");
                                return VSConstants.S_OK; // Blocca la propagazione
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.CANCEL:
                            System.Diagnostics.Debug.WriteLine("Escape intercettato dal CommandFilter");
                            _suggestionAdornment.HideSuggestion();
                            return VSConstants.S_OK;

                        case (uint)VSConstants.VSStd2KCmdID.END:
                            System.Diagnostics.Debug.WriteLine("End intercettato dal CommandFilter");
                            if (_suggestionAdornment.AcceptSuggestion())
                            {
                                System.Diagnostics.Debug.WriteLine("Suggerimento accettato con End - BLOCCANDO PROPAGAZIONE");
                                return VSConstants.S_OK; // Blocca la propagazione
                            }
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.BACKSPACE:
                        case (uint)VSConstants.VSStd2KCmdID.DELETE:
                            System.Diagnostics.Debug.WriteLine($"Backspace/Delete intercettato, nascondo suggerimento");
                            _suggestionAdornment.HideSuggestion();
                            break;

                        case (uint)VSConstants.VSStd2KCmdID.TYPECHAR:
                            System.Diagnostics.Debug.WriteLine("Carattere digitato, nascondo suggerimento");
                            _suggestionAdornment.HideSuggestion();
                            break;
                    }
                }
            }

            // Passa il comando al prossimo handler
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
