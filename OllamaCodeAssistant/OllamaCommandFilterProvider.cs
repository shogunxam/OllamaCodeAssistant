using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

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
                System.Diagnostics.Debug.WriteLine("Impossibile ottenere WpfTextView");
                return;
            }

            AddCommandFilter(textViewAdapter, textView);
        }

        private void AddCommandFilter(IVsTextView textViewAdapter, IWpfTextView textView)
        {
            try
            {
                // Ottieni il command target corrente
                IOleCommandTarget next;
                int hr = textViewAdapter.AddCommandFilter(null, out next);
                if (ErrorHandler.Failed(hr))
                {
                    System.Diagnostics.Debug.WriteLine($"Errore nell'ottenere il command target: {hr}");
                    return;
                }

                // Crea il nostro command filter
                var commandFilter = new OllamaCommandFilter(textView, next);

                // Aggiungi il nostro filter alla catena
                hr = textViewAdapter.AddCommandFilter(commandFilter, out next);
                if (ErrorHandler.Succeeded(hr))
                {
                    // Salva il command filter nelle proprietà della text view per poterlo recuperare dopo
                    textView.Properties.AddProperty(typeof(OllamaCommandFilter), commandFilter);
                    System.Diagnostics.Debug.WriteLine("OllamaCommandFilter aggiunto con successo");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Errore nell'aggiungere il command filter: {hr}");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Eccezione nell'aggiungere il command filter: {ex.Message}");
            }
        }
    }
}
