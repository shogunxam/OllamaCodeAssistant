using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
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
            // Subscribe to events
            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnTextBufferChanged;
            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            System.Diagnostics.Debug.WriteLine("InlineSuggestionAdornment created");
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
                System.Diagnostics.Debug.WriteLine($"ShowSuggestion called with: '{suggestion}' at position {position}");
                // Hide any IntelliCode suggestions when showing our own
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
                System.Diagnostics.Debug.WriteLine("Suggestion hidden");
            }
        }

        private void DismissIntelliCodeSuggestions()
        {
            try
            {
                // Try to close any active completion sessions
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
                            System.Diagnostics.Debug.WriteLine("IntelliCode session closed");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing IntelliCode: {ex.Message}");
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
            // Copy variables inside the lock
            lock (_lockObject)
            {
                suggestionToAccept = _currentSuggestion;
                startPosition = _suggestionStartPosition;
                isCurrentlyVisible = _isVisible;
            }
            if (!isCurrentlyVisible || string.IsNullOrEmpty(suggestionToAccept))
            {
                System.Diagnostics.Debug.WriteLine("AcceptSuggestion: suggestion not visible or empty");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: inserting '{suggestionToAccept}' at position {startPosition}");
                // Check that the position is still valid
                if (startPosition > _textView.TextSnapshot.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"AcceptSuggestion: position {startPosition} is invalid for snapshot length {_textView.TextSnapshot.Length}");
                    HideSuggestion();
                    return false;
                }

                // Verify that the cursor is still at the correct position
                var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                if (currentCaretPosition != startPosition)
                {
                    System.Diagnostics.Debug.WriteLine($"Cursor moved from {startPosition} to {currentCaretPosition}");
                    HideSuggestion();
                    return false;
                }

                // Hide the suggestion BEFORE modifying text to avoid interference
                HideSuggestion();

                // Temporarily disable events to prevent interference
                _textView.TextBuffer.Changed -= OnTextBufferChanged;
                try
                {
                    var edit = _textView.TextBuffer.CreateEdit();
                    edit.Insert(startPosition, suggestionToAccept);
                    var snapshot = edit.Apply();
                    if (snapshot != null)
                    {
                        // Move cursor to the end of the inserted text
                        var newPosition = startPosition + suggestionToAccept.Length;
                        if (newPosition <= snapshot.Length)
                        {
                            var newPoint = new SnapshotPoint(snapshot, newPosition);
                            _textView.Caret.MoveTo(newPoint);
                            System.Diagnostics.Debug.WriteLine($"Cursor moved to position {newPosition}");
                        }
                        System.Diagnostics.Debug.WriteLine("Suggestion accepted successfully");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("AcceptSuggestion: edit.Apply() returned null");
                        return false;
                    }
                }
                finally
                {
                    // Re-enable events
                    _textView.TextBuffer.Changed += OnTextBufferChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accepting suggestion: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                // Re-enable events in case of error
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

            // Simplified version using only VS theme colors
            var suggestionBrush = GetAdaptiveSuggestionBrush();
            _suggestionTextBlock = new TextBlock
            {
                Text = _currentSuggestion,
                Foreground = suggestionBrush,
                Opacity = 0.6,
                FontFamily = _textView.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                FontStyle = FontStyles.Italic,
                FontWeight = FontWeights.Normal,
                IsHitTestVisible = false,
                Background = Brushes.Transparent
            };
        }

        private Brush GetAdaptiveSuggestionBrush()
        {
            try
            {
                // Method 1: Try to use VS theme colors (with proper conversion)
                try
                {
                    var drawingColor = VSColorTheme.GetThemedColor(EnvironmentColors.SystemGrayTextColorKey);
                    var mediaColor = ConvertDrawingColorToMediaColor(drawingColor);
                    return new SolidColorBrush(mediaColor);
                }
                catch
                {
                    // Proceed to next method
                }

                // Method 2: Determine based on editor background color
                var editorBackground = _textView.Background;
                if (editorBackground is SolidColorBrush backgroundBrush)
                {
                    var bgColor = backgroundBrush.Color;
                    // Calculate luminance
                    double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255;
                    if (luminance > 0.5)
                    {
                        // Light theme: use dark gray
                        return new SolidColorBrush(Color.FromRgb(100, 100, 100));
                    }
                    else
                    {
                        // Dark theme: use light gray
                        return new SolidColorBrush(Color.FromRgb(200, 200, 200));
                    }
                }

                // Method 3: Use the editor's text color with reduced opacity
                var textProperties = _textView.FormattedLineSource.DefaultTextProperties;
                if (textProperties.ForegroundBrush is SolidColorBrush textBrush)
                {
                    var textColor = textBrush.Color;
                    // Create a more transparent version of the text color
                    return new SolidColorBrush(Color.FromArgb(
                        150, // Reduced alpha for transparency
                        textColor.R,
                        textColor.G,
                        textColor.B
                    ));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error determining adaptive color: {ex.Message}");
            }

            // Final fallback
            return new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }

        // Helper method to convert System.Drawing.Color to System.Windows.Media.Color
        private Color ConvertDrawingColorToMediaColor(System.Drawing.Color drawingColor)
        {
            return Color.FromArgb(
                drawingColor.A,
                drawingColor.R,
                drawingColor.G,
                drawingColor.B
            );
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

                // Verify that the line is visible
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
                System.Diagnostics.Debug.WriteLine($"Error positioning suggestion: {ex.Message}");
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
            // If user keeps typing, hide the suggestion
            if (_isVisible)
            {
                var caretPosition = _textView.Caret.Position.BufferPosition.Position;
                // If the cursor has moved beyond the suggestion position, hide it
                if (caretPosition != _suggestionStartPosition)
                {
                    HideSuggestion();
                }
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            // Hide the suggestion if the cursor moves
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