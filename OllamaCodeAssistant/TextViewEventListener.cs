using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using OllamaCodeAssistant.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OllamaCodeAssistant
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class TextViewEventListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            System.Diagnostics.Debug.WriteLine($"TextViewCreated called for: {textView.TextBuffer.ContentType.TypeName}");
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
                    System.Diagnostics.Debug.WriteLine("Package instance not available");
                }
            });
        }

        internal class AutoCompleteHandler
        {
            private readonly IWpfTextView _textView;
            private readonly LLMInteractionManager _llmManager;
            private readonly InlineSuggestionAdornment _suggestionAdornment;
            private bool _isProcessing = false;
            private System.Threading.Timer _debounceTimer;
            private const int DEBOUNCE_DELAY_MS = 1000; // 1 second delay

            public AutoCompleteHandler(IWpfTextView textView, LLMInteractionManager llmManager)
            {
                _textView = textView;
                _llmManager = llmManager;
                _suggestionAdornment = new InlineSuggestionAdornment(_textView);
                // Configure the Command Filter instead of KeyProcessor
                SetupCommandFilter();
                System.Diagnostics.Debug.WriteLine("AutoCompleteHandler created");
                _textView.TextBuffer.Changed += OnTextChanged;
                _textView.Closed += OnTextViewClosed;
            }

            // New method to determine the programming language
            private string GetProgrammingLanguage()
            {
                try
                {
                    // 1. Try to get the language from ContentType
                    var contentType = _textView.TextBuffer.ContentType.TypeName;
                    System.Diagnostics.Debug.WriteLine($"ContentType: {contentType}");

                    // Map content types to languages
                    var languageMap = new Dictionary<string, string>
                    {
                        { "CSharp", "C#" },
                        { "csharp", "C#" },
                        { "C/C++", "C++" },
                        { "cpp", "C++" },
                        { "JavaScript", "JavaScript" },
                        { "TypeScript", "TypeScript" },
                        { "Python", "Python" },
                        { "Java", "Java" },
                        { "HTML", "HTML" },
                        { "CSS", "CSS" },
                        { "XML", "XML" },
                        { "JSON", "JSON" },
                        { "SQL", "SQL" },
                        { "PowerShell", "PowerShell" },
                        { "VB", "Visual Basic" },
                        { "F#", "F#" },
                        { "XAML", "XAML" }
                    };

                    if (languageMap.ContainsKey(contentType))
                    {
                        return languageMap[contentType];
                    }

                    // 2. Try to determine by file name
                    if (_textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument))
                    {
                        var fileName = textDocument.FilePath;
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            var extension = Path.GetExtension(fileName).ToLowerInvariant();
                            System.Diagnostics.Debug.WriteLine($"File extension: {extension}");

                            var extensionMap = new Dictionary<string, string>
                            {
                                { ".cs", "C#" },
                                { ".cpp", "C++" },
                                { ".cc", "C++" },
                                { ".cxx", "C++" },
                                { ".c", "C" },
                                { ".h", "C/C++" },
                                { ".hpp", "C++" },
                                { ".js", "JavaScript" },
                                { ".ts", "TypeScript" },
                                { ".py", "Python" },
                                { ".java", "Java" },
                                { ".html", "HTML" },
                                { ".htm", "HTML" },
                                { ".css", "CSS" },
                                { ".xml", "XML" },
                                { ".json", "JSON" },
                                { ".sql", "SQL" },
                                { ".ps1", "PowerShell" },
                                { ".vb", "Visual Basic" },
                                { ".fs", "F#" },
                                { ".xaml", "XAML" },
                                { ".php", "PHP" },
                                { ".rb", "Ruby" },
                                { ".go", "Go" },
                                { ".rs", "Rust" },
                                { ".kt", "Kotlin" },
                                { ".swift", "Swift" }
                            };

                            if (extensionMap.ContainsKey(extension))
                            {
                                return extensionMap[extension];
                            }
                        }
                    }

                    // 3. Try to determine by file content
                    var text = _textView.TextSnapshot.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // Search for language-specific patterns
                        if (text.Contains("using System") || text.Contains("namespace ") || text.Contains("public class"))
                            return "C#";
                        if (text.Contains("#include") || text.Contains("std::"))
                            return "C++";
                        if (text.Contains("function ") || text.Contains("var ") || text.Contains("const ") || text.Contains("let "))
                            return "JavaScript";
                        if (text.Contains("def ") || text.Contains("import ") || text.Contains("from "))
                            return "Python";
                        if (text.Contains("public static void main") || text.Contains("import java"))
                            return "Java";
                        if (text.Contains("<!DOCTYPE") || text.Contains("<html"))
                            return "HTML";
                    }

                    System.Diagnostics.Debug.WriteLine("Language not determined, using 'text' as default");
                    return "text";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error determining language: {ex.Message}");
                    return "text";
                }
            }

            private void SetupCommandFilter()
            {
                try
                {
                    // Get the command filter from TextView properties
                    if (_textView.Properties.TryGetProperty(typeof(OllamaCommandFilter), out OllamaCommandFilter commandFilter))
                    {
                        commandFilter.SetSuggestionAdornment(_suggestionAdornment);
                        System.Diagnostics.Debug.WriteLine("CommandFilter configured successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("CommandFilter not found in properties");
                        // Try after a short delay
                        System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                        {
                            Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                            {
                                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                if (_textView.Properties.TryGetProperty(typeof(OllamaCommandFilter), out OllamaCommandFilter delayedFilter))
                                {
                                    delayedFilter.SetSuggestionAdornment(_suggestionAdornment);
                                    System.Diagnostics.Debug.WriteLine("CommandFilter configured successfully (delayed)");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("CommandFilter still not found after delay");
                                }
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error configuring CommandFilter: {ex.Message}");
                }
            }

            private void OnTextViewClosed(object sender, EventArgs e)
            {
                _debounceTimer?.Dispose();
                _suggestionAdornment?.Dispose();
                _textView.TextBuffer.Changed -= OnTextChanged;
                _textView.Closed -= OnTextViewClosed;
            }

            private void OnTextChanged(object sender, TextContentChangedEventArgs e)
            {
                // Hide current suggestion when user is typing
                _suggestionAdornment.HideSuggestion();

                // Use a timer to avoid too many calls during typing
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
                    const int cBlockMaxSize = 100;
                    var caretPosition = _textView.Caret.Position.BufferPosition;
                    var startPosition = caretPosition.Position >= cBlockMaxSize ? (caretPosition.Position - cBlockMaxSize) : 0;
                    var blockSize = caretPosition.Position - startPosition;
                    var codeBefore = _textView.TextSnapshot.GetText(startPosition, blockSize);
                    blockSize = caretPosition.Position + cBlockMaxSize > _textView.TextSnapshot.Length ? _textView.TextSnapshot.Length - caretPosition.Position : cBlockMaxSize;
                    var codeAfter = _textView.TextSnapshot.GetText(caretPosition.Position, blockSize);

                    // Don't suggest if text is too short or ends with space/newline
                    if (codeBefore.Length < 10 ||
                        codeBefore.EndsWith(" ") ||
                        codeBefore.EndsWith("\n") ||
                        codeBefore.EndsWith("\r\n"))
                    {
                        return;
                    }

                    // Determine programming language
                    string language = GetProgrammingLanguage();
                    System.Diagnostics.Debug.WriteLine($"Requesting suggestion for text: {codeBefore}");
                    System.Diagnostics.Debug.WriteLine($"Requesting suggestion for position: {caretPosition.Position}");
                    System.Diagnostics.Debug.WriteLine($"Detected language: {language}");

                    _isProcessing = true;

                    // Pass also the language to the function
                    string response = await _llmManager.GetCodeCompletionAsync(codeBefore, codeAfter, language);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        string cleanSuggestion = ExtractNewCodeFromResponse(response, codeBefore);
                        if (!string.IsNullOrWhiteSpace(cleanSuggestion))
                        {
                            // Check that the cursor is still at the same position
                            var currentCaretPosition = _textView.Caret.Position.BufferPosition.Position;
                            if (currentCaretPosition == caretPosition.Position)
                            {
                                _suggestionAdornment.ShowSuggestion(cleanSuggestion, caretPosition.Position);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during AI completion: {ex.Message}");
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
                    // Remove code blocks if present
                    string cleanResponse = RemoveCodeBlocks(response);

                    // Remove the original code if present in the response
                    string newCode = RemoveOriginalCode(cleanResponse, originalCode);

                    // Clean extra spaces and newlines
                    newCode = newCode.Trim();

                    /*
                    // If the new code is too long, take only the first line
                    var lines = newCode.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        newCode = lines[0].Trim();
                    }
                    */
                    System.Diagnostics.Debug.WriteLine($"Original code: {originalCode}");
                    System.Diagnostics.Debug.WriteLine($"Full response: {response}");
                    System.Diagnostics.Debug.WriteLine($"Extracted new code: {newCode}");

                    return newCode;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting code: {ex.Message}");
                    return string.Empty;
                }
            }

            private string RemoveCodeBlocks(string text)
            {
                // Pattern to match markdown code blocks (including language specifier)
                var codeBlockPattern = @"```([^\n]*)\n([\s\S]*?)```";

                // Find the first code block match in the text
                var match = Regex.Match(text, codeBlockPattern);
                if (match.Success && match.Groups.Count >= 3)
                {
                    // match.Groups[1] contains the language (e.g., "c++")
                    // match.Groups[2] contains the code content inside the block
                    var codeContent = match.Groups[2].Value;

                    // Return the code content, trimming any leading/trailing whitespace
                    return codeContent.Trim();
                }

                // If no code block is found, return the original text
                return text;
            }

            private string RemoveOriginalCode(string response, string originalCode)
            {
                if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(originalCode))
                    return response ?? string.Empty;

                // Find the maximum possible overlap length
                int maxOverlap = Math.Min(originalCode.Length, response.Length);

                // Search for the longest overlap starting from the end of originalCode
                for (int overlapLength = maxOverlap; overlapLength > 0; overlapLength--)
                {
                    string originalSuffix = originalCode.Substring(originalCode.Length - overlapLength);
                    string responsePrefix = response.Substring(0, overlapLength);

                    if (originalSuffix == responsePrefix)
                    {
                        // Overlap found, remove the matching part from response
                        return response.Substring(overlapLength);
                    }
                }

                // No overlap found, return the original response
                return response;
            }

        }
    }
}