using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace OllamaCodeAssistant {

  internal sealed class ExecuteExplainErrorCommand {

    /// <summary>
    /// Gets the instance of the command.
    /// </summary>
    public static ExecuteExplainErrorCommand Instance {
      get;
      private set;
    }

    /// <summary>
    /// Command ID.
    /// </summary>
    public const int CommandId = 0x1021;

    /// <summary>
    /// Command menu group (command set GUID).
    /// </summary>
    public static readonly Guid CommandSet = new Guid("d266ffd7-3e8a-4a56-82fa-d0b63b3471e4");

    private readonly LLMInteractionManager _llmInteractionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecuteExplainErrorCommand"/> class.
    /// Adds our command handlers for menu (commands must exist in the command table file)
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    /// <param name="commandService">Command service to add command to, not null.</param>
    private ExecuteExplainErrorCommand(AsyncPackage package, OleMenuCommandService commandService, LLMInteractionManager llmInteractionManager) {
      commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

      var menuCommandID = new CommandID(CommandSet, CommandId);
      var menuItem = new MenuCommand(this.Execute, menuCommandID);
      commandService.AddCommand(menuItem);

      _llmInteractionManager = llmInteractionManager;
    }

    /// <summary>
    /// Initializes the singleton instance of the command.
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    public static async Task InitializeAsync(AsyncPackage package, LLMInteractionManager llmInteractionManager) {
      // Switch to the main thread - the call to AddCommand in ChatToolWindowCommand's constructor requires
      // the UI thread.
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

      OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
      Instance = new ExecuteExplainErrorCommand(package, commandService, llmInteractionManager);
    }

    private void Execute(object sender, EventArgs e) {
      var error = DETService.GetSelectedError();
      if (error == null) {
        return;
      }

      Debug.WriteLine($"Description: {error.Description}");
      Debug.WriteLine($"FileName: {error.FileName}");
      Debug.WriteLine($"Line: {error.Line}");
      Debug.WriteLine($"Column: {error.Column}");
      Debug.WriteLine($"Project: {error.Project}");
      Debug.WriteLine($"ErrorLevel: {error.ErrorLevel}");

      var fullPrompt = PromptManager.BuildPrompt(error);

      // Example of using LLMInteractionManager
      _llmInteractionManager.HandleUserMessageAsync($"Help me troubleshoot this error: {error.Description}", fullPrompt);
    }
  }
}