this build-release looks like a windows only powershell script. can you write me a bash version for fedora linux? or even better, take a look at dump.txt and see if we can remove this step altogether and create a new release (not pre-release) but actually auto incrementing release on every push to github? 

This is a great idea. Moving the build logic entirely to GitHub Actions is the modern "DevOps" way to handle this. It eliminates the "works on my machine" issues and ensures that every bit of code you commit is immediately built, tested, and ready for deployment.

Since you are on Fedora now, I will provide two things:

1. **The "Better" Solution:** A complete GitHub Actions workflow that automatically builds, versions, and releases on **every push**.
2. **The Local Fallback:** A bash script (`build.sh`) for when you want to build locally on Fedora without pushing to GitHub.

### 1. The Automated GitHub Workflow

This workflow replaces your old `build-and-release.yml`. It uses the build number to automatically increment the version (e.g., `0.0.7.45` where `45` is the GitHub run number), ensuring every push gets a unique, increasing release version.

**Action:** Delete `.github/workflows/build-and-release.yml` and create `.github/workflows/continuous-release.yml` with this content:

```yaml
name: Continuous Release

# Trigger on every push to main
on:
  push:
    branches: [ "main" ]
  workflow_dispatch: # Allows manual trigger if needed

permissions:
  contents: write # Required to create releases and tags

env:
  DOTNET_VERSION: '10.0.x'
  PROJECT_PATH: 'GeminiClientConsole/GeminiClientConsole.csproj'

jobs:
  build-and-release:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      with:
        fetch-depth: 0

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    # Get the base version from Directory.Build.props (e.g., 0.0.7)
    # And append the GitHub Run Number (e.g., 0.0.7.42)
    - name: Generate Version
      id: versioning
      run: |
        BASE_VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
        NEW_VERSION="${BASE_VERSION}.${{ github.run_number }}"
        echo "Calculated Version: $NEW_VERSION"
        echo "version=$NEW_VERSION" >> $GITHUB_OUTPUT

    - name: Restore dependencies
      run: dotnet restore ${{ env.PROJECT_PATH }}

    # Build for Linux (Fedora/Ubuntu compatible)
    - name: Build Linux x64
      run: |
        dotnet publish ${{ env.PROJECT_PATH }} \
          --configuration Release \
          --runtime linux-x64 \
          --self-contained true \
          --output ./publish/linux-x64 \
          -p:PublishSingleFile=true \
          -p:PublishTrimmed=true \
          -p:Version=${{ steps.versioning.outputs.version }}

        # Package
        cd ./publish/linux-x64
        chmod +x GeminiClientConsole
        mv GeminiClientConsole gemini-client-linux-x64
        tar -czf ../../gemini-client-linux-x64.tar.gz gemini-client-linux-x64
        cd ../..

    # Build for Windows
    - name: Build Windows x64
      run: |
        dotnet publish ${{ env.PROJECT_PATH }} \
          --configuration Release \
          --runtime win-x64 \
          --self-contained true \
          --output ./publish/win-x64 \
          -p:PublishSingleFile=true \
          -p:PublishTrimmed=true \
          -p:Version=${{ steps.versioning.outputs.version }}

        # Package
        cd ./publish/win-x64
        mv GeminiClientConsole.exe gemini-client-win-x64.exe
        zip -r ../../gemini-client-win-x64.zip gemini-client-win-x64.exe
        cd ../..

    # Create the Release on GitHub
    - name: Create Release
      uses: softprops/action-gh-release@v2
      with:
        tag_name: v${{ steps.versioning.outputs.version }}
        name: Release v${{ steps.versioning.outputs.version }}
        draft: false
        prerelease: false
        generate_release_notes: true
        files: |
          gemini-client-linux-x64.tar.gz
          gemini-client-win-x64.zip

```

### 2. The Local Bash Script (`build.sh`)

Since you are on Fedora, you may occasionally want to test the build locally before pushing. This script replicates the logic of your old PowerShell script but is native to Linux.

**Action:** Create `build.sh` in the root of your repo:

```bash
#!/bin/bash
set -e # Exit immediately if a command fails

# Default arguments
CONFIGURATION="Release"
SKIP_TESTS=false

# Auto-detect version from Directory.Build.props
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
echo -e "\e[36mGemini Client Build Script v$VERSION\e[0m"

# Cleanup
echo -e "\e[33mCleaning previous builds...\e[0m"
rm -rf ./publish ./artifacts
mkdir -p ./artifacts

# Restore
echo -e "\e[33mRestoring dependencies...\e[0m"
dotnet restore

# Test
if [ "$SKIP_TESTS" = false ]; then
    echo -e "\e[33mRunning tests...\e[0m"
    dotnet test --configuration $CONFIGURATION --verbosity minimal
fi

# Build Function
build_platform() {
    RUNTIME=$1
    EXT=$2
    echo -e "\e[32mBuilding for $RUNTIME...\e[0m"
    
    OUTPUT_PATH="./publish/$RUNTIME"
    
    dotnet publish ./GeminiClientConsole/GeminiClientConsole.csproj \
        --configuration $CONFIGURATION \
        --runtime $RUNTIME \
        --self-contained true \
        --output $OUTPUT_PATH \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -p:Version=$VERSION

    # Rename and Archive
    cd $OUTPUT_PATH
    
    if [[ "$RUNTIME" == "win"* ]]; then
        mv GeminiClientConsole.exe "gemini-client-$RUNTIME.exe"
        zip -r "../../artifacts/gemini-client-$RUNTIME-v$VERSION.zip" "gemini-client-$RUNTIME.exe"
    else
        mv GeminiClientConsole "gemini-client-$RUNTIME"
        chmod +x "gemini-client-$RUNTIME"
        tar -czf "../../artifacts/gemini-client-$RUNTIME-v$VERSION.tar.gz" "gemini-client-$RUNTIME"
    fi
    
    cd - > /dev/null
}

# Run Builds
build_platform "linux-x64" ""
# build_platform "win-x64" ".exe" # Uncomment if you want to build windows artifacts locally on Linux

echo -e "\e[32mBuild Complete! Artifacts are in ./artifacts\e[0m"

```

### 3. Next Steps (Execution Plan)

Run these commands in your Fedora terminal to switch over:

1. **Remove the old scripts:**
```bash
rm build-release.ps1
git rm build-release.ps1

```


2. **Add the new local script:**
```bash
# (Copy the build.sh content above into a file named build.sh)
chmod +x build.sh
git add build.sh

```


3. **Update the Workflow:**
```bash
git rm .github/workflows/build-and-release.yml
mkdir -p .github/workflows
# (Create the continuous-release.yml file inside .github/workflows/)
git add .github/workflows/continuous-release.yml

```


4. **Commit and Push:**
```bash
git commit -m "refactor: switch to automated CI/CD release workflow"
git push origin main

```



Once you push, go to the **Actions** tab in your GitHub repository. You will see the "Continuous Release" workflow run, and within a few minutes, a new **Release** will appear on the right side of your repo main page with the Linux and Windows binaries attached.







































































build fails
Run dotnet publish GeminiClientConsole/GeminiClientConsole.csproj \
      Determining projects to restore...
      Restored /home/runner/work/GeminiClient/GeminiClient/GeminiClient/GeminiClient.csproj (in 1.36 sec).
      Restored /home/runner/work/GeminiClient/GeminiClient/GeminiClientConsole/GeminiClientConsole.csproj (in 1.36 sec).
    Warning: /home/runner/work/GeminiClient/GeminiClient/GeminiClient/obj/Release/net10.0/GeminiClient.AssemblyInfo.cs(18,59): warning CS7035: The specified version string '0.0.7.2.0' does not conform to the recommended format - major.minor.build.revision [/home/runner/work/GeminiClient/GeminiClient/GeminiClient/GeminiClient.csproj]    
    Error: /home/runner/work/GeminiClient/GeminiClient/GeminiClient/obj/Release/net10.0/GeminiClient.AssemblyInfo.cs(22,55): error CS7034: The specified version string '0.0.7.2.0' does not conform to the required format - major[.minor[.build[.revision]]] [/home/runner/work/GeminiClient/GeminiClient/GeminiClient/GeminiClient.csproj]
    Error: Process completed with exit code 1.


The build failed because of how the version properties are defined in your `Directory.Build.props` file versus how the CI overrides them.

### The Problem

In your `Directory.Build.props`, you have this logic:

```xml
<Version>0.0.7</Version>
<FileVersion>$(Version).0</FileVersion>
<AssemblyVersion>$(Version).0</AssemblyVersion>

```

When the GitHub Action sets the `Version` to `0.0.7.2` (adding the run number), MSBuild automatically evaluates `FileVersion` as `$(Version).0`, resulting in **`0.0.7.2.0`**.
Windows version numbers strictly allow only 4 parts (`Major.Minor.Build.Revision`), so `0.0.7.2.0` (5 parts) triggers error `CS7034`.

### The Fix

You need to explicitly override `FileVersion` and `AssemblyVersion` in your GitHub Action command so MSBuild doesn't try to append that extra `.0`.

Update your `.github/workflows/continuous-release.yml` file to include these three extra flags in the `dotnet publish` commands:
`-p:FileVersion=${{ steps.versioning.outputs.version }} -p:AssemblyVersion=${{ steps.versioning.outputs.version }}`

Here is the corrected, complete workflow file:

```yaml
name: Continuous Release

on:
  push:
    branches: [ "main" ]
  workflow_dispatch:

permissions:
  contents: write

env:
  DOTNET_VERSION: '9.0.x'
  PROJECT_PATH: 'GeminiClientConsole/GeminiClientConsole.csproj'

jobs:
  build-and-release:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      with:
        fetch-depth: 0

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Generate Version
      id: versioning
      run: |
        # Extract 0.0.7 from <Version>0.0.7</Version>
        BASE_VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
        # Create 0.0.7.45 (where 45 is the run number)
        NEW_VERSION="${BASE_VERSION}.${{ github.run_number }}"
        echo "Calculated Version: $NEW_VERSION"
        echo "version=$NEW_VERSION" >> $GITHUB_OUTPUT

    - name: Restore dependencies
      run: dotnet restore ${{ env.PROJECT_PATH }}

    # Build for Linux
    - name: Build Linux x64
      run: |
        dotnet publish ${{ env.PROJECT_PATH }} \
          --configuration Release \
          --runtime linux-x64 \
          --self-contained true \
          --output ./publish/linux-x64 \
          -p:PublishSingleFile=true \
          -p:PublishTrimmed=true \
          -p:Version=${{ steps.versioning.outputs.version }} \
          -p:FileVersion=${{ steps.versioning.outputs.version }} \
          -p:AssemblyVersion=${{ steps.versioning.outputs.version }}

        # Package
        cd ./publish/linux-x64
        chmod +x GeminiClientConsole
        mv GeminiClientConsole gemini-client-linux-x64
        tar -czf ../../gemini-client-linux-x64.tar.gz gemini-client-linux-x64
        cd ../..

    # Build for Windows
    - name: Build Windows x64
      run: |
        dotnet publish ${{ env.PROJECT_PATH }} \
          --configuration Release \
          --runtime win-x64 \
          --self-contained true \
          --output ./publish/win-x64 \
          -p:PublishSingleFile=true \
          -p:PublishTrimmed=true \
          -p:Version=${{ steps.versioning.outputs.version }} \
          -p:FileVersion=${{ steps.versioning.outputs.version }} \
          -p:AssemblyVersion=${{ steps.versioning.outputs.version }}

        # Package
        cd ./publish/win-x64
        mv GeminiClientConsole.exe gemini-client-win-x64.exe
        zip -r ../../gemini-client-win-x64.zip gemini-client-win-x64.exe
        cd ../..

    - name: Create Release
      uses: softprops/action-gh-release@v2
      with:
        tag_name: v${{ steps.versioning.outputs.version }}
        name: Release v${{ steps.versioning.outputs.version }}
        draft: false
        prerelease: false
        generate_release_notes: true
        files: |
          gemini-client-linux-x64.tar.gz
          gemini-client-win-x64.zip

```




































































dump.txt details a gemini client application built with dotnet. can you please update the following shell script to install this on linux? 

here is the repo information 
kushal@fedora:~/Documents/mail$ cd ~/src/dotnet/; time git clone git@github.com:kusl/GeminiClient.git
Cloning into 'GeminiClient'...
remote: Enumerating objects: 173, done.
remote: Counting objects: 100% (173/173), done.
remote: Compressing objects: 100% (102/102), done.
remote: Total 173 (delta 97), reused 133 (delta 57), pack-reused 0 (from 0)
Receiving objects: 100% (173/173), 141.06 KiB | 3.28 MiB/s, done.
Resolving deltas: 100% (97/97), done.

real    0m0.615s
user    0m0.018s
sys    0m0.025s


#!/bin/bash
set -e # Exit immediately if a command exits with a non-zero status.

# --- Configuration ---
GITHUB_REPO="kusl/GeminiClient"
INSTALL_DIR="/opt/gemini-client"
EXECUTABLE_BASE_NAME="gemini-client"
CONFIG_FILE="appsettings.json"

# --- Helper Functions ---
function check_deps() {
    echo "--> Checking for dependencies (curl, jq)..."
    if ! command -v curl &> /dev/null || ! command -v jq &> /dev/null; then
        echo "Error: 'curl' and 'jq' are required to run this script."
        echo "Please install them using your package manager."
        echo "  - Debian/Ubuntu: sudo apt-get update && sudo apt-get install curl jq"
        echo "  - Fedora/RHEL:   sudo dnf install curl jq"
        echo "  - Arch Linux:    sudo pacman -S curl jq"
        exit 1
    fi
}

function detect_arch() {
    echo "--> Detecting system architecture..."
    ARCH=$(uname -m)
    case $ARCH in
        x86_64)
            GH_ARCH="linux-x64"
            ;;
        aarch64)
            GH_ARCH="linux-arm64"
            ;;
        armv7l)
            GH_ARCH="linux-arm"
            ;;
        *)
            echo "Error: Unsupported architecture '$ARCH'."
            exit 1
            ;;
    esac
    echo "    Architecture found: $GH_ARCH"
}

# --- Main Script ---

check_deps
detect_arch

# Construct the expected asset name and executable name from the detected architecture
ASSET_NAME="${EXECUTABLE_BASE_NAME}-${GH_ARCH}.tar.gz"
EXECUTABLE_NAME="${EXECUTABLE_BASE_NAME}-${GH_ARCH}"

echo "--> Fetching latest release information from GitHub..."
API_URL="https://api.github.com/repos/$GITHUB_REPO/releases/latest"
DOWNLOAD_URL=$(curl -s "$API_URL" | jq -r ".assets[] | select(.name == \"$ASSET_NAME\") | .browser_download_url")

if [[ -z "$DOWNLOAD_URL" || "$DOWNLOAD_URL" == "null" ]]; then
    echo "Error: Could not find a download URL for asset '$ASSET_NAME' in the latest release."
    echo "Please check the releases page: https://github.com/$GITHUB_REPO/releases"
    exit 1
fi

echo "    Found download URL for version $(basename $(dirname $DOWNLOAD_URL))."

# Create temporary directories for download and extraction
TMP_DIR=$(mktemp -d)
trap 'rm -rf -- "$TMP_DIR"' EXIT # Ensure cleanup on script exit

TMP_ARCHIVE="$TMP_DIR/$ASSET_NAME"

echo "--> Downloading latest release to a temporary location..."
curl -L -o "$TMP_ARCHIVE" "$DOWNLOAD_URL"

echo "--> Extracting archive..."
# The archive contains the files directly, so we extract into a sub-folder
EXTRACT_DIR="$TMP_DIR/extracted"
mkdir -p "$EXTRACT_DIR"
tar -xzf "$TMP_ARCHIVE" -C "$EXTRACT_DIR"

# --- Configuration Preservation ---
DEST_CONFIG_PATH="$INSTALL_DIR/$CONFIG_FILE"
TEMP_CONFIG_BACKUP="$TMP_DIR/$CONFIG_FILE.bak"
PRESERVED_CONFIG=false

if [ -f "$DEST_CONFIG_PATH" ]; then
    echo "--> Found existing configuration. Preserving it."
    # Use sudo to copy the config file we might not have permission to read otherwise
    sudo cp "$DEST_CONFIG_PATH" "$TEMP_CONFIG_BACKUP"
    PRESERVED_CONFIG=true
fi

echo "--> Installing to $INSTALL_DIR (this may require sudo password)..."

# Use sudo for all subsequent operations that touch the destination directory
sudo mkdir -p "$INSTALL_DIR"
sudo rm -rf "${INSTALL_DIR:?}"/* # Clear out the old installation
sudo cp -r "$EXTRACT_DIR"/* "$INSTALL_DIR/"

# Restore the backed-up configuration if it exists
if [ "$PRESERVED_CONFIG" = true ]; then
    echo "--> Restoring previous configuration..."
    sudo mv "$TEMP_CONFIG_BACKUP" "$DEST_CONFIG_PATH"
fi

# Set executable permissions
EXECUTABLE_PATH="$INSTALL_DIR/$EXECUTABLE_NAME"
echo "--> Setting executable permission for $EXECUTABLE_PATH..."
sudo chmod +x "$EXECUTABLE_PATH"

echo ""
echo "✅ Installation/Update complete!"
echo "--------------------------------------------------"
echo "  Executable: $EXECUTABLE_PATH"
echo "  Config:     $DEST_CONFIG_PATH"
echo ""

if [ "$PRESERVED_CONFIG" = false ]; then
    echo "NOTE: A new '$CONFIG_FILE' was created."
    echo "You must edit it to add your API Key:"
    echo "  sudo nano $DEST_CONFIG_PATH"
else
    echo "Your existing '$CONFIG_FILE' has been preserved."
fi

echo ""
echo "To run the client from anywhere, you can create a symbolic link:"
echo "  sudo ln -s -f $EXECUTABLE_PATH /usr/local/bin/$EXECUTABLE_BASE_NAME"
echo ""
echo "Now you can run the client by typing: $EXECUTABLE_BASE_NAME"
echo ""

# The trap will handle cleaning up $TMP_DIR

Based on the `continuous-release.yml` and the `README.md`  found in the file dump, I have updated the installation script.

### Key Changes & Improvements:

1. 
**Repo URL:** Updated to `kusl/GeminiClient` matching the provided git output and `Directory.Build.props`.


2. 
**Binary Name Handling:** The CI workflow renames the binary to `gemini-client-linux-x64` before archiving. The script now accounts for this specific filename rather than a generic name.


3. 
**Config Generation:** The release workflow explicitly *only* tars the executable (`tar -czf ... gemini-client-linux-x64`). This means the `appsettings.json` is **not** included in the download. I added logic to generate a default configuration file if one is not present so the app doesn't crash on the first run.


4. 
**Architecture:** While the project code supports ARM64, the current release workflow  only builds for `linux-x64` and `win-x64`. I added a specific check to warn ARM users (like Raspberry Pi) that a release might not exist for them yet.



Here is the updated `install.sh`:

```bash
#!/bin/bash
set -e # Exit immediately if a command exits with a non-zero status.

# --- Configuration ---
GITHUB_REPO="kusl/GeminiClient"
INSTALL_DIR="/opt/gemini-client"
# The specific binary name generated by the CI workflow (e.g., gemini-client-linux-x64)
BINARY_BASE_NAME="gemini-client" 
SYMLINK_NAME="gemini-client"
CONFIG_FILE="appsettings.json"

# --- Helper Functions ---
function check_deps() {
    echo "--> Checking for dependencies..."
    local deps=("curl" "jq" "tar")
    local missing=()
    for dep in "${deps[@]}"; do
        if ! command -v "$dep" &> /dev/null; then
            missing+=("$dep")
        fi
    done

    if [ ${#missing[@]} -gt 0 ]; then
        echo "Error: The following dependencies are required: ${missing[*]}"
        echo "Please install them using your package manager."
        echo "  - Fedora/RHEL:   sudo dnf install ${missing[*]}"
        echo "  - Debian/Ubuntu: sudo apt-get update && sudo apt-get install ${missing[*]}"
        exit 1
    fi
}

function detect_arch() {
    echo "--> Detecting system architecture..."
    ARCH=$(uname -m)
    case $ARCH in
        x86_64)
            GH_ARCH="linux-x64"
            ;;
        aarch64)
            GH_ARCH="linux-arm64"
            echo "⚠️  Note: While the project supports ARM64, the current CI pipeline might only publish x64 releases."
            ;;
        armv7l)
            GH_ARCH="linux-arm"
            ;;
        *)
            echo "Error: Unsupported architecture '$ARCH'."
            exit 1
            ;;
    esac
    echo "    Architecture found: $GH_ARCH"
}

# --- Main Script ---

check_deps
detect_arch

# The asset name in GitHub releases (e.g., gemini-client-linux-x64.tar.gz)
ASSET_NAME="${BINARY_BASE_NAME}-${GH_ARCH}.tar.gz"
# The actual executable name inside the tar (e.g., gemini-client-linux-x64)
EXECUTABLE_NAME="${BINARY_BASE_NAME}-${GH_ARCH}"

echo "--> Fetching latest release information from GitHub..."
API_URL="https://api.github.com/repos/$GITHUB_REPO/releases/latest"
RELEASE_JSON=$(curl -s "$API_URL")
DOWNLOAD_URL=$(echo "$RELEASE_JSON" | jq -r ".assets[] | select(.name == \"$ASSET_NAME\") | .browser_download_url")

if [[ -z "$DOWNLOAD_URL" || "$DOWNLOAD_URL" == "null" ]]; then
    echo "Error: Could not find a download URL for asset '$ASSET_NAME' in the latest release."
    echo "This may mean there is no release for your architecture ($GH_ARCH) yet."
    echo "Please check the releases page: https://github.com/$GITHUB_REPO/releases"
    exit 1
fi

VERSION=$(echo "$RELEASE_JSON" | jq -r .tag_name)
echo "    Found version $VERSION at: $DOWNLOAD_URL"

# Create temporary directories
TMP_DIR=$(mktemp -d)
trap 'rm -rf -- "$TMP_DIR"' EXIT

TMP_ARCHIVE="$TMP_DIR/$ASSET_NAME"

echo "--> Downloading..."
curl -L -o "$TMP_ARCHIVE" "$DOWNLOAD_URL"

echo "--> Extracting..."
EXTRACT_DIR="$TMP_DIR/extracted"
mkdir -p "$EXTRACT_DIR"
tar -xzf "$TMP_ARCHIVE" -C "$EXTRACT_DIR"

# --- Configuration Management ---
DEST_CONFIG_PATH="$INSTALL_DIR/$CONFIG_FILE"
TEMP_CONFIG_BACKUP="$TMP_DIR/$CONFIG_FILE.bak"
CONFIG_STRATEGY="none"

# 1. Check if user already has a config
if [ -f "$DEST_CONFIG_PATH" ]; then
    echo "--> Found existing configuration. Preserving it."
    sudo cp "$DEST_CONFIG_PATH" "$TEMP_CONFIG_BACKUP"
    CONFIG_STRATEGY="restore"
else
    # 2. Check if the download included a config (Note: current CI excludes it)
    if [ -f "$EXTRACT_DIR/$CONFIG_FILE" ]; then
        echo "--> Using configuration provided in release."
        CONFIG_STRATEGY="copy_new"
    else
        # 3. Generate default config if missing
        echo "--> No configuration found in release. Generating default $CONFIG_FILE."
        cat <<EOF > "$EXTRACT_DIR/$CONFIG_FILE"
{
    "Logging": {
        "LogLevel": {
            "Default": "Warning",
            "GeminiClient": "Warning",
            "GeminiClientConsole": "Information"
        }
    },
    "GeminiSettings": {
        "ApiKey": "YOUR_GEMINI_API_KEY_HERE",
        "BaseUrl": "https://generativelanguage.googleapis.com/",
        "DefaultModel": "gemini-2.5-flash",
        "StreamingEnabled": true
    }
}
EOF
        CONFIG_STRATEGY="copy_generated"
    fi
fi

echo "--> Installing to $INSTALL_DIR (requires sudo)..."

# Prepare directory
sudo mkdir -p "$INSTALL_DIR"

# Copy executable
sudo cp "$EXTRACT_DIR/$EXECUTABLE_NAME" "$INSTALL_DIR/"

# Handle Config Placement
if [ "$CONFIG_STRATEGY" == "restore" ]; then
    sudo mv "$TEMP_CONFIG_BACKUP" "$DEST_CONFIG_PATH"
elif [ "$CONFIG_STRATEGY" == "copy_generated" ] || [ "$CONFIG_STRATEGY" == "copy_new" ]; then
    sudo cp "$EXTRACT_DIR/$CONFIG_FILE" "$DEST_CONFIG_PATH"
fi

# Set permissions
INSTALLED_EXEC="$INSTALL_DIR/$EXECUTABLE_NAME"
echo "--> Setting executable permissions..."
sudo chmod +x "$INSTALLED_EXEC"
# Ensure config is readable by users
sudo chmod 644 "$DEST_CONFIG_PATH"

# Symlink
echo "--> Updating symbolic link..."
sudo ln -sf "$INSTALLED_EXEC" "/usr/local/bin/$SYMLINK_NAME"

echo ""
echo "✅ Installation complete!"
echo "--------------------------------------------------"
echo "  Executable: $INSTALLED_EXEC"
echo "  Symlink:    /usr/local/bin/$SYMLINK_NAME"
echo "  Config:     $DEST_CONFIG_PATH"
echo "--------------------------------------------------"

if [ "$CONFIG_STRATEGY" == "copy_generated" ]; then
    echo "⚠️  IMPORTANT: A default configuration file was created."
    echo "   You MUST update it with your API Key before running:"
    echo "   sudo nano $DEST_CONFIG_PATH"
    echo ""
    echo "   Get your key here: https://aistudio.google.com/apikey"
elif [ "$CONFIG_STRATEGY" == "restore" ]; then
    echo "   Your previous configuration was preserved."
fi

echo ""
echo "Run the client by typing: $SYMLINK_NAME"

```















































