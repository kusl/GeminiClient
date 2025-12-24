# 🤖 Gemini Client Console

A powerful, interactive command-line client for Google's Gemini AI API with **real-time streaming**, model selection, performance metrics, and session statistics.

![GitHub release (latest by date)](https://img.shields.io/github/v/release/yourusername/GeminiClient)
![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/yourusername/GeminiClient/build-and-release.yml)
![Platform Support](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![License](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue)

## 🔑 Quick Start - API Key Required!

> **⚠️ IMPORTANT: You need a Google Gemini API key to use this application!**

### Getting Your API Key

1. **Get a FREE API key** from Google AI Studio: [https://aistudio.google.com/apikey](https://aistudio.google.com/apikey)
2. Click "Get API Key" and follow the instructions
3. Copy your API key (starts with `AIza...`)

### Setting Your API Key (3 Methods)

#### Method 1: Configuration File (Recommended)
Create an `appsettings.json` file in the same directory as the executable:

```json
{
  "GeminiSettings": {
    "ApiKey": "YOUR_API_KEY_HERE",
    "BaseUrl": "https://generativelanguage.googleapis.com/",
    "DefaultModel": "gemini-2.5-flash",
    "StreamingEnabled": true
  }
}
```

#### Method 2: Environment Variable
```bash
# Linux/macOS
export GeminiSettings__ApiKey="YOUR_API_KEY_HERE"

# Windows Command Prompt
set GeminiSettings__ApiKey=YOUR_API_KEY_HERE

# Windows PowerShell
$env:GeminiSettings__ApiKey="YOUR_API_KEY_HERE"
```

#### Method 3: User Secrets (Development)
```bash
dotnet user-secrets set "GeminiSettings:ApiKey" "YOUR_API_KEY_HERE"
```

> **🔒 Security Note**: Never commit your API key to version control! The `appsettings.json` file is gitignored by default.

## 📥 Installation

### Download Pre-built Binaries

Download the latest release for your platform from the [Releases page](https://github.com/yourusername/GeminiClient/releases).

| Platform | Download | Architecture |
|----------|----------|--------------|
| **Windows** | `gemini-client-win-x64.zip` | 64-bit Intel/AMD |
| | `gemini-client-win-x86.zip` | 32-bit Intel/AMD |
| | `gemini-client-win-arm64.zip` | ARM64 |
| **Linux** | `gemini-client-linux-x64.tar.gz` | 64-bit Intel/AMD |
| | `gemini-client-linux-arm64.tar.gz` | ARM64 (Raspberry Pi 4+) |
| | `gemini-client-linux-musl-x64.tar.gz` | Alpine Linux |
| **macOS** | `gemini-client-osx-x64.tar.gz` | Intel Macs |
| | `gemini-client-osx-arm64.tar.gz` | Apple Silicon (M1/M2/M3) |

### Running the Application

#### Windows
```powershell
# Extract the ZIP file
# Double-click gemini-client-win-x64.exe
# OR run from command line:
.\gemini-client-win-x64.exe
```

#### Linux/macOS
```bash
# Extract the archive
tar -xzf gemini-client-linux-x64.tar.gz

# Make executable
chmod +x gemini-client-linux-x64

# Run
./gemini-client-linux-x64
```

## 🚀 Features

### 🌊 Real-time Streaming Responses ✨ NEW!
- **Live Text Generation**: See responses appear character by character as they're generated
- **Server-Sent Events (SSE)**: True real-time streaming from Gemini API
- **Streaming Toggle**: Switch between streaming and batch modes with the `stream` command
- **First Response Timing**: See exactly when the first chunk arrives (typically 200-500ms)
- **Real-time Performance**: Monitor streaming speed and throughput as it happens
- **Memory Efficient**: Yield-based processing handles large responses without memory spikes

### Interactive Model Selection
- **Dynamic Model Discovery**: Automatically fetches all available Gemini models with animated loading
- **Smart Recommendations**: Suggests optimal models based on your needs
- **Model Categories**:
  - ⚡ **Flash Models**: Fast, cost-effective for most tasks
  - 💎 **Pro Models**: Advanced capabilities for complex tasks
  - 🚀 **Ultra Models**: Maximum performance (when available)
  - 🧪 **Experimental Models**: Cutting-edge features in testing
- **Timeout Support**: Automatic default selection after 5 minutes of inactivity
- **Animated UI**: Smooth loading animations and character-by-character confirmations

### Advanced Performance Metrics
- **Response Time Tracking**: See exactly how long each request takes
- **Token Speed Analysis**: Monitors tokens/second throughput in real-time
- **Visual Speed Indicators**:
  - 🐌 Slow (< 10 tokens/s)
  - 🚶 Normal (10-30 tokens/s)
  - 🏃 Fast (30-50 tokens/s)
  - 🚀 Very Fast (50-100 tokens/s)
  - ⚡ Lightning (100+ tokens/s)
- **Streaming vs Batch Comparison**: Compare performance between modes
- **Session Averages**: Track improvements over time

### Comprehensive Session Statistics
- Track all requests in your session
- View average response times across models
- Compare streaming vs non-streaming performance
- See total tokens and characters processed
- Model usage breakdown with performance metrics

### Smart Error Handling
- Automatic fallback to stable models
- Clear error messages with suggested fixes
- Graceful handling of API limits and server issues
- Streaming connection recovery and retry logic

## 💻 Usage

### Available Commands

| Command | Description |
|---------|-------------|
| `exit` | Quit the application |
| `model` | Change the selected AI model |
| `stats` | View detailed session statistics |
| `stream` | Toggle streaming mode ON/OFF |

### Example Streaming Session

```
🤖 Available Gemini Models:
═══════════════════════════
⠋ Checking model availability...

  [1] ⚡ gemini-2.5-flash - Latest Gemini 2.5 Flash - Fast and efficient
  [2] 💎 gemini-2.0-flash-exp - Experimental Gemini 2.0 Flash - Cutting edge features
  [3] 🚀 gemini-2.0-flash - Gemini 2.0 Flash - Balanced performance

Select a model (1-3) or press Enter for default [gemini-2.5-flash]:
> [Press Enter]

✓ Selected: g e m i n i - 2 . 5 - f l a s h
🎉 Ready to go!

📝 Enter prompt ('exit' to quit, 'model' to change model, 'stats' for session stats, 'stream' to toggle streaming: ON):
> Explain quantum computing in simple terms

╭─── Streaming Response ───╮
⚡ First response: 247ms

Quantum computing is like having a magical computer that can try many solutions 
at once instead of one at a time. Imagine you're trying to solve a massive maze...

[Text continues to appear in real-time as it's generated]

...This makes them incredibly powerful for certain types of problems!
╰────────────────╯

📊 Streaming Performance Metrics:
   └─ Total Time: 2.34s
   └─ Words: 127 | Characters: 823
   └─ Est. Tokens: ~206 | Speed: 88.0 tokens/s [████████░░] 🚀
   └─ Mode: 🌊 Streaming (real-time)

📝 Enter prompt ('exit' to quit, 'model' to change model, 'stats' for session stats, 'stream' to toggle streaming: ON):
> stream

✓ Streaming disabled

📝 Enter prompt ('exit' to quit, 'model' to change model, 'stats' for session stats, 'stream' to toggle streaming: OFF):
> What is machine learning?

⠙ Generating response... [00:01.89]

╭─── Response ─── ⏱ 1.89s ───╮
Machine learning is a branch of artificial intelligence (AI) that enables 
computers to learn and improve from experience without being explicitly 
programmed for every task...
╰────────────────╯

📊 Performance Metrics:
   └─ Response Time: 1.89s
   └─ Words: 95 | Characters: 634
   └─ Est. Tokens: ~158 | Speed: 83.6 tokens/s [████████░░] 🚀
   └─ Session Avg: 2.12s (🟢 faster)

📝 Enter prompt ('exit' to quit, 'model' to change model, 'stats' for session stats, 'stream' to toggle streaming: OFF):
> stats

╔═══ Session Statistics ═══╗
  📊 Total Requests: 2
  ⏱  Average Response: 2.12s
  🚀 Fastest: 1.89s
  🐌 Slowest: 2.34s
  📝 Total Output: 1,457 characters
  ⏰ Session Duration: 2m 34s
  🌊 Streaming: Disabled

  🤖 Models Used:
     └─ gemini-2.5-flash: 2 requests (avg 2.12s)
╚════════════════════════╝
```

## ⚙️ Configuration

### Full Configuration Options

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "GeminiClient": "Information",
      "GeminiClientConsole": "Information"
    }
  },
  "GeminiSettings": {
    "ApiKey": "YOUR_API_KEY_HERE",
    "BaseUrl": "https://generativelanguage.googleapis.com/",
    "DefaultModel": "gemini-2.5-flash",
    "ModelPreference": "Fastest",
    "TimeoutSeconds": 30,
    "MaxRetries": 3,
    "EnableDetailedLogging": false,
    
    // Streaming Configuration
    "StreamingEnabled": true,
    "StreamingTimeout": 300,
    "StreamingChunkDelay": 50,
    "StreamingRetryAttempts": 3
  }
}
```

### Configuration Priority

The application loads configuration in this order (later sources override earlier ones):
1. Default values
2. `appsettings.json` file
3. User secrets (development only)
4. Environment variables
5. Command line arguments (if applicable)

### Model Preferences

Set `ModelPreference` to control automatic model selection:
- `"Fastest"` - Prefers Flash models for quick responses
- `"MostCapable"` - Prefers Pro/Ultra models for complex tasks
- `"Balanced"` - Balances speed and capability

### Streaming Options

- `StreamingEnabled` - Enable streaming by default (can be toggled at runtime)
- `StreamingTimeout` - Seconds to wait for streaming response chunks
- `StreamingChunkDelay` - Milliseconds between chunk processing (for visual effect)
- `StreamingRetryAttempts` - Number of retry attempts for failed streaming connections

## 🛠️ Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Build Steps

```bash
# Clone the repository
git clone https://github.com/yourusername/GeminiClient.git
cd GeminiClient

# Restore dependencies
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run --project GeminiClientConsole
```

### Creating a Release Build

```bash
# Windows PowerShell
./build-release.ps1 -Version 0.0.6

# Linux/macOS
chmod +x build-release.sh
./build-release.sh 0.0.6
```

## 📦 Project Structure

```
GeminiClient/
├── GeminiClient/                 # Core library
│   ├── GeminiApiClient.cs       # Main API client with streaming support
│   ├── IGeminiApiClient.cs      # Client interface (sync + async streaming)
│   └── Models/                  # Data models and JSON serialization
├── GeminiClientConsole/          # Console application
│   ├── Program.cs               # Entry point with DI setup
│   ├── AppRunner.cs             # Main application logic with streaming UI
│   └── ConsoleModelSelector.cs  # Interactive model selection with animations
├── .github/workflows/            # CI/CD pipelines
│   ├── build-and-release.yml   # Release automation
│   └── ci.yml                   # Continuous integration
└── README.md                     # This file
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Development Setup

```bash
# Clone your fork
git clone https://github.com/yourusername/GeminiClient.git
cd GeminiClient

# Create a new branch
git checkout -b feature/your-feature

# Set up user secrets for development
dotnet user-secrets set "GeminiSettings:ApiKey" "YOUR_API_KEY"

# Run tests
dotnet test

# Run the application
dotnet run --project GeminiClientConsole
```

## 🐛 Troubleshooting

### Common Issues

#### "API Key not configured"
- Make sure you've set your API key using one of the three methods above
- Check that your `appsettings.json` is in the same directory as the executable
- Verify environment variables are set correctly

#### Streaming Issues
- **Connection timeouts**: Increase `StreamingTimeout` in configuration
- **Slow streaming**: Check your internet connection; try switching to batch mode temporarily
- **Choppy text**: Adjust `StreamingChunkDelay` for smoother visual flow
- **SSE format errors**: Some models may not support streaming; switch to a supported model

#### "500 Internal Server Error"
- Some experimental models may be unstable
- Switch to a stable model like `gemini-2.5-flash`
- Try disabling streaming mode with the `stream` command
- Check [Google's status page](https://status.cloud.google.com/) for outages

#### "Rate limit exceeded"
- Free tier has usage limits
- Wait a few minutes and try again
- Consider upgrading your API plan
- Streaming mode may consume quota faster due to persistent connections

#### Application won't start on macOS
```bash
# Remove quarantine attribute
xattr -d com.apple.quarantine ./gemini-client-osx-arm64

# Make executable
chmod +x ./gemini-client-osx-arm64
```

#### Application won't start on Linux
```bash
# Check if executable permission is set
chmod +x ./gemini-client-linux-x64

# If using Alpine Linux, use the musl version
./gemini-client-linux-musl-x64
```

## 📊 Performance

### Binary Sizes (Approximate)

| Platform | Size | Notes |
|----------|------|-------|
| Windows x64 | ~37 MB | Self-contained, trimmed, includes SSE streaming |
| Linux x64 | ~40 MB | Self-contained, trimmed, includes SSE streaming |
| macOS ARM64 | ~38 MB | Self-contained, trimmed, includes SSE streaming |

### System Requirements

- **Memory**: 128 MB RAM minimum, 256 MB recommended for streaming
- **Disk Space**: 50 MB for application
- **Network**: Stable internet connection required (persistent for streaming)
- **.NET Runtime**: Not required (self-contained)

### Streaming Performance

- **First Response**: Typically 200-500ms for Flash models
- **Throughput**: 50-200+ tokens/second depending on model and network
- **Memory Usage**: Constant ~50MB regardless of response length
- **Connection**: Single persistent HTTP/2 connection per streaming session

## 📝 API Usage and Limits

### Free Tier Limits (as of 2025)

- **Requests**: 60 requests per minute
- **Daily Tokens**: Varies by model
- **Rate Limits**: Automatically handled with retry logic
- **Streaming**: May consume quota faster due to persistent connections

### Tips for Optimal Usage

1. **Use Flash models for most tasks** - They're fast, cost-effective, and fully support streaming
2. **Toggle streaming as needed** - Use batch mode for very long responses to conserve bandwidth
3. **Switch to Pro models for complex reasoning** - When you need advanced capabilities
4. **Monitor your usage** - Check your [Google AI Studio dashboard](https://aistudio.google.com/)
5. **Use session stats** - Track your performance patterns and optimize accordingly

## 📜 License

This project is licensed under the GNU Affero General Public License v3.0 or later (AGPL-3.0-or-later) - see the [LICENSE](LICENSE) file for details.

### What this means:

- ✅ **You can**: Use, modify, distribute, and use commercially
- ⚠️ **You must**: Disclose source, include license and copyright notice, state changes, and use the same license
- 🌐 **Network use**: If you run a modified version on a server, you must provide source code to users of that server
- 🚫 **You cannot**: Hold the authors liable or remove the license terms

For more information, see the [full AGPL-3.0 license text](https://www.gnu.org/licenses/agpl-3.0.en.html).

## 🙏 Acknowledgments

- Google for the Gemini AI API and Server-Sent Events support
- The .NET team for the excellent framework and async/await patterns
- All contributors and users of this project

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/GeminiClient/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/GeminiClient/discussions)
- **API Documentation**: [Google AI Documentation](https://ai.google.dev/gemini-api/docs)
- **Streaming API**: [Server-Sent Events Documentation](https://ai.google.dev/gemini-api/docs/text-generation#streaming)

## 🗺️ Roadmap

### Recently Completed ✅
- ~~Add support for streaming responses~~ ✅ **COMPLETED v0.0.6** - Full SSE streaming with real-time display
- ~~Enhanced model selection~~ ✅ **COMPLETED v0.0.6** - Animated, async model selection with validation

### Upcoming Features
- [ ] Add support for image inputs
- [ ] Implement conversation history with streaming support
- [ ] Add export functionality for responses (including streaming sessions)
- [ ] Create a web UI version with WebSocket streaming
- [ ] Implement prompt templates with streaming preview
- [ ] Add batch processing mode for multiple prompts
- [ ] Add support for function calling with streaming
- [ ] Implement custom system prompts
- [ ] Add response caching and deduplication

### Future Enhancements
- [ ] Multi-modal support (images, audio, video)
- [ ] Plugin architecture for extensibility
- [ ] Advanced streaming controls (pause/resume/speed)
- [ ] Integration with popular development tools
- [ ] Cloud deployment templates

---

<div align="center">

Made with ❤️ using .NET 9, Google Gemini AI, and Server-Sent Events

⭐ **Star this repo if you find it useful!**

🌊 **Try the new streaming mode for a magical AI experience!**

</div>

---

## 🔄 Version History

- **v0.0.6** (Latest) - Added real-time streaming support with SSE, enhanced model selection with animations, improved performance metrics
- **v0.0.5** - Improved terminal compatibility, removed Console.Clear() that was destroying scrollback buffer
- **v0.0.4** - Interactive console client with dynamic model discovery, performance metrics, session statistics, cross-platform support
- **v0.0.3** - Clean up compiler warnings
- **v0.0.2** - Remove errant character 'W' from code
- **v0.0.1** - Properly configure trimming for JSON serialization
- **v0.0.0** - 🎉 Initial commit with basic project structure

---

*Notice: This project contains code generated by Large Language Models such as Claude and Gemini. All code is experimental whether explicitly stated or not. The streaming implementation uses Server-Sent Events (SSE) for real-time communication with the Gemini API.*