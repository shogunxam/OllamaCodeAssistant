using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Shell;
using OllamaCodeAssistant.Options;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System;

namespace OllamaCodeAssistant
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class TextViewEventListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            System.Diagnostics.Debug.WriteLine($"TextViewCreated chiamato per: {textView.TextBuffer.ContentType.TypeName}");

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var package = OllamaCodeAssistantPackage.Instance;
                if (package != null)
                {
                    var optionsPage = package.GetDialogPage(typeof(OllamaOptionsPage)) as OllamaOptionsPage;
                    if (optionsPage != null)
                    {
                        var llmManager = new LLMInteractionManager(optionsPage);
                        new AutoCompleteHandler(textView, llmManager);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Package instance non disponibile");
                }
            });
        }
    }

    internal class AutoCompleteHandler
    {
        private readonly IWpfTextView _textView;
        private readonly LLMInteractionManager _llmManager;
        private readonly InlineSuggestionAdornment _suggestionAdornment;
        private bool _isProcessing = false;
        private System.Threading.Timer _debounceTimer;
        private const int DEBOUNCE_DELAY_MS = 1000; // 1 secondo di delay

        public AutoCompleteHandler(IWpfTextView textView, LLMInteractionManager llmManager)
        {
            _textView = textView;
            _llmManager = llmManager;
            _suggestionAdornment = new InlineSuggestionAdornment(_textView);

            System.Diagnostics.Debug.WriteLine("AutoCompleteHandler creato");

            _textView.TextBuffer.Changed += OnTextChanged;
            _textView.Closed += OnTextViewClosed;
        }

        private void OnTextViewClosed(object sender, System.EventArgs e)
        {
            _debounceTimer?.Dispose();
            _suggestionAdornment?.Dispose();
            _textView.TextBuffer.Changed -= OnTextChanged;
            _textView.Closed -= OnTextViewClosed;
        }

        private void OnTextChanged(object sender, TextContentChangedEventArgs e)
        {
            // Nascondi il suggerimento corrente quando l'utente scrive
            _suggestionAdornment.HideSuggestion();

            // Usa un timer per evitare troppe chiamate durante la digitazione
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(OnDebounceTimerElapsed, null, DEBOUNCE_DELAY_MS, System.Threading.Timeout.Infinite);
        }

        private async void OnDebounceTimerElapsed(object state)
        {
            if (_isProcessing || _llmManager.IsRequestActive)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var caretPosition = _textView.Caret.Position.BufferPosition;
                var currentText = _textView.TextSnapshot.GetText(0, caretPosition.Position);

                // Non suggerire se il testo è troppo corto o finisce con spazio/newline
                if (currentText.Length < 10 ||
                    currentText.EndsWith(" ") ||
                    currentText.EndsWith("\n") ||
                    currentText.EndsWith("\r\n"))
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Richiesta suggerimento per posizione: {caretPosition.Position}");

                _isProcessing = true;

                string prompt = $"Complete this code with just the next logical line or completion. Return ONLY the new code without any explanations, comments, or code block markers. Do not repeat the existing code.\n\nExisting code:\n{currentText}\n\nNew code:";

                string response = await _llmManager.GetOneShotResponseAsync(prompt);

                if (!string.IsNullOrWhiteSpace(response))
                {
                    string cleanSuggestion = ExtractNewCodeFromResponse(response, currentText);
                    if (!string.IsNullOrWhiteSpace(cleanSuggestion))
                    {
                        // Verifica che il cursore sia ancora nella stessa posizione
                        var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                        if (currentCaretPosition == caretPosition.Position)
                        {
                            _suggestionAdornment.ShowSuggestion(cleanSuggestion, caretPosition.Position);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore durante il completamento AI: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private string ExtractNewCodeFromResponse(string response, string originalCode)
        {
            try
            {
                // Rimuovi code blocks se presenti
                string cleanResponse = RemoveCodeBlocks(response);

                // Rimuovi il codice originale se è presente nella risposta
                string newCode = RemoveOriginalCode(cleanResponse, originalCode);

                // Pulisci spazi e newline extra
                newCode = newCode.Trim();

                // Se il nuovo codice è troppo lungo, prendi solo la prima riga
                var lines = newCode.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    newCode = lines[0].Trim();
                }

                System.Diagnostics.Debug.WriteLine($"Codice originale: {originalCode}");
                System.Diagnostics.Debug.WriteLine($"Risposta completa: {response}");
                System.Diagnostics.Debug.WriteLine($"Nuovo codice estratto: {newCode}");

                return newCode;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore nell'estrazione del codice: {ex.Message}");
                return string.Empty;
            }
        }

        private string RemoveCodeBlocks(string text)
        {
            // Rimuovi code blocks markdown (```...```)
            var codeBlockPattern = @"```[\s\S]*?```";
            var matches = Regex.Matches(text, codeBlockPattern);

            if (matches.Count > 0)
            {
                // Prendi il contenuto del primo code block
                var match = matches[0];
                var content = match.Value;
                // Rimuovi i delimitatori ``` e eventuali specificatori di linguaggio
                content = Regex.Replace(content, @"^```\w*\s*", "", RegexOptions.Multiline);
                content = Regex.Replace(content, @"```$", "", RegexOptions.Multiline);
                return content.Trim();
            }

            // Se non ci sono code blocks, ritorna il testo originale
            return text;
        }

        private string RemoveOriginalCode(string response, string originalCode)
        {
            // Se la risposta contiene il codice originale, rimuovilo
            if (response.Contains(originalCode))
            {
                int index = response.IndexOf(originalCode);
                if (index >= 0)
                {
                    // Prendi tutto quello che viene dopo il codice originale
                    string afterOriginal = response.Substring(index + originalCode.Length);
                    return afterOriginal.Trim();
                }
            }

            // Se il codice originale non è presente, controlla se la risposta inizia con parte del codice originale
            var originalLines = originalCode.Split('\n');
            var responseLines = response.Split('\n');

            // Trova dove inizia il nuovo codice
            int skipLines = 0;
            for (int i = 0; i < Math.Min(originalLines.Length, responseLines.Length); i++)
            {
                if (originalLines[originalLines.Length - 1 - i].Trim() == responseLines[i].Trim())
                {
                    skipLines = i + 1;
                }
                else
                {
                    break;
                }
            }

            if (skipLines > 0 && skipLines < responseLines.Length)
            {
                return string.Join("\n", responseLines.Skip(skipLines));
            }

            return response;
        }
    }
}
