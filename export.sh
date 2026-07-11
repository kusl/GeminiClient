#!/usr/bin/env bash
# =============================================================================
# export.sh — Repository context dump for LLM consumption
#
# Location: <repo root>/export.sh
#
# Usage:  bash export.sh
#         time bash export.sh
#
# Behaviour:
#   • Resolves its own location; works regardless of CWD.
#   • Silently exits if no Git repository is found at that location.
#   • Dumps all Git-tracked files (excluding docs/llm/**, yarn.lock, .pnp.cjs,
#     and .pnp.loader.mjs) to docs/llm/dump.txt.
#   • Prepends its own source to the dump (self-documentation).
#   • Emits rich per-file metadata (name, path, size, permissions, mtime, SHA-256).
#   • Includes a file-tree view of all included files.
#   • Prints the complete dump to stdout before writing the file.
# =============================================================================

set -euo pipefail
IFS=$'\n\t'

# ---------------------------------------------------------------------------
# 0. Resolve script's own directory — immune to CWD
# ---------------------------------------------------------------------------
SCRIPT_PATH="$(readlink -f "${BASH_SOURCE[0]}")"
SCRIPT_DIR="$(dirname "$SCRIPT_PATH")"
SCRIPT_NAME="$(basename "$SCRIPT_PATH")"

# ---------------------------------------------------------------------------
# 1. Validate: must be inside a working Git repository
# ---------------------------------------------------------------------------
if ! git -C "$SCRIPT_DIR" rev-parse --is-inside-work-tree &>/dev/null 2>&1; then
    exit 0
fi

# Ensure git is usable (index readable, not a bare repo, etc.)
if ! git -C "$SCRIPT_DIR" status --porcelain &>/dev/null 2>&1; then
    exit 0
fi

REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)" || exit 0

# ---------------------------------------------------------------------------
# 2. Constants & derived paths
# ---------------------------------------------------------------------------
EXCLUDE_DIR="docs/llm"

# Individual files to exclude from the dump, in addition to EXCLUDE_DIR.
# A bare name (no slash) is excluded wherever it appears; a name containing a
# slash is treated as an exact repo-relative path.
#
# yarn.lock, .pnp.cjs, and .pnp.loader.mjs are all Yarn-generated artifacts
# (lockfile + PnP resolution map + PnP loader shim). They stay tracked in git
# for Yarn's Zero-Installs workflow, but they are pure noise for an LLM
# context dump, so they're excluded here rather than from git itself.
EXCLUDE_FILES=("yarn.lock" ".pnp.cjs" ".pnp.loader.mjs")
EXCLUDE_FILES_DISPLAY="$(printf '%s, ' "${EXCLUDE_FILES[@]}")"
EXCLUDE_FILES_DISPLAY="${EXCLUDE_FILES_DISPLAY%, }"

OUTPUT_DIR="${REPO_ROOT}/${EXCLUDE_DIR}"
OUTPUT_FILE="${OUTPUT_DIR}/dump.txt"

TIMESTAMP="$(date --iso-8601=seconds)"
GIT_BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')"
GIT_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || echo 'unknown')"
GIT_COMMIT_SHORT="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo 'unknown')"
GIT_COMMIT_MSG="$(git -C "$REPO_ROOT" log -1 --pretty=format:'%s' 2>/dev/null || echo 'unknown')"
GIT_COMMIT_DATE="$(git -C "$REPO_ROOT" log -1 --pretty=format:'%ci' 2>/dev/null || echo 'unknown')"
GIT_REMOTE="$(git -C "$REPO_ROOT" remote get-url origin 2>/dev/null || echo 'none')"
GIT_STATUS_SUMMARY="$(git -C "$REPO_ROOT" status --short 2>/dev/null | head -20 || echo '')"

HOSTNAME_VAL="$(hostname -f 2>/dev/null || hostname 2>/dev/null || echo 'unknown')"
USER_VAL="$(id -un 2>/dev/null || echo 'unknown')"
OS_VAL="$(uname -srm 2>/dev/null || echo 'unknown')"

# ---------------------------------------------------------------------------
# 3. Collect tracked files, excluding the output directory and EXCLUDE_FILES
#    git ls-files guarantees only committed/staged files; no build artefacts.
# ---------------------------------------------------------------------------
mapfile -t _RAW_FILES < <(
    git -C "$REPO_ROOT" ls-files \
        --cached \
        --others \
        --exclude-standard \
        -z \
        2>/dev/null \
    | tr '\0' '\n' \
    | sort -u
)

ALL_FILES=()
for _f in "${_RAW_FILES[@]}"; do
    [[ -z "$_f" ]] && continue
    # Skip the entire excluded directory tree.
    [[ "$_f" == "${EXCLUDE_DIR}/"* ]] && continue
    # Skip any individually-excluded file.
    _skip=0
    for _ex in "${EXCLUDE_FILES[@]}"; do
        if [[ "$_ex" == */* ]]; then
            [[ "$_f" == "$_ex" ]] && { _skip=1; break; }
        else
            [[ "$_f" == "$_ex" || "$_f" == */"$_ex" ]] && { _skip=1; break; }
        fi
    done
    (( _skip )) && continue
    ALL_FILES+=("$_f")
done

FILE_COUNT="${#ALL_FILES[@]}"

# ---------------------------------------------------------------------------
# 4. Helper: human-readable file size
# ---------------------------------------------------------------------------
human_size() {
    local bytes="$1"
    if   (( bytes < 1024 ));               then printf '%d B'      "$bytes"
    elif (( bytes < 1048576 ));            then printf '%.1f KiB'  "$(echo "scale=1; $bytes/1024"       | bc)"
    elif (( bytes < 1073741824 ));         then printf '%.1f MiB'  "$(echo "scale=1; $bytes/1048576"    | bc)"
    else                                        printf '%.1f GiB'  "$(echo "scale=1; $bytes/1073741824" | bc)"
    fi
}

# ---------------------------------------------------------------------------
# 5. Helper: SHA-256 of a file (portable across Linux distros)
# ---------------------------------------------------------------------------
file_sha256() {
    local path="$1"
    if command -v sha256sum &>/dev/null; then
        sha256sum "$path" | awk '{print $1}'
    elif command -v shasum &>/dev/null; then
        shasum -a 256 "$path" | awk '{print $1}'
    else
        echo 'unavailable'
    fi
}

# ---------------------------------------------------------------------------
# 6. Helper: MIME type (best-effort, no error on missing tool)
# ---------------------------------------------------------------------------
file_mime() {
    local path="$1"
    if command -v file &>/dev/null; then
        file --brief --mime-type "$path" 2>/dev/null || echo 'unknown'
    else
        echo 'unknown'
    fi
}

# ---------------------------------------------------------------------------
# 7. Build the tree view using 'tree' if available, else pure bash fallback
# ---------------------------------------------------------------------------
build_tree() {
    if command -v tree &>/dev/null; then
        # --fromfile reads the path list from stdin and renders a tree
        printf '%s\n' "${ALL_FILES[@]}" \
        | tree --fromfile -a --noreport 2>/dev/null \
        || _bash_tree
    else
        _bash_tree
    fi
}

_bash_tree() {
    # Pure-bash tree: reconstruct directory hierarchy from the flat file list
    local -A SEEN_DIRS=()
    local prev_depth=0
    local line

    for f in "${ALL_FILES[@]}"; do
        local dir parts depth i prefix
        dir="$(dirname "$f")"
        IFS='/' read -ra parts <<< "$f"
        depth=$(( ${#parts[@]} - 1 ))

        # Print intermediate directories we haven't seen yet
        local partial=""
        for (( i=0; i<depth; i++ )); do
            if [[ -z "$partial" ]]; then
                partial="${parts[$i]}"
            else
                partial="${partial}/${parts[$i]}"
            fi
            if [[ -z "${SEEN_DIRS[$partial]+_}" ]]; then
                SEEN_DIRS["$partial"]=1
                prefix="$(printf '│   %.0s' $(seq 1 $i))├── "
                printf '%s%s/\n' "$prefix" "${parts[$i]}"
            fi
        done

        # Print the file itself
        prefix="$(printf '│   %.0s' $(seq 1 $depth))└── "
        printf '%s%s\n' "$prefix" "${parts[$depth]}"
    done
}

# ---------------------------------------------------------------------------
# 8. Assemble the dump into a variable (stdout + file simultaneously)
# ---------------------------------------------------------------------------
mkdir -p "$OUTPUT_DIR"

# We use a temp file to build, then atomic-rename at the end.
TMP_FILE="$(mktemp "${OUTPUT_DIR}/.dump.XXXXXX")"
trap 'rm -f "$TMP_FILE"' EXIT

# Tee everything to both stdout and the temp file
exec > >(tee "$TMP_FILE") 2>&1

# ── Preamble banner ──────────────────────────────────────────────────────────
cat <<BANNER
################################################################################
#                                                                              #
#   REPOSITORY CONTEXT DUMP                                                    #
#   Generated for LLM consumption — do not edit manually                       #
#                                                                              #
################################################################################

DUMP METADATA
═════════════════════════════════════════════════════════════════════════════════
  Generated at   : ${TIMESTAMP}
  Generator      : ${SCRIPT_NAME}
  Host           : ${HOSTNAME_VAL}
  User           : ${USER_VAL}
  OS             : ${OS_VAL}

REPOSITORY METADATA
═════════════════════════════════════════════════════════════════════════════════
  Repository root: ${REPO_ROOT}
  Branch         : ${GIT_BRANCH}
  Commit (full)  : ${GIT_COMMIT}
  Commit (short) : ${GIT_COMMIT_SHORT}
  Commit date    : ${GIT_COMMIT_DATE}
  Commit message : ${GIT_COMMIT_MSG}
  Remote origin  : ${GIT_REMOTE}
  Files included : ${FILE_COUNT}
  Excluded path  : ${EXCLUDE_DIR}/
  Excluded files : ${EXCLUDE_FILES_DISPLAY}

GIT WORKING TREE STATUS (first 20 lines)
═════════════════════════════════════════════════════════════════════════════════
BANNER

if [[ -n "$GIT_STATUS_SUMMARY" ]]; then
    printf '%s\n' "$GIT_STATUS_SUMMARY"
else
    echo "  (clean — no uncommitted changes)"
fi

# ── Self-documentation: dump this script first ───────────────────────────────
cat <<SELF_HEADER

################################################################################
# FILE: ${SCRIPT_NAME}  [THIS SCRIPT — included for full context]
################################################################################
SELF_HEADER

printf '\n--- METADATA ---\n'
_self_size="$(wc -c < "$SCRIPT_PATH")"
_self_mtime="$(stat --format='%y' "$SCRIPT_PATH" 2>/dev/null \
               || stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S %z' "$SCRIPT_PATH" 2>/dev/null \
               || echo 'unavailable')"
_self_perms="$(stat --format='%A' "$SCRIPT_PATH" 2>/dev/null \
               || stat -f '%Sp' "$SCRIPT_PATH" 2>/dev/null \
               || echo 'unavailable')"
_self_sha="$(file_sha256 "$SCRIPT_PATH")"

printf '  %-22s %s\n'  "File name:"       "$SCRIPT_NAME"
printf '  %-22s %s\n'  "Relative path:"   "$(realpath --relative-to="$REPO_ROOT" "$SCRIPT_PATH" 2>/dev/null || echo "$SCRIPT_PATH")"
printf '  %-22s %s\n'  "Size:"            "$(human_size "$_self_size") (${_self_size} bytes)"
printf '  %-22s %s\n'  "Last modified:"   "$_self_mtime"
printf '  %-22s %s\n'  "Permissions:"     "$_self_perms"
printf '  %-22s %s\n'  "SHA-256:"         "$_self_sha"
printf '  %-22s %s\n'  "MIME type:"       "$(file_mime "$SCRIPT_PATH")"
printf '\n--- CONTENT ---\n'
cat "$SCRIPT_PATH"

# ── File tree ─────────────────────────────────────────────────────────────────
cat <<TREE_HEADER

################################################################################
# FILE TREE  (all ${FILE_COUNT} included files)
################################################################################
.
TREE_HEADER

build_tree
echo ""

# ── Per-file content dump ─────────────────────────────────────────────────────
TOTAL_BYTES=0

for rel_path in "${ALL_FILES[@]}"; do
    abs_path="${REPO_ROOT}/${rel_path}"

    # Skip if the file no longer exists on disk (deleted but still indexed)
    [[ -f "$abs_path" ]] || continue

    # ── Metadata collection ────────────────────────────────────────────────
    file_size="$(wc -c < "$abs_path" 2>/dev/null || echo 0)"
    TOTAL_BYTES=$(( TOTAL_BYTES + file_size ))

    file_mtime="$(stat --format='%y' "$abs_path" 2>/dev/null \
                  || stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S %z' "$abs_path" 2>/dev/null \
                  || echo 'unavailable')"

    file_perms="$(stat --format='%A' "$abs_path" 2>/dev/null \
                  || stat -f '%Sp' "$abs_path" 2>/dev/null \
                  || echo 'unavailable')"

    file_owner="$(stat --format='%U:%G' "$abs_path" 2>/dev/null \
                  || stat -f '%Su:%Sg' "$abs_path" 2>/dev/null \
                  || echo 'unavailable')"

    file_inode="$(stat --format='%i' "$abs_path" 2>/dev/null \
                  || stat -f '%i' "$abs_path" 2>/dev/null \
                  || echo 'unavailable')"

    file_links="$(stat --format='%h' "$abs_path" 2>/dev/null \
                  || stat -f '%l' "$abs_path" 2>/dev/null \
                  || echo 'unavailable')"

    file_sha="$(file_sha256 "$abs_path")"
    file_type="$(file_mime "$abs_path")"

    git_log_line="$(git -C "$REPO_ROOT" log -1 --pretty=format:'%h %ai %s' -- "$rel_path" 2>/dev/null || echo 'unavailable')"

    # ── Section header ─────────────────────────────────────────────────────
    printf '\n'
    printf '################################################################################\n'
    printf '# FILE: %s\n' "$rel_path"
    printf '################################################################################\n'
    printf '\n--- METADATA ---\n'
    printf '  %-22s %s\n'  "File name:"       "$(basename "$rel_path")"
    printf '  %-22s %s\n'  "Relative path:"   "$rel_path"
    printf '  %-22s %s\n'  "Absolute path:"   "$abs_path"
    printf '  %-22s %s\n'  "Size:"            "$(human_size "$file_size") (${file_size} bytes)"
    printf '  %-22s %s\n'  "Last modified:"   "$file_mtime"
    printf '  %-22s %s\n'  "Permissions:"     "$file_perms"
    printf '  %-22s %s\n'  "Owner:"           "$file_owner"
    printf '  %-22s %s\n'  "Inode:"           "$file_inode"
    printf '  %-22s %s\n'  "Hard links:"      "$file_links"
    printf '  %-22s %s\n'  "MIME type:"       "$file_type"
    printf '  %-22s %s\n'  "SHA-256:"         "$file_sha"
    printf '  %-22s %s\n'  "Last git commit:" "$git_log_line"
    printf '\n--- CONTENT ---\n'

    # ── Content emission ───────────────────────────────────────────────────
    # Binary files: emit a notice instead of raw bytes
    if [[ "$file_type" == application/octet-stream ]] \
    || [[ "$file_type" == image/* ]] \
    || [[ "$file_type" == audio/* ]] \
    || [[ "$file_type" == video/* ]] \
    || ! file --brief "$abs_path" 2>/dev/null | grep -qi 'text\|script\|xml\|json\|yaml\|svg\|source\|empty'; then
        printf '[Binary file — content omitted. Size: %s, SHA-256: %s]\n' \
            "$(human_size "$file_size")" "$file_sha"
    else
        cat "$abs_path"
        # Ensure there is always a trailing newline so the next header aligns
        [[ "$(tail -c1 "$abs_path" | wc -c)" -gt 0 ]] \
            && [[ "$(tail -c1 "$abs_path")" != $'\n' ]] \
            && printf '\n'
    fi
done

# ── Summary footer ────────────────────────────────────────────────────────────
cat <<FOOTER

################################################################################
# DUMP SUMMARY
################################################################################
  Files dumped   : ${FILE_COUNT}
  Total size     : $(human_size "$TOTAL_BYTES") (${TOTAL_BYTES} bytes)
  Output file    : ${OUTPUT_FILE}
  Completed at   : $(date --iso-8601=seconds)
################################################################################
# END OF DUMP
################################################################################
FOOTER

# ---------------------------------------------------------------------------
# 9. Atomic rename — avoids partial writes being read by concurrent processes
# ---------------------------------------------------------------------------
mv -f "$TMP_FILE" "$OUTPUT_FILE"
