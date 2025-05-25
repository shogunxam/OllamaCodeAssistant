using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OllamaCodeAssistant
{
    internal class InlineSuggestionAdornment
    {
        private readonly IWpfTextView _textView;
        private readonly IAdornmentLayer _adornmentLayer;
        private TextBlock _suggestionTextBlock;
        private string _currentSuggestion;
        private int _suggestionStartPosition;
        private bool _isVisible;
        private readonly object _lockObject = new object();

        // Proprietà pubblica per permettere al KeyProcessor di controllare lo stato
        public bool IsVisible
        {
            get
            {
                lock (_lockObject)
                {
                    return _isVisible &&
                           !string.IsNullOrEmpty(_currentSuggestion) &&
                           _suggestionTextBlock != null;
                }
            }
        }


        public InlineSuggestionAdornment(IWpfTextView textView)
        {
            _textView = textView;
            _adornmentLayer = textView.GetAdornmentLayer("InlineSuggestion");

            // Sottoscrivi agli eventi
            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _textView.Caret.PositionChanged += OnCaretPositionChanged;

            System.Diagnostics.Debug.WriteLine("InlineSuggestionAdornment creato");
        }
        public void ShowSuggestion(string suggestion, int position)
        {
            lock (_lockObject)
            {
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    HideSuggestion();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"ShowSuggestion chiamato con: '{suggestion}' alla posizione {position}");

                // Nascondi eventuali suggerimenti di IntelliCode quando mostriamo il nostro
                DismissIntelliCodeSuggestions();

                _currentSuggestion = suggestion;
                _suggestionStartPosition = position;
                _isVisible = true;

                CreateSuggestionTextBlock();
                PositionSuggestion();
            }
        }

        public void HideSuggestion()
        {
            lock (_lockObject)
            {
                if (_suggestionTextBlock != null)
                {
                    _adornmentLayer.RemoveAdornment(_suggestionTextBlock);
                    _suggestionTextBlock = null;
                }
                _currentSuggestion = null;
                _isVisible = false;
                System.Diagnostics.Debug.WriteLine("Suggerimento nascosto");
            }
        }

        private void DismissIntelliCodeSuggestions()
        {
            try
            {
                // Prova a chiudere le sessioni di completamento attive
                var componentModel = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider.GetService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel)) as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;

                if (componentModel != null)
                {
                    var completionBroker = componentModel.GetService<Microsoft.VisualStudio.Language.Intellisense.ICompletionBroker>();
                    if (completionBroker != null)
                    {
                        var sessions = completionBroker.GetSessions(_textView);
                        foreach (var session in sessions.Where(s => !s.IsDismissed))
                        {
                            session.Dismiss();
                            System.Diagnostics.Debug.WriteLine("Sessione IntelliCode chiusa");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore nel chiudere IntelliCode: {ex.Message}");
            }
        }
        public bool IsCaretAtSuggestionPosition(int caretPosition)
        {
            lock (_lockObject)
            {
                return _isVisible &&
                       !string.IsNullOrEmpty(_currentSuggestion) &&
                       caretPosition == _suggestionStartPosition;
            }
        }

        public bool AcceptSuggestion()
        {
            string suggestionToAccept;
            int startPosition;
            bool isCurrentlyVisible;

            // Copia le variabili dentro il lock
            lock (_lockObject)
            {
                suggestionToAccept = _currentSuggestion;
                startPosition = _suggestionStartPosition;
                isCurrentlyVisible = _isVisible;
            }

            if (!isCurrentlyVisible || string.IsNullOrEmpty(suggestionToAccept))
            {
                System.Diagnostics.Debug.WriteLine("AcceptSuggestion: suggerimento non visibile o vuoto");
                return false;
            }

            if (!isCurrentlyVisible || string.IsNullOrEmpty(suggestionToAccept))
            {
                System.Diagnostics.Debug.WriteLine("AcceptSuggestion: suggerimento non visibile o vuoto");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: inserisco '{suggestionToAccept}' alla posizione {startPosition}");

                // Verifica che la posizione sia ancora valida
                if (startPosition > _textView.TextSnapshot.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: posizione {startPosition} non valida per snapshot di lunghezza {_textView.TextSnapshot.Length}");
                    HideSuggestion();
                    return false;
                }

                // Verifica che il cursore sia ancora nella posizione corretta
                var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                if (currentCaretPosition != startPosition)
                {
                    System.Diagnostics.Debug.WriteLine($"Cursore spostato da {startPosition} a {currentCaretPosition}");
                    HideSuggestion();
                    return false;
                }

                // Nascondi il suggerimento PRIMA di modificare il testo per evitare interferenze
                HideSuggestion();

                // Disabilita temporaneamente gli eventi per evitare interferenze
                _textView.TextBuffer.Changed -= OnTextBufferChanged;

                try
                {
                    var edit = _textView.TextBuffer.CreateEdit();
                    edit.Insert(startPosition, suggestionToAccept);
                    var snapshot = edit.Apply();

                    if (snapshot != null)
                    {
                        // Muovi il cursore alla fine del testo inserito
                        var newPosition = startPosition + suggestionToAccept.Length;
                        if (newPosition <= snapshot.Length)
                        {
                            var newPoint = new SnapshotPoint(snapshot, newPosition);
                            _textView.Caret.MoveTo(newPoint);
                            System.Diagnostics.Debug.WriteLine($"Cursore spostato alla posizione {newPosition}");
                        }

                        System.Diagnostics.Debug.WriteLine("Suggerimento accettato con successo");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("AcceptSuggestion: edit.Apply() ha restituito null");
                        return false;
                    }
                }
                finally
                {
                    // Riabilita gli eventi
                    _textView.TextBuffer.Changed += OnTextBufferChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore nell'accettazione del suggerimento: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Riabilita gli eventi in caso di errore
                _textView.TextBuffer.Changed += OnTextBufferChanged;
                return false;
            }
        }


        private void CreateSuggestionTextBlock()
        {
            if (_suggestionTextBlock != null)
            {
                _adornmentLayer.RemoveAdornment(_suggestionTextBlock);
                _suggestionTextBlock = null;
            }

            _suggestionTextBlock = new TextBlock
            {
                Text = _currentSuggestion,
                Foreground = new SolidColorBrush(Colors.LightGreen),
                Opacity = 0.8,
                FontFamily = _textView.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                FontStyle = FontStyles.Italic,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0))
            };
        }

        private void PositionSuggestion()
        {
            if (_suggestionTextBlock == null || !_isVisible)
                return;

            try
            {
                var snapshot = _textView.TextSnapshot;
                if (_suggestionStartPosition > snapshot.Length)
                {
                    HideSuggestion();
                    return;
                }

                var snapshotPoint = new SnapshotPoint(snapshot, _suggestionStartPosition);

                // Verifica che la linea sia visibile
                if (!_textView.TextViewLines.ContainsBufferPosition(snapshotPoint))
                {
                    HideSuggestion();
                    return;
                }

                var line = _textView.GetTextViewLineContainingBufferPosition(snapshotPoint);
                if (line == null)
                {
                    HideSuggestion();
                    return;
                }

                var characterBounds = line.GetCharacterBounds(snapshotPoint);

                Canvas.SetLeft(_suggestionTextBlock, characterBounds.Left);
                Canvas.SetTop(_suggestionTextBlock, characterBounds.Top);

                _adornmentLayer.RemoveAdornment(_suggestionTextBlock);
                _adornmentLayer.AddAdornment(
                    AdornmentPositioningBehavior.TextRelative,
                    line.Extent,
                    null,
                    _suggestionTextBlock,
                    null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore nel posizionamento del suggerimento: {ex.Message}");
                HideSuggestion();
            }
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_isVisible)
            {
                PositionSuggestion();
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Se l'utente continua a scrivere, nascondi il suggerimento
            if (_isVisible)
            {
                var caretPosition = _textView.Caret.Position.BufferPosition.Position;

                // Se il cursore si è mosso oltre la posizione del suggerimento, nascondilo
                if (caretPosition != _suggestionStartPosition)
                {
                    HideSuggestion();
                }
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            // Nascondi il suggerimento se il cursore si muove
            if (_isVisible)
            {
                var caretPosition = e.NewPosition.BufferPosition.Position;
                if (caretPosition != _suggestionStartPosition)
                {
                    HideSuggestion();
                }
            }
        }

        public void Dispose()
        {
            HideSuggestion();

            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.TextBuffer.Changed -= OnTextBufferChanged;
            _textView.Caret.PositionChanged -= OnCaretPositionChanged;
        }
    }
}
