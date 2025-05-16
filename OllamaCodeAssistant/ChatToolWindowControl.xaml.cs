using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using EnvDTE80;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using OllamaCodeAssistant.Options;

namespace OllamaCodeAssistant {

  public partial class ChatToolWindowControl : UserControl {
    private const int MaxCodeLength = 1500;

    private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();

    private IChatClient _chatClient;
    private string _ollamaApiUrl;
    private string _model;
    private CancellationTokenSource _cancellationTokenSource;

    private readonly ChatToolWindow _chatToolWindow;

    private OllamaCodeAssistantPackage Package => _chatToolWindow?.Package as OllamaCodeAssistantPackage;

    public ChatToolWindowControl(ChatToolWindow chatToolWindow) {
      _chatToolWindow = chatToolWindow;
      InitializeComponent();
      ClearError();
    }

    private void DisplayError(string message) {
      Dispatcher.BeginInvoke((Action)(() => {
        ErrorDisplayBorder.Visibility = Visibility.Visible;
        ErrorDisplayTextBlock.Text = message;
      }));
    }

    private void ClearError() {
      Dispatcher.BeginInvoke((Action)(() => {
        ErrorDisplayTextBlock.Text = string.Empty;
        ErrorDisplayBorder.Visibility = Visibility.Collapsed;
      }));
    }

    private async void SendButtonClicked(object sender, RoutedEventArgs e) {
      ClearError();

      if (_cancellationTokenSource != null) {
        Debug.WriteLine("Cancellation requested.");
        _cancellationTokenSource.Cancel();
        return;
      }

      var userPrompt = UserInputTextBox.Text;
      if (string.IsNullOrWhiteSpace(userPrompt)) {
        return;
      }

      // Update the UI to indicate processing
      UserInputTextBox.Clear();
      SendButton.Content = "Stop";

      try {
        // Display user message
        Debug.WriteLine($"User: {userPrompt}");
        AppendMessageToUI($"\n\nYou: {userPrompt}");
        AppendMessageToUI($"\n\nAssistant: ");

        // Substitute in the active document
        if (userPrompt.Contains("@doc")) {
          userPrompt = userPrompt.Replace("@doc", GetActiveDocumentText());
        }

        // Add context to the prompt
        userPrompt = BuildPrompt(userPrompt);

        // Add our new message to the chat history
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        // Read the streaming result from the chat client and update the UI
        var fullResponse = new StringBuilder();
        Debug.WriteLine($"Final Prompt: {userPrompt}");

        var options = Package?.GetDialogPage(typeof(OllamaOptionsPage)) as OllamaOptionsPage ?? throw new ApplicationException("Unable to load settings");
        string url = options.OllamaApiUrl;
        string model = options.DefaultModel;

        if (url != _ollamaApiUrl || _model != model) {
          // Settings changed, so reinitialize the chat client
          _ollamaApiUrl = url;
          _model = model;

          _chatClient?.Dispose();
          _chatClient = new OllamaChatClient(new Uri(url), model);
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationTokenSource.Token);
        try {
          while (await enumerator.MoveNextAsync()) {
            if (_cancellationTokenSource.Token.IsCancellationRequested) {
              AppendMessageToUI("\n\nQuery Canceled");
              break;
            }

            var response = enumerator.Current;
            if (response == null) {
              AppendMessageToUI("\n\nResponse Was Empty");
              break;
            }

            fullResponse.Append(response.Text);

            AppendMessageToUI(response.Text);
          }
        } catch (OperationCanceledException) {
          AppendMessageToUI("\n\nQuery Canceled");
        } finally {
          await enumerator.DisposeAsync();
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        DisplayError(ex.Message);
      } finally {
        // Restore UI for next prompt
        UserInputTextBox.IsEnabled = true;
        UserInputTextBox.Focus();
        SendButton.Content = "Send";
        _cancellationTokenSource = null;
      }
    }

    private void AppendMessageToUI(string message) {
      Dispatcher.BeginInvoke((Action)(() => {
        MarkdownWebView.CoreWebView2.PostWebMessageAsString(message);
      }));
    }

    private string GetActiveDocumentText() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
      Document activeDoc = dte?.ActiveDocument;
      TextDocument textDoc = activeDoc?.Object("TextDocument") as TextDocument;
      EditPoint start = textDoc?.StartPoint.CreateEditPoint();
      return start?.GetText(textDoc.EndPoint);
    }

    private string GetSelectedText() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
      if (dte?.ActiveDocument?.Selection is TextSelection selection) {
        string text = selection.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
      }
      return null;
    }

    private string GetTruncatedSelectedText() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
      if (dte?.ActiveDocument?.Selection is TextSelection selection) {
        string text = selection.Text;
        if (string.IsNullOrWhiteSpace(text))
          return null;

        if (text.Length <= MaxCodeLength)
          return text;

        // Truncate cleanly at line breaks
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int totalLength = 0;
        List<string> truncatedLines = new List<string>();

        foreach (var line in lines) {
          if (totalLength + line.Length > MaxCodeLength)
            break;

          truncatedLines.Add(line);
          totalLength += line.Length + 1; // Include newline
        }

        truncatedLines.Add("// [Truncated due to length limits]");
        return string.Join(Environment.NewLine, truncatedLines);
      }

      return null;
    }

    private string GetLanguageFromFile() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
      string fileName = dte?.ActiveDocument?.Name;

      if (fileName == null) return null;

      string ext = Path.GetExtension(fileName).ToLowerInvariant();

      switch (ext) {
        case ".cs": return "C#";
        case ".ts": return "TypeScript";
        case ".js": return "JavaScript";
        case ".cpp": return "C++";
        case ".h": return "C++ header";
        case ".py": return "Python";
        case ".html": return "HTML";
        case ".css": return "CSS";
        case ".json": return "JSON";
        default: return null;
      }
    }

    private string BuildPrompt(string userInput) {
      string code;

      if (ContextIncludeSelection.IsChecked == true) {
        code = GetTruncatedSelectedText();
      } else if (ContextIncludeFile.IsChecked == true) {
        code = GetActiveDocumentText();
      } else {
        return userInput;
      }

      string language = GetLanguageFromFile();
      string langHint = language != null ? $"The following is a {language} code snippet:\n" : "The following is a code snippet:\n";

      // Handle some common cases
      string normalized = userInput.Trim().ToLowerInvariant();
      if (normalized == "explain" || normalized.StartsWith("explain this")) {
        return $"Please explain what this code does.\n\n{langHint}```\n{code}\n```";
      }
      if (normalized.StartsWith("refactor")) {
        return $"Refactor this code to be cleaner or more efficient.\n\n{langHint}```\n{code}\n```";
      }
      if (normalized.StartsWith("add comments") || normalized.Contains("document")) {
        return $"Add inline comments to explain the logic in this code.\n\n{langHint}```\n{code}\n```";
      }

      // Default case — just include the code below the input
      return $"{userInput}\n\n{langHint}```\n{code}\n```";
    }

    private string LoadHtmlFromResource() {
      var assembly = Assembly.GetExecutingAssembly();
      var resourceName = "OllamaCodeAssistant.Resources.ChatView.html";

      using (var stream = assembly.GetManifestResourceStream(resourceName))
      using (var reader = new StreamReader(stream)) {
        return reader.ReadToEnd();
      }
    }

    private async void MyToolWindow_Loaded(object sender, RoutedEventArgs e) {
      Loaded -= MyToolWindow_Loaded; // Unsubscribe from the event to prevent multiple calls
      try {
        await InitializeWebViewAsync(MarkdownWebView);
      } catch (Exception ex) {
        DisplayError($"Error loading WebView2: {ex.Message}");
      }

      TextSelectionListener.SelectionChanged += TextViewTrackerSelectionChanged;

      UserInputTextBox.Focus();
    }

    private void TextViewTrackerSelectionChanged(object sender, string e) {
      Dispatcher.BeginInvoke((Action)(() => {
        ContextIncludeSelection.IsChecked = e != null && e.Length > 0;
      }));
    }

    private async Task InitializeWebViewAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView) {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

      string userDataFolder = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "OllamaCodeAssistant", "WebView2UserData");

      var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

      await webView.EnsureCoreWebView2Async(env);

      webView.CoreWebView2InitializationCompleted += (s, e) => {
        if (!e.IsSuccess) {
          DisplayError($"WebView2 init failed: {e.InitializationException.Message}");
        }
      };

      webView.NavigateToString(LoadHtmlFromResource());
    }
  }
}