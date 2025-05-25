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

        public InlineSuggestionAdornment(IWpfTextView textView)
        {
            _textView = textView;
            _adornmentLayer = textView.GetAdornmentLayer("InlineSuggestion");

            // Sottoscrivi agli eventi
            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _textView.Caret.PositionChanged += OnCaretPositionChanged;

            // Accedi al controllo WPF per gli eventi di focus e tastiera
            if (_textView.VisualElement != null)
            {
                _textView.VisualElement.LostKeyboardFocus += OnLostFocus;
                _textView.VisualElement.PreviewKeyDown += OnKeyDown;
            }
        }

        public void ShowSuggestion(string suggestion, int position)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                HideSuggestion();
                return;
            }
            System.Diagnostics.Debug.WriteLine($"ShowSuggestion chiamato con: '{suggestion}' alla posizione {position}");

            _currentSuggestion = suggestion;
            _suggestionStartPosition = position;
            _isVisible = true;

            CreateSuggestionTextBlock();
            PositionSuggestion();
        }

        public void HideSuggestion()
        {
            if (_suggestionTextBlock != null)
            {
                _adornmentLayer.RemoveAdornment(_suggestionTextBlock);
                _suggestionTextBlock = null;
            }
            _currentSuggestion = null;
            _isVisible = false;
        }

        public bool AcceptSuggestion()
        {
            if (!_isVisible || string.IsNullOrEmpty(_currentSuggestion))
            {
                System.Diagnostics.Debug.WriteLine("AcceptSuggestion: suggerimento non visibile o vuoto");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: inserisco '{_currentSuggestion}' alla posizione {_suggestionStartPosition}");

                // Verifica che la posizione sia ancora valida
                if (_suggestionStartPosition > _textView.TextSnapshot.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: posizione {_suggestionStartPosition} non valida per snapshot di lunghezza {_textView.TextSnapshot.Length}");
                    HideSuggestion();
                    return false;
                }

                // Disabilita temporaneamente gli eventi per evitare interferenze
                _textView.TextBuffer.Changed -= OnTextBufferChanged;

                var edit = _textView.TextBuffer.CreateEdit();
                edit.Insert(_suggestionStartPosition, _currentSuggestion);
                var snapshot = edit.Apply();

                // Riabilita gli eventi
                _textView.TextBuffer.Changed += OnTextBufferChanged;

                if (snapshot != null)
                {
                    // Muovi il cursore alla fine del testo inserito
                    var newPosition = _suggestionStartPosition + _currentSuggestion.Length;
                    if (newPosition <= snapshot.Length)
                    {
                        var newPoint = new SnapshotPoint(snapshot, newPosition);
                        _textView.Caret.MoveTo(newPoint);
                        System.Diagnostics.Debug.WriteLine($"Cursore spostato alla posizione {newPosition}");
                    }

                    HideSuggestion();
                    System.Diagnostics.Debug.WriteLine("Suggerimento accettato con successo");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("AcceptSuggestion: edit.Apply() ha restituito null");
                    HideSuggestion();
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore nell'accettazione del suggerimento: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Riabilita gli eventi in caso di errore
                _textView.TextBuffer.Changed += OnTextBufferChanged;
                HideSuggestion();
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
                Foreground = new SolidColorBrush(Colors.Gray),
                Opacity = 0.6,
                FontFamily = _textView.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                FontStyle = FontStyles.Italic,
                IsHitTestVisible = false // Non intercetta i click del mouse
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

        private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            HideSuggestion();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isVisible || string.IsNullOrEmpty(_currentSuggestion))
                return;

            System.Diagnostics.Debug.WriteLine($"Tasto premuto: {e.Key}, suggerimento attivo: '{_currentSuggestion}'");

            // Verifica che il cursore sia ancora nella posizione corretta
            var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
            if (currentCaretPosition != _suggestionStartPosition)
            {
                System.Diagnostics.Debug.WriteLine($"Cursore spostato da {_suggestionStartPosition} a {currentCaretPosition}, nascondo suggerimento");
                HideSuggestion();
                return;
            }

            switch (e.Key)
            {
                case Key.Tab:
                    System.Diagnostics.Debug.WriteLine("Tab premuto, accetto suggerimento");
                    if (AcceptSuggestion())
                    {
                        e.Handled = true;
                        System.Diagnostics.Debug.WriteLine("Suggerimento accettato con Tab");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Errore nell'accettazione del suggerimento con Tab");
                    }
                    break;

                case Key.Right:
                    // Accetta solo se siamo alla fine della riga o se non c'è altro testo dopo il cursore
                    var line = _textView.TextSnapshot.GetLineFromPosition(_suggestionStartPosition);
                    var positionInLine = _suggestionStartPosition - line.Start.Position;
                    var lineText = line.GetText();

                    if (positionInLine >= lineText.Length || string.IsNullOrWhiteSpace(lineText.Substring(positionInLine)))
                    {
                        System.Diagnostics.Debug.WriteLine("Freccia destra premuta alla fine della riga, accetto suggerimento");
                        if (AcceptSuggestion())
                        {
                            e.Handled = true;
                            System.Diagnostics.Debug.WriteLine("Suggerimento accettato con freccia destra");
                        }
                    }
                    break;

                case Key.Escape:
                    System.Diagnostics.Debug.WriteLine("Escape premuto, nascondo suggerimento");
                    HideSuggestion();
                    e.Handled = true;
                    break;

                case Key.End:
                    System.Diagnostics.Debug.WriteLine("End premuto, accetto suggerimento");
                    if (AcceptSuggestion())
                    {
                        e.Handled = true;
                        System.Diagnostics.Debug.WriteLine("Suggerimento accettato con End");
                    }
                    break;

                default:
                    if (IsTextModifyingKey(e.Key))
                    {
                        System.Diagnostics.Debug.WriteLine($"Tasto modificatore di testo premuto: {e.Key}, nascondo suggerimento");
                        HideSuggestion();
                    }
                    break;
            }
        }

        private static readonly HashSet<Key> NonModifyingKeys = new HashSet<Key>
    {
        Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt, Key.RightAlt,
        Key.LeftShift, Key.RightShift,
        Key.Left, Key.Right, Key.Up, Key.Down,
        Key.Home, Key.End,
        Key.PageUp, Key.PageDown,
        Key.Insert, Key.CapsLock, Key.NumLock,
        Key.PrintScreen, Key.Pause,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6,
        Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        Key.LWin, Key.RWin, Key.Apps
    };
        private bool IsTextModifyingKey(Key key)
        {
            return !NonModifyingKeys.Contains(key);
        }

        public void Dispose()
        {
            HideSuggestion();

            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.TextBuffer.Changed -= OnTextBufferChanged;
            _textView.Caret.PositionChanged -= OnCaretPositionChanged;

            if (_textView.VisualElement != null)
            {
                _textView.VisualElement.LostKeyboardFocus -= OnLostFocus;
                _textView.VisualElement.PreviewKeyDown -= OnKeyDown;
            }
        }
    }
}
