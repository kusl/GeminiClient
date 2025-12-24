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







































































