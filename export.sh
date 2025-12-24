#!/bin/bash
# =============================================================================
# Complete Project Export for LLM Analysis
# =============================================================================
# Exports all relevant source files, configs, and documentation for AI review.
# Excludes: binaries, build outputs, IDE files, packages, git internals
# =============================================================================

set -e

OUTPUT_DIR="docs/llm"
OUTPUT_FILE="$OUTPUT_DIR/dump.txt"
PROJECT_PATH="$(pwd)"

# File extensions to include (comprehensive list for .NET/Avalonia projects)
# Source code
SOURCE_EXTS="cs|fs|vb"
# XAML/Avalonia UI
XAML_EXTS="axaml|xaml|paml"
# Project/Build files
PROJECT_EXTS="csproj|fsproj|vbproj|slnx|sln|props|targets|tasks"
# Config files
CONFIG_EXTS="json|yaml|yml|xml|config|settings"
# Documentation
DOC_EXTS="md|txt|rst|adoc"
# Scripts
SCRIPT_EXTS="sh|ps1|psm1|cmd|bat"
# Web/Style (in case of Avalonia web or styling)
STYLE_EXTS="css|scss|sass|less"
# Data/Templates
DATA_EXTS="sql|csv|resx|resources"
# Docker/CI
DEVOPS_EXTS="dockerfile|dockerignore|editorconfig|gitignore|gitattributes"

# Combine all extensions
ALL_EXTS="$SOURCE_EXTS|$XAML_EXTS|$PROJECT_EXTS|$CONFIG_EXTS|$DOC_EXTS|$SCRIPT_EXTS|$STYLE_EXTS|$DATA_EXTS"

# Directories to exclude
EXCLUDE_DIRS="bin|obj|.git|.vs|.idea|.vscode|node_modules|packages|TestResults|coverage|publish|artifacts|.nuget|wwwroot/lib"

# Files to exclude (patterns)
EXCLUDE_FILES="*.Designer.cs|*.g.cs|*.g.i.cs|AssemblyInfo.cs|*.min.js|*.min.css|package-lock.json|*.lock|*.bak"

echo "=============================================="
echo "  Project Export for LLM Analysis"
echo "=============================================="
echo ""
echo "Project Path: $PROJECT_PATH"
echo "Output File:  $OUTPUT_FILE"
echo ""

mkdir -p "$OUTPUT_DIR"

# Start output file
{
    echo "==============================================================================="
    echo "PROJECT EXPORT"
    echo "Generated: $(date)"
    echo "Project Path: $PROJECT_PATH"
    echo "==============================================================================="
    echo ""
} > "$OUTPUT_FILE"

# Directory structure
echo "Generating directory structure..."
{
    echo "DIRECTORY STRUCTURE:"
    echo "==================="
    echo ""
    # Try tree first, fall back to find
    if command -v tree &> /dev/null; then
        tree -a -I "$EXCLUDE_DIRS" --noreport --dirsfirst 2>/dev/null || echo "(tree command failed)"
    else
        find . -type d \( -name "bin" -o -name "obj" -o -name ".git" -o -name ".vs" -o -name ".idea" -o -name "node_modules" -o -name "packages" -o -name "TestResults" \) -prune -o -type f -print | sed 's|[^/]*/|  |g' | sort
    fi
    echo ""
    echo ""
} >> "$OUTPUT_FILE"

# Build the find command dynamically
echo "Collecting files..."

# Create a temporary file for the file list
TMPFILE=$(mktemp)

# Find all relevant files
find . -type f \( \
    -iname "*.cs" -o \
    -iname "*.fs" -o \
    -iname "*.vb" -o \
    -iname "*.axaml" -o \
    -iname "*.xaml" -o \
    -iname "*.paml" -o \
    -iname "*.csproj" -o \
    -iname "*.fsproj" -o \
    -iname "*.vbproj" -o \
    -iname "*.slnx" -o \
    -iname "*.sln" -o \
    -iname "*.props" -o \
    -iname "*.targets" -o \
    -iname "*.json" -o \
    -iname "*.yaml" -o \
    -iname "*.yml" -o \
    -iname "*.xml" -o \
    -iname "*.config" -o \
    -iname "*.md" -o \
    -iname "*.txt" -o \
    -iname "*.sh" -o \
    -iname "*.ps1" -o \
    -iname "*.cmd" -o \
    -iname "*.bat" -o \
    -iname "*.sql" -o \
    -iname "*.resx" -o \
    -iname "*.css" -o \
    -iname "*.scss" -o \
    -iname "*.manifest" -o \
    -iname "*.ico" -o \
    -iname "Dockerfile" -o \
    -iname "docker-compose*.yml" -o \
    -iname ".editorconfig" -o \
    -iname ".gitignore" -o \
    -iname ".gitattributes" -o \
    -iname "global.json" -o \
    -iname "nuget.config" -o \
    -iname "Directory.Build.props" -o \
    -iname "Directory.Build.targets" -o \
    -iname "Directory.Packages.props" \
    \) \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    ! -path "*/docs/*" \
    ! -path "*/.git/*" \
    ! -path "*/.vs/*" \
    ! -path "*/.idea/*" \
    ! -path "*/.vscode/*" \
    ! -path "*/node_modules/*" \
    ! -path "*/packages/*" \
    ! -path "*/TestResults/*" \
    ! -path "*/coverage/*" \
    ! -path "*/publish/*" \
    ! -path "*/artifacts/*" \
    ! -path "*/.nuget/*" \
    ! -name "*.Designer.cs" \
    ! -name "*.g.cs" \
    ! -name "*.g.i.cs" \
    ! -name "*.min.js" \
    ! -name "*.min.css" \
    ! -name "package-lock.json" \
    ! -name "*.bak" \
    2>/dev/null | sort > "$TMPFILE"

FILE_COUNT=$(wc -l < "$TMPFILE")
echo "Found $FILE_COUNT files to export"
echo ""

# Add file contents header
{
    echo "FILE CONTENTS:"
    echo "=============="
    echo ""
} >> "$OUTPUT_FILE"

# Process each file
COUNTER=0
SKIPPED=0

while IFS= read -r file; do
    COUNTER=$((COUNTER + 1))
    FILENAME="${file#./}"
    
    # Skip binary files (check if file is text)
    if file "$file" | grep -qE "binary|executable|data|image"; then
        # For some files we want to note they exist but not dump contents
        if [[ "$file" =~ \.(ico|png|jpg|jpeg|gif|bmp|svg|woff|woff2|ttf|eot)$ ]]; then
            SKIPPED=$((SKIPPED + 1))
            echo "Skipping binary ($COUNTER/$FILE_COUNT): $FILENAME"
            {
                echo "================================================================================"
                echo "FILE: $FILENAME"
                echo "TYPE: [BINARY FILE - Contents not exported]"
                echo "================================================================================"
                echo ""
            } >> "$OUTPUT_FILE"
            continue
        fi
    fi
    
    # Get file info
    FILESIZE=$(stat -c%s "$file" 2>/dev/null || stat -f%z "$file" 2>/dev/null || echo "0")
    MODIFIED=$(stat -c%y "$file" 2>/dev/null | cut -d'.' -f1 || stat -f"%Sm" -t "%Y-%m-%d %H:%M:%S" "$file" 2>/dev/null || echo "unknown")
    
    # Skip very large files (>500KB) - they're probably not source code
    if [ "$FILESIZE" -gt 512000 ]; then
        SKIPPED=$((SKIPPED + 1))
        echo "Skipping large file ($COUNTER/$FILE_COUNT): $FILENAME ($(echo "scale=0; $FILESIZE/1024" | bc)KB)"
        {
            echo "================================================================================"
            echo "FILE: $FILENAME"
            echo "SIZE: $(echo "scale=2; $FILESIZE/1024" | bc) KB"
            echo "TYPE: [LARGE FILE - Contents not exported, exceeds 500KB limit]"
            echo "================================================================================"
            echo ""
        } >> "$OUTPUT_FILE"
        continue
    fi
    
    echo "Processing ($COUNTER/$FILE_COUNT): $FILENAME"
    
    {
        echo "================================================================================"
        echo "FILE: $FILENAME"
        echo "SIZE: $(echo "scale=2; $FILESIZE/1024" | bc 2>/dev/null || echo "0.00") KB"
        echo "MODIFIED: $MODIFIED"
        echo "================================================================================"
        echo ""
        cat "$file" 2>/dev/null || echo "[ERROR: Could not read file]"
        echo ""
        echo ""
    } >> "$OUTPUT_FILE"
    
done < "$TMPFILE"

# Cleanup
rm -f "$TMPFILE"

# Summary
EXPORTED=$((COUNTER - SKIPPED))
{
    echo "==============================================================================="
    echo "EXPORT COMPLETED: $(date)"
    echo "Total Files Found: $FILE_COUNT"
    echo "Files Exported: $EXPORTED"
    echo "Files Skipped: $SKIPPED (binary or large files)"
    echo "Output File: $PROJECT_PATH/$OUTPUT_FILE"
    echo "==============================================================================="
} >> "$OUTPUT_FILE"

# Final output
OUTPUT_SIZE=$(stat -c%s "$OUTPUT_FILE" 2>/dev/null || stat -f%z "$OUTPUT_FILE" 2>/dev/null || echo "0")

echo ""
echo "=============================================="
echo "  Export Complete!"
echo "=============================================="
echo ""
echo "Output file:    $OUTPUT_FILE"
echo "Files exported: $EXPORTED"
echo "Files skipped:  $SKIPPED"
echo "Output size:    $(echo "scale=2; $OUTPUT_SIZE/1024" | bc 2>/dev/null || echo "?") KB"
echo ""
echo "File types included:"
echo "  • Source code: .cs, .fs, .vb"
echo "  • UI/XAML: .axaml, .xaml, .paml"
echo "  • Projects: .csproj, .slnx, .sln, .props, .targets"
echo "  • Config: .json, .yaml, .yml, .xml, .config"
echo "  • Docs: .md, .txt"
echo "  • Scripts: .sh, .ps1, .cmd, .bat"
echo "  • Other: .sql, .resx, .css, .scss, Dockerfile, etc."
echo ""
