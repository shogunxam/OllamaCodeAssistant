# Ollama Code Assistant

## Overview
Ollama Code Assistant is a Visual Studio extension designed to enhance your coding experience by integrating AI capabilities. It allows you to interact with the Ollama API to get assistance with software development tasks, such as code completion, debugging tips, and more.

### Features:
- **Interactive Chat Interface**: Communicate directly with the Ollama AI model through an integrated chat interface.
- **Contextual Code Assistance**: Provide contextual information from your current project to receive relevant AI-generated responses.
- **Customizable Settings**: Configure the extension with different Ollama API URLs and models.

## Requirements
- **Microsoft Visual Studio 2022 Community or later** (version 17.0 and above).
- **Microsoft Edge WebView2 Runtime**: Most users have this installed via Windows Update, but if you encounter issues, you can download it from [here](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).

## Installation
Since the extension is not yet available on the Visual Studio Marketplace, you will need to build and install it manually.

### Step 1: Clone the Repository
Clone this repository to your local machine using Git:

```bash
git clone https://github.com/RyanMathewson/OllamaCodeAssistant.git
```

### Step 2: Open in Visual Studio
Open the solution file `OllamaCodeAssistant.sln` in Microsoft Visual Studio.

### Step 3: Build the Extension
Build the solution to compile the extension. You can do this by selecting `Build > Build Solution` from the menu or pressing `Ctrl + Shift + B`.

### Step 4: Deploy the Extension
1. Select `Debug > Start Debugging` (or press `F5`) to build and launch a new instance of Visual Studio with the extension installed.
2. Alternatively, you can manually install the `.vsix` file generated in the `bin\Debug` directory:
   - Navigate to the `OllamaCodeAssistant\bin\Debug` folder.
   - Locate the `OllamaCodeAssistant.vsix` file.
   - Double-click the `.vsix` file to open the Visual Studio Installer.
   - Follow the prompts to install the extension.

## Usage
1. **Open Visual Studio** and load your project.
2. Navigate to the `View` menu, then select `Ollama Code Assistant` to open the chat interface.
3. Enter your requests or questions in the chat input box.
4. Use the checkboxes to include context from your current selection, file, or all open files when sending prompts.

## Configuration
You can configure the extension settings by navigating to `Tools > Options` in Visual Studio:
- **Ollama API URL**: Specify the URL of the Ollama API you want to use.
- **Model**: Select or enter the model you wish to use for generating responses.
- Click `Refresh List` to update the list of available models if needed.