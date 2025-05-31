using System.ComponentModel.Composition;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace OllamaCodeAssistant
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class OllamaCommandFilterProvider : IVsTextViewCreationListener
    {
        [Import(typeof(IVsEditorAdaptersFactoryService))]
        internal IVsEditorAdaptersFactoryService AdapterService = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            System.Diagnostics.Debug.WriteLine("OllamaCommandFilterProvider: VsTextViewCreated");

            IWpfTextView textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null)
            {
                System.Diagnostics.Debug.WriteLine("Unable to get WpfTextView");
                return;
            }

            AddCommandFilter(textViewAdapter, textView);
        }

        private void AddCommandFilter(IVsTextView textViewAdapter, IWpfTextView textView)
        {
            try
            {
                // Get the current command target
                IOleCommandTarget next;
                int hr = textViewAdapter.AddCommandFilter(null, out next);
                if (ErrorHandler.Failed(hr))
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting command target: {hr}");
                    return;
                }

                // Create our command filter
                var commandFilter = new OllamaCommandFilter(textView, next);

                // Add our filter to the chain
                hr = textViewAdapter.AddCommandFilter(commandFilter, out next);
                if (ErrorHandler.Succeeded(hr))
                {
                    // Save the command filter in the text view's properties for later retrieval
                    textView.Properties.AddProperty(typeof(OllamaCommandFilter), commandFilter);
                    System.Diagnostics.Debug.WriteLine("OllamaCommandFilter successfully added");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Error adding command filter: {hr}");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception while adding command filter: {ex.Message}");
            }
        }
    }
}