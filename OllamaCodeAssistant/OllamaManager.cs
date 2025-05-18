using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OllamaCodeAssistant {

  public static class OllamaManager {

    public static async Task<List<string>> GetAvailableModelsAsync(string url) {
      using (var client = new HttpClient()) {
        var response = await client.GetStringAsync($"{url.TrimEnd('/')}/api/tags");
        using (var doc = JsonDocument.Parse(response)) {
          var modelNames = new List<string>();
          foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray()) {
            modelNames.Add(model.GetProperty("name").GetString());
          }
          return modelNames;
        }
      }
    }
  }
}