#!/usr/bin/env bash
# ==============================================================================
# BadBuilder v2.1.0 - Xbox 360 BadUpdate USB Builder
# Linux native shell version (no .NET required)
# Original: https://github.com/Pdawg-bytes/BadBuilder
# ==============================================================================
set -euo pipefail
IFS=$'\n\t'

# ── Colours ───────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'    # #76B900
DKGREEN='\033[0;32m'
ORANGE='\033[38;5;208m'
YELLOW='\033[1;33m'
GRAY='\033[0;90m'
WHITE='\033[1;37m'
BOLD='\033[1m'
RESET='\033[0m'

# ── Script / work dirs ────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)"
WORK_DIR="${SCRIPT_DIR}/Work"
DOWNLOAD_DIR="${WORK_DIR}/Downloads"
STAGING_DIR="${WORK_DIR}/Staging"

# ── Config state ──────────────────────────────────────────────────────────────
TARGET_DISK_ID=""        # e.g. sdb
TARGET_DISK_NAME=""      # model string
TARGET_DISK_SIZE_GB=""
TARGET_DISK_PATH=""      # /dev/sdb
MOUNT_POINT=""
SELECTED_EXPLOIT="BadUpdate"         # BadUpdate | ABadAvatar | ABadAvatarUpdate
SELECTED_BOOTSTRAP="XeUnshackle"    # XeUnshackle | FreeMyXe
FIRMWARE_UPDATE=false

# Homebrew list (parallel arrays)
HB_IDS=()
HB_NAMES=()
HB_ENABLED=()    # "1" = included
HB_ENTRY=()      # relative .xex path or ""
HB_SRC_PATH=()   # local path for custom archives; empty = built-in
LAUNCH_HB_ID=""  # ID of homebrew to auto-launch (empty = none)

# Populate built-ins (always start enabled)
_init_homebrew() {
    HB_IDS=(   "homebrew-aurora"  "homebrew-xexmenu"  "homebrew-simple360nandflasher" )
    HB_NAMES=( "Aurora"           "XeXMenu"            "Simple 360 NAND Flasher"       )
    HB_ENABLED=( "1"              "1"                  "1"                              )
    HB_ENTRY=( "Aurora.xex"       ""                   "Simple 360 NAND Flasher/Default.xex" )
    HB_SRC_PATH=( ""              ""                   ""                               )
}
_init_homebrew

# ── TUI helpers ───────────────────────────────────────────────────────────────
render_header() {
    clear
    echo -e "${DKGREEN}██████╗  █████╗ ██████╗ ██████╗ ██╗   ██╗██╗██╗     ██████╗ ███████╗██████╗${RESET}"
    echo -e "${DKGREEN}██╔══██╗██╔══██╗██╔══██╗██╔══██╗██║   ██║██║██║     ██╔══██╗██╔════╝██╔══██╗${RESET}"
    echo -e "${GREEN}██████╔╝███████║██║  ██║██████╔╝██║   ██║██║██║     ██║  ██║█████╗  ██████╔╝${RESET}"
    echo -e "${GREEN}██╔══██╗██╔══██║██║  ██║██╔══██╗██║   ██║██║██║     ██║  ██║██╔══╝  ██╔══██╗${RESET}"
    echo -e "${GREEN}██████╔╝██║  ██║██████╔╝██████╔╝╚██████╔╝██║███████╗██████╔╝███████╗██║  ██║${RESET}"
    echo -e "${GREEN}╚═════╝ ╚═╝  ╚═╝╚═════╝ ╚═════╝  ╚═════╝ ╚═╝╚══════╝╚═════╝ ╚══════╝╚═╝  ╚═╝${RESET}"
    echo ""
    echo -e "${GREEN}──────────────────────────────────────────────────────────────v2.1.0${RESET}"
    echo -e "───────────────────────Xbox 360 ${ORANGE}BadUpdate${RESET} USB Builder───────────────────────"
    echo -e "                            ${GRAY}Created by Pdawg | Linux port${RESET}"
    echo -e "${GREEN}────────────────────────────────────────────────────────────────────────${RESET}"
    echo ""
}

show_summary() {
    echo -e "┌──────────────────────────────────────────────────────────┐"
    if $FIRMWARE_UPDATE; then
        printf "│  %-16s  %-38s│\n" "Drive"  "${TARGET_DISK_NAME:-None selected} ${TARGET_DISK_PATH:+($TARGET_DISK_PATH)}"
        printf "│  %-16s  %-38s│\n" "Update"  "Update to 2.0.17559.0"
    else
        printf "│  %-16s  %-38s│\n" "Drive"      "${TARGET_DISK_NAME:-None selected} ${TARGET_DISK_PATH:+($TARGET_DISK_PATH)}"
        printf "│  %-16s  %-38s│\n" "Exploit"    "$SELECTED_EXPLOIT"
        printf "│  %-16s  %-38s│\n" "Bootstrap"  "$SELECTED_BOOTSTRAP"
        local hblist=""
        for i in "${!HB_IDS[@]}"; do
            [[ "${HB_ENABLED[$i]}" == "1" ]] || continue
            local mark=""
            [[ "${HB_IDS[$i]}" == "$LAUNCH_HB_ID" ]] && mark=" (launch)"
            hblist+="${HB_NAMES[$i]}${mark}, "
        done
        hblist="${hblist%, }"
        printf "│  %-16s  %-38s│\n" "Homebrew"   "${hblist:-None}"
    fi
    echo -e "└──────────────────────────────────────────────────────────┘"
    echo ""
}

msg_info()    { echo -e "${YELLOW}[*]${RESET} $*" >&2; }
msg_ok()      { echo -e "${GREEN}[+]${RESET} $*" >&2; }
msg_warn()    { echo -e "${ORANGE}[!]${RESET} $*" >&2; }
msg_err()     { echo -e "${RED}[-]${RESET} $*" >&2; }

pause() {
    local msg="${1:-Press Enter to continue...}"
    echo -e "\n${GRAY}${msg}${RESET}"
    read -r _
}

# Numbered selection menu. Sets REPLY_INDEX (0-based) and REPLY_VALUE.
# Usage: prompt_menu "Title" "desc1" "desc2" ...
# Returns 255 on Back/Cancel.
prompt_menu() {
    local title="$1"; shift
    local options=("$@")
    echo ""
    echo -e "${ORANGE}${BOLD}${title}${RESET}"
    for i in "${!options[@]}"; do
        echo -e "  ${GREEN}$((i+1))${RESET}. ${options[$i]}"
    done
    echo ""
    while true; do
        read -rp "Select [1-${#options[@]}]: " choice
        if [[ "$choice" =~ ^[0-9]+$ ]] && (( choice >= 1 && choice <= ${#options[@]} )); then
            REPLY_INDEX=$(( choice - 1 ))
            REPLY_VALUE="${options[$REPLY_INDEX]}"
            return 0
        fi
        echo -e "${RED}Invalid choice.${RESET}"
    done
}

confirm() {
    # confirm "question" → returns 0 for yes, 1 for no
    local question="$1"
    while true; do
        read -rp "${question} [y/N]: " yn
        case "${yn,,}" in
            y|yes) return 0 ;;
            n|no|"") return 1 ;;
        esac
    done
}

# ── Dependency check ──────────────────────────────────────────────────────────
check_deps() {
    local missing=()
    local optional_missing=()

    for cmd in lsblk parted mkfs.fat mount umount curl jq sha256sum; do
        command -v "$cmd" &>/dev/null || missing+=("$cmd")
    done

    # Archive extractors
    command -v unzip &>/dev/null || missing+=("unzip")
    if ! command -v 7z &>/dev/null && ! command -v 7za &>/dev/null; then
        missing+=("p7zip-full (7z)")
    fi
    if ! command -v unrar &>/dev/null && ! command -v unar &>/dev/null; then
        optional_missing+=("unrar or unar (needed for Aurora.rar)")
    fi
    if ! command -v partprobe &>/dev/null && ! command -v blockdev &>/dev/null; then
        optional_missing+=("partprobe/blockdev (partition reload; parted works too)")
    fi

    if [[ ${#missing[@]} -gt 0 ]]; then
        msg_err "Missing required dependencies:"
        for dep in "${missing[@]}"; do echo "    - $dep"; done
        echo ""
        echo "Install on Debian/Ubuntu: sudo apt install util-linux dosfstools parted curl jq unzip p7zip-full"
        echo "Install on Fedora/RHEL:   sudo dnf install util-linux dosfstools parted curl jq unzip p7zip"
        exit 1
    fi

    if [[ ${#optional_missing[@]} -gt 0 ]]; then
        for dep in "${optional_missing[@]}"; do
            msg_warn "Optional dependency missing: $dep"
        done
    fi
}

# ── Disk helpers ──────────────────────────────────────────────────────────────
list_disks() {
    # Outputs: id|name|size_gb|removable|path
    lsblk -J -d -b -o NAME,SIZE,TYPE,TRAN,MODEL,RM 2>/dev/null | \
    jq -r '.blockdevices[] |
        select(.type == "disk") |
        select(.name | test("^loop|^sr|^fd"; "i") | not) |
        [
          .name,
          (.model // .name | ltrimstr(" ") | rtrimstr(" ")),
          ((.size // 0) | tonumber / 1073741824 | . * 100 | round / 100 | tostring),
          (if .rm == true or .rm == 1 or (.rm | tostring) == "1" or .tran == "usb" then "Removable" else "Fixed" end),
          ("/dev/" + .name)
        ] | join("|")'
}

umount_partitions() {
    local dev="$1"          # e.g. /dev/sdb
    local devname
    devname="$(basename "$dev")"
    if [[ -f /proc/mounts ]]; then
        while IFS=' ' read -r mdev mpoint _rest; do
            local mname
            mname="$(basename "$mdev")"
            if [[ "$mname" == "${devname}"* && "$mname" != "$devname" ]]; then
                umount "$mdev" 2>/dev/null || true
            fi
        done < /proc/mounts
    fi
}

format_fat32() {
    local dev="$1"    # /dev/sdb
    local devname
    devname="$(basename "$dev")"

    msg_info "Unmounting partitions on $dev..."
    umount_partitions "$dev"

    msg_info "Writing partition table..."
    parted -s "$dev" mklabel msdos
    parted -s "$dev" mkpart primary fat32 1MiB 100%
    parted -s "$dev" set 1 lba on

    # Reload partition table
    partprobe "$dev" 2>/dev/null || blockdev --rereadpt "$dev" 2>/dev/null || true
    udevadm settle 2>/dev/null || sleep 2

    # Find partition device
    local part=""
    for _try in $(seq 1 10); do
        if   [[ -b "${dev}1"  ]]; then part="${dev}1";  break
        elif [[ -b "${dev}p1" ]]; then part="${dev}p1"; break
        fi
        sleep 0.5
    done
    [[ -n "$part" ]] || { msg_err "Partition ${dev}1 did not appear."; exit 1; }

    msg_info "Formatting $part as FAT32..."
    mkfs.fat -F 32 -n "BADUPDATE" "$part"

    # Mount
    MOUNT_POINT="/tmp/badbuilder_${devname}"
    umount "$MOUNT_POINT" 2>/dev/null || true
    mkdir -p "$MOUNT_POINT"
    mount "$part" "$MOUNT_POINT"
    msg_ok "Drive formatted and mounted at $MOUNT_POINT"
}

# ── Download helpers ──────────────────────────────────────────────────────────
_7z_cmd() { command -v 7z &>/dev/null && echo "7z" || echo "7za"; }

_rar_extract() {
    local archive="$1" dest="$2"
    if command -v unrar &>/dev/null; then
        unrar x -o+ "$archive" "$dest"/ >/dev/null
    elif command -v unar &>/dev/null; then
        unar -o "$dest" -f "$archive" >/dev/null
    else
        msg_err "No RAR extractor found (install unrar or unar)."
        exit 1
    fi
}

extract_archive() {
    local archive="$1"
    local dest="$2"
    mkdir -p "$dest"
    case "${archive,,}" in
        *.zip)       unzip -o -q "$archive" -d "$dest" ;;
        *.7z)        "$(_7z_cmd)" x "$archive" -o"$dest" -y >/dev/null ;;
        *.rar)       _rar_extract "$archive" "$dest" ;;
        *.tar.gz|*.tgz) tar -xzf "$archive" -C "$dest" ;;
        *.tar.bz2)   tar -xjf "$archive" -C "$dest" ;;
        *.tar.xz)    tar -xJf "$archive" -C "$dest" ;;
        *)
            # Try 7z as generic fallback
            "$(_7z_cmd)" x "$archive" -o"$dest" -y >/dev/null || {
                msg_err "Cannot extract: $archive"
                exit 1
            }
            ;;
    esac
}

# Resolve GitHub latest or tagged release JSON
gh_release_json() {
    local owner="$1" repo="$2" tag="${3:-}"
    local url
    if [[ -n "$tag" ]]; then
        url="https://api.github.com/repos/${owner}/${repo}/releases/tags/${tag}"
    else
        url="https://api.github.com/repos/${owner}/${repo}/releases/latest"
    fi
    local json
    json="$(curl -sf --retry 3 "$url")" || {
        # Fallback: first release in list
        json="$(curl -sf --retry 3 "https://api.github.com/repos/${owner}/${repo}/releases" | jq '.[0]')" || echo ""
    }
    echo "$json"
}

# Get browser download URL of the first non-checksum asset, or named asset
gh_asset_url() {
    local json="$1" asset_name="${2:-}"
    if [[ -n "$asset_name" ]]; then
        echo "$json" | jq -r --arg n "$asset_name" '.assets[] | select(.name == $n) | .browser_download_url' | head -1
    else
        echo "$json" | jq -r '.assets[] | select(.name | test("sha256|checksum"; "i") | not) | .browser_download_url' | head -1
    fi
}

gh_version_tag() { echo "$1" | jq -r '.tag_name // "unknown"'; }

sha_from_json() {
    # Extract SHA-256 from a release JSON body; safe with empty/invalid JSON.
    local json="$1"
    [[ -z "$json" ]] && { echo ""; return 0; }
    echo "$json" | jq -r '.body // ""' 2>/dev/null \
        | grep -oi 'sha256:[[:space:]]*[0-9a-fA-F]\{64\}' \
        | grep -oi '[0-9a-fA-F]\{64\}' \
        | head -1 \
        | tr '[:lower:]' '[:upper:]' \
        || echo ""
}

# Download with progress, skip if cached and SHA matches
download_artifact() {
    local id="$1"       # artifact ID for cache dir
    local url="$2"
    local filename="$3"
    local version_tag="${4:-direct}"
    local expected_sha="${5:-}"  # empty = no check

    local cache_dir="${DOWNLOAD_DIR}/${id}"
    local out_file="${cache_dir}/${filename}"
    mkdir -p "$cache_dir"

    local cached_version=""
    [[ -f "${cache_dir}/version.txt" ]] && cached_version="$(cat "${cache_dir}/version.txt")"

    # If cached file exists and version matches, verify SHA if available
    if [[ -f "$out_file" && "$cached_version" == "$version_tag" ]]; then
        if [[ -n "$expected_sha" ]]; then
            local actual_sha
            actual_sha="$(sha256sum "$out_file" | awk '{print toupper($1)}')"
            if [[ "$actual_sha" == "$expected_sha" ]]; then
                msg_ok "$id already up to date (cached)."
                echo "$out_file"
                return 0
            fi
        else
            msg_ok "$id already up to date (cached, no hash to verify)."
            echo "$out_file"
            return 0
        fi
    fi

    msg_info "Downloading $id..."
    curl -fL --retry 3 --progress-bar -o "$out_file" "$url" || {
        msg_err "Download failed: $url"
        exit 1
    }

    if [[ -n "$expected_sha" ]]; then
        local actual_sha
        actual_sha="$(sha256sum "$out_file" | awk '{print toupper($1)}')"
        if [[ "$actual_sha" != "$expected_sha" ]]; then
            msg_err "SHA-256 mismatch for $id. Expected: $expected_sha Got: $actual_sha"
            exit 1
        fi
    fi

    echo "$version_tag" > "${cache_dir}/version.txt"
    msg_ok "$id downloaded."
    echo "$out_file"
}

# ── Install operation helpers ─────────────────────────────────────────────────

# Copy all contents of $src_dir into $dst_dir (recursive merge)
copy_dir_contents() {
    local src="$1" dst="$2"
    if [[ ! -d "$src" ]]; then
        msg_err "Source directory not found: $src"
        exit 1
    fi
    mkdir -p "$dst"
    cp -r "$src"/. "$dst"/
}

# Find first subdirectory of $dir
first_subdir() {
    local dir="$1"
    find "$dir" -mindepth 1 -maxdepth 1 -type d | sort | head -1
}

# ── Artifact downloaders & installers ────────────────────────────────────────

install_exploit() {
    local exploit="$1"
    local usb="$2"

    case "$exploit" in
        "BadUpdate")
            local json; json="$(gh_release_json grimdoomer Xbox360BadUpdate)"
            local url; url="$(gh_asset_url "$json")"
            [[ -z "$url" ]] && { msg_err "Could not resolve download URL for BadUpdate."; exit 1; }
            local tag; tag="$(gh_version_tag "$json")"
            local filename; filename="$(basename "$url")"
            local sha; sha="$(sha_from_json "$json")"
            local archive; archive="$(download_artifact "exploit-badupdate" "$url" "$filename" "$tag" "$sha")"
            local stage="${STAGING_DIR}/exploit-badupdate"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            mkdir -p "${usb}/Rock Band Blitz"
            copy_dir_contents "$stage" "${usb}/Rock Band Blitz"
            msg_ok "BadUpdate exploit installed."
            ;;

        "ABadAvatar")
            local json; json="$(gh_release_json shutterbug2000 ABadAvatar)"
            local url; url="$(gh_asset_url "$json")"
            [[ -z "$url" ]] && { msg_err "Could not resolve download URL for ABadAvatar."; exit 1; }
            local tag; tag="$(gh_version_tag "$json")"
            local filename; filename="$(basename "$url")"
            local sha; sha="$(sha_from_json "$json")"
            local archive; archive="$(download_artifact "exploit-abadavatar" "$url" "$filename" "$tag" "$sha")"
            local stage="${STAGING_DIR}/exploit-abadavatar"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            copy_dir_contents "$stage" "$usb"
            msg_ok "ABadAvatar exploit installed."
            ;;

        "ABadAvatarUpdate")
            local json; json="$(gh_release_json bibarub Xbox360BadUpdate)"
            local url; url="$(gh_asset_url "$json")"
            [[ -z "$url" ]] && { msg_err "Could not resolve download URL for ABadAvatar 1.3."; exit 1; }
            local tag; tag="$(gh_version_tag "$json")"
            local filename; filename="$(basename "$url")"
            local sha; sha="$(sha_from_json "$json")"
            local archive; archive="$(download_artifact "exploit-abadavatarupdate" "$url" "$filename" "$tag" "$sha")"
            local stage="${STAGING_DIR}/exploit-abadavatarupdate"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            copy_dir_contents "$stage" "$usb"
            msg_ok "ABadAvatar 1.3 exploit installed."
            ;;
    esac
}

install_bootstrap() {
    local bootstrap="$1"
    local usb="$2"

    case "$bootstrap" in
        "XeUnshackle")
            local json; json="$(gh_release_json Byrom90 XeUnshackle)"
            local url; url="$(gh_asset_url "$json")"
            [[ -z "$url" ]] && { msg_err "Could not resolve download URL for XeUnshackle."; exit 1; }
            local tag; tag="$(gh_version_tag "$json")"
            local filename; filename="$(basename "$url")"
            local sha; sha="$(sha_from_json "$json")"
            local archive; archive="$(download_artifact "bootstrap-xeunshackle" "$url" "$filename" "$tag" "$sha")"
            local stage="${STAGING_DIR}/bootstrap-xeunshackle"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            # <SUBFOLDER>: copy first subdir's contents to USB root
            local subdir; subdir="$(first_subdir "$stage")"
            if [[ -z "$subdir" ]]; then
                copy_dir_contents "$stage" "$usb"
            else
                copy_dir_contents "$subdir" "$usb"
            fi
            msg_ok "XeUnshackle bootstrap installed."
            ;;

        "FreeMyXe")
            local json; json="$(gh_release_json InvoxiPlayGames FreeMyXe)"
            local url; url="$(gh_asset_url "$json")"
            [[ -z "$url" ]] && { msg_err "Could not resolve download URL for FreeMyXe."; exit 1; }
            local tag; tag="$(gh_version_tag "$json")"
            local filename; filename="$(basename "$url")"
            local sha; sha="$(sha_from_json "$json")"
            local archive; archive="$(download_artifact "bootstrap-freemyxe" "$url" "$filename" "$tag" "$sha")"
            local stage="${STAGING_DIR}/bootstrap-freemyxe"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            # Copy staging root into USB/BadUpdatePayload/
            mkdir -p "${usb}/BadUpdatePayload"
            copy_dir_contents "$stage" "${usb}/BadUpdatePayload"
            # Rename default.xex -> FreeMyXe.xex
            local src_xex="${usb}/BadUpdatePayload/default.xex"
            local dst_xex="${usb}/BadUpdatePayload/FreeMyXe.xex"
            if [[ -f "$src_xex" ]]; then
                mv "$src_xex" "$dst_xex"
            fi
            msg_ok "FreeMyXe bootstrap installed."
            ;;
    esac
}

install_badupdate_gamedata() {
    local usb="$1"
    local json; json="$(gh_release_json Pdawg-bytes BadBuilder v0.10a)"
    local url; url="$(gh_asset_url "$json" "GameData.zip")"
    local archive; archive="$(download_artifact "badupdate-gamedata" "$url" "GameData.zip" "v0.10a")"
    local stage="${STAGING_DIR}/badupdate-gamedata"
    rm -rf "$stage"; extract_archive "$archive" "$stage"
    copy_dir_contents "$stage" "$usb"
    msg_ok "Rock Band Blitz game data installed."
}

install_homebrew_entry() {
    local idx="$1"
    local usb="$2"
    local id="${HB_IDS[$idx]}"
    local name="${HB_NAMES[$idx]}"
    local src_path="${HB_SRC_PATH[$idx]}"

    # Resolve archive
    local archive=""
    case "$id" in
        "homebrew-aurora")
            local json; json="$(gh_release_json Pdawg-bytes BadBuilder v0.10a)"
            local url; url="$(gh_asset_url "$json" "Aurora.rar")"
            archive="$(download_artifact "homebrew-aurora" "$url" "Aurora.rar" "v0.10a")"
            local stage="${STAGING_DIR}/homebrew-aurora"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            # CopyDirectory: source=".", dest="Apps/Aurora"
            mkdir -p "${usb}/Apps/Aurora"
            copy_dir_contents "$stage" "${usb}/Apps/Aurora"
            msg_ok "Aurora installed to Apps/Aurora."
            ;;

        "homebrew-xexmenu")
            local json; json="$(gh_release_json Pdawg-bytes BadBuilder v0.10a)"
            local url; url="$(gh_asset_url "$json" "MenuData.7z")"
            archive="$(download_artifact "homebrew-xexmenu" "$url" "MenuData.7z" "v0.10a")"
            local stage="${STAGING_DIR}/homebrew-xexmenu"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            copy_dir_contents "$stage" "$usb"
            msg_ok "XeXMenu installed."
            ;;

        "homebrew-simple360nandflasher")
            local json; json="$(gh_release_json Pdawg-bytes BadBuilder v0.10a)"
            local url; url="$(gh_asset_url "$json" "Flasher.7z")"
            archive="$(download_artifact "homebrew-simple360nandflasher" "$url" "Flasher.7z" "v0.10a")"
            local stage="${STAGING_DIR}/homebrew-simple360nandflasher"
            rm -rf "$stage"; extract_archive "$archive" "$stage"
            # CopyDirectory: source="Simple 360 NAND Flasher", dest="Apps/Simple 360 NAND Flasher"
            local src_subdir="${stage}/Simple 360 NAND Flasher"
            if [[ -d "$src_subdir" ]]; then
                mkdir -p "${usb}/Apps/Simple 360 NAND Flasher"
                copy_dir_contents "$src_subdir" "${usb}/Apps/Simple 360 NAND Flasher"
            else
                mkdir -p "${usb}/Apps/Simple 360 NAND Flasher"
                copy_dir_contents "$stage" "${usb}/Apps/Simple 360 NAND Flasher"
            fi
            msg_ok "Simple 360 NAND Flasher installed."
            ;;

        homebrew-custom-*)
            # Custom homebrew provided by user
            if [[ -z "$src_path" || ! -f "$src_path" ]]; then
                msg_err "Custom homebrew archive not found: ${src_path:-<empty>}"
                return 1
            fi
            local display_name="$name"
            local stage="${STAGING_DIR}/${id}"
            rm -rf "$stage"; extract_archive "$src_path" "$stage"
            mkdir -p "${usb}/Apps/${display_name}"
            copy_dir_contents "$stage" "${usb}/Apps/${display_name}"
            msg_ok "Custom homebrew '${display_name}' installed."
            ;;
    esac
}

install_dashboard_update() {
    local usb="$1"
    local url="https://download.microsoft.com/download/8/f/4/8f456817-e264-4207-9b95-6efc990fee98/SystemUpdate_17559_USB.zip"
    local archive; archive="$(download_artifact "dashboard-update" "$url" "SystemUpdate_17559_USB.zip" "direct")"
    local stage="${STAGING_DIR}/dashboard-update"
    rm -rf "$stage"; extract_archive "$archive" "$stage"
    copy_dir_contents "$stage" "$usb"
    msg_ok "Dashboard update installed."
}

update_launch_ini() {
    local usb="$1"
    local hb_name="$2"
    local entry_path="$3"   # e.g. "Aurora.xex"

    local ini="${usb}/launch.ini"
    if [[ ! -f "$ini" ]]; then
        msg_warn "launch.ini not found; Dashlaunch auto-launch not configured."
        return
    fi

    local new_default="Usb:\\Apps\\${hb_name}\\${entry_path}"
    # Replace or append Default = line
    if grep -qi "^[[:space:]]*Default[[:space:]]*=" "$ini"; then
        sed -i "s|^[[:space:]]*[Dd][Ee][Ff][Aa][Uu][Ll][Tt][[:space:]]*=.*|Default = ${new_default}|" "$ini"
    else
        echo "Default = ${new_default}" >> "$ini"
    fi
    msg_ok "launch.ini updated: Default = ${new_default}"
}

# ── Menu: Configure drive ─────────────────────────────────────────────────────
menu_configure_drive() {
    render_header
    msg_info "Scanning for disks..."
    local disk_lines=()
    while IFS= read -r line; do
        disk_lines+=("$line")
    done < <(list_disks)

    if [[ ${#disk_lines[@]} -eq 0 ]]; then
        msg_err "No disks found."
        pause; return
    fi

    local labels=()
    for line in "${disk_lines[@]}"; do
        IFS='|' read -r did dname dsize dtype dpath <<< "$line"
        labels+=("${dname} (${dsize} GB) - ${dtype} [${dpath}]")
    done
    labels+=("Back")

    prompt_menu "Choose target drive  [ALL DATA WILL BE LOST]" "${labels[@]}"

    if (( REPLY_INDEX == ${#disk_lines[@]} )); then return; fi

    IFS='|' read -r TARGET_DISK_ID TARGET_DISK_NAME TARGET_DISK_SIZE_GB _ TARGET_DISK_PATH \
        <<< "${disk_lines[$REPLY_INDEX]}"
    msg_ok "Selected: ${TARGET_DISK_NAME} (${TARGET_DISK_PATH})"
    pause
}

# ── Menu: Configure exploit ───────────────────────────────────────────────────
menu_configure_exploit() {
    render_header
    prompt_menu "Choose exploit" \
        "BadUpdate        - Original exploit via Rock Band Blitz savedata" \
        "ABadAvatar       - Exploit targeting the avatar system" \
        "ABadAvatar 1.3   - Unofficial ABadAvatar with improved speed" \
        "Back"

    case $REPLY_INDEX in
        0) SELECTED_EXPLOIT="BadUpdate" ;;
        1) SELECTED_EXPLOIT="ABadAvatar" ;;
        2) SELECTED_EXPLOIT="ABadAvatarUpdate" ;;
        3) return ;;
    esac
    msg_ok "Exploit set to: $SELECTED_EXPLOIT"
    pause
}

# ── Menu: Configure bootstrap ─────────────────────────────────────────────────
menu_configure_bootstrap() {
    render_header
    prompt_menu "Choose post-exploit bootstrap" \
        "XeUnshackle  - Full payload: Dashlaunch, plugins, high compatibility" \
        "FreeMyXe     - Lightweight: essential patches, XeLL, LibXenon" \
        "Back"

    case $REPLY_INDEX in
        0) SELECTED_BOOTSTRAP="XeUnshackle" ;;
        1)
            SELECTED_BOOTSTRAP="FreeMyXe"
            if [[ -n "$LAUNCH_HB_ID" ]]; then
                LAUNCH_HB_ID=""
                msg_warn "Auto-launch cleared (requires XeUnshackle)."
            fi
            ;;
        2) return ;;
    esac
    msg_ok "Bootstrap set to: $SELECTED_BOOTSTRAP"
    pause
}

# ── Menu: Configure homebrew ──────────────────────────────────────────────────
_hb_index_by_id() {
    local target="$1"
    for i in "${!HB_IDS[@]}"; do
        [[ "${HB_IDS[$i]}" == "$target" ]] && { echo "$i"; return; }
    done
    echo "-1"
}

menu_homebrew_toggle() {
    render_header
    local labels=()
    for i in "${!HB_IDS[@]}"; do
        local state="[ON] "; [[ "${HB_ENABLED[$i]}" == "1" ]] || state="[OFF]"
        labels+=("${state} ${HB_NAMES[$i]}")
    done
    labels+=("Back")
    prompt_menu "Toggle homebrew packages" "${labels[@]}"
    if (( REPLY_INDEX < ${#HB_IDS[@]} )); then
        if [[ "${HB_ENABLED[$REPLY_INDEX]}" == "1" ]]; then
            HB_ENABLED[$REPLY_INDEX]="0"
            [[ "${HB_IDS[$REPLY_INDEX]}" == "$LAUNCH_HB_ID" ]] && LAUNCH_HB_ID=""
            msg_ok "Disabled: ${HB_NAMES[$REPLY_INDEX]}"
        else
            HB_ENABLED[$REPLY_INDEX]="1"
            msg_ok "Enabled: ${HB_NAMES[$REPLY_INDEX]}"
        fi
        pause
    fi
}

menu_add_custom_homebrew() {
    render_header
    echo -e "${ORANGE}Add custom homebrew archive${RESET}"
    read -rp "Archive path (.zip/.7z/.rar): " arc_path
    arc_path="${arc_path//\"/}"
    arc_path="${arc_path//\'/}"
    arc_path="$(eval echo "$arc_path")"  # expand ~

    if [[ ! -f "$arc_path" ]]; then
        msg_err "File not found: $arc_path"
        pause; return
    fi

    local display_name
    display_name="$(basename "${arc_path%.*}")"
    read -rp "Display name [${display_name}]: " input_name
    [[ -n "$input_name" ]] && display_name="$input_name"

    read -rp "Entry point .xex (relative path, empty to skip): " entry_xex

    local new_id="homebrew-custom-$(date +%s%N)"
    HB_IDS+=("$new_id")
    HB_NAMES+=("$display_name")
    HB_ENABLED+=("1")
    HB_ENTRY+=("$entry_xex")
    HB_SRC_PATH+=("$arc_path")
    msg_ok "Added: $display_name"
    pause
}

menu_remove_homebrew() {
    render_header
    local removable=()
    local removable_idx=()
    for i in "${!HB_IDS[@]}"; do
        removable+=("${HB_NAMES[$i]}")
        removable_idx+=("$i")
    done
    removable+=("Back")
    prompt_menu "Select homebrew to remove" "${removable[@]}"
    if (( REPLY_INDEX < ${#removable_idx[@]} )); then
        local real_idx="${removable_idx[$REPLY_INDEX]}"
        local rem_id="${HB_IDS[$real_idx]}"
        msg_ok "Removed: ${HB_NAMES[$real_idx]}"
        [[ "$rem_id" == "$LAUNCH_HB_ID" ]] && LAUNCH_HB_ID=""
        # Rebuild arrays without this entry
        local new_ids=() new_names=() new_en=() new_entry=() new_src=()
        for j in "${!HB_IDS[@]}"; do
            [[ "$j" == "$real_idx" ]] && continue
            new_ids+=("${HB_IDS[$j]}")
            new_names+=("${HB_NAMES[$j]}")
            new_en+=("${HB_ENABLED[$j]}")
            new_entry+=("${HB_ENTRY[$j]}")
            new_src+=("${HB_SRC_PATH[$j]}")
        done
        HB_IDS=("${new_ids[@]+"${new_ids[@]}"}")
        HB_NAMES=("${new_names[@]+"${new_names[@]}"}")
        HB_ENABLED=("${new_en[@]+"${new_en[@]}"}")
        HB_ENTRY=("${new_entry[@]+"${new_entry[@]}"}")
        HB_SRC_PATH=("${new_src[@]+"${new_src[@]}"}")
        pause
    fi
}

menu_set_launch_homebrew() {
    if [[ "$SELECTED_BOOTSTRAP" != "XeUnshackle" ]]; then
        msg_warn "Auto-launch requires XeUnshackle bootstrap."
        pause; return
    fi
    render_header
    local opts=()
    local opt_ids=()
    for i in "${!HB_IDS[@]}"; do
        [[ "${HB_ENABLED[$i]}" == "1" && -n "${HB_ENTRY[$i]}" ]] || continue
        opts+=("${HB_NAMES[$i]}")
        opt_ids+=("${HB_IDS[$i]}")
    done
    if [[ ${#opts[@]} -eq 0 ]]; then
        msg_warn "No homebrew with a known entry point is enabled."
        pause; return
    fi
    opts+=("Clear (none)" "Back")
    prompt_menu "Set default launch homebrew" "${opts[@]}"
    if (( REPLY_INDEX < ${#opt_ids[@]} )); then
        LAUNCH_HB_ID="${opt_ids[$REPLY_INDEX]}"
        msg_ok "Auto-launch: ${HB_NAMES[$(( REPLY_INDEX ))]}"
    elif [[ "${REPLY_VALUE}" == "Clear (none)" ]]; then
        LAUNCH_HB_ID=""
        msg_ok "Auto-launch cleared."
    fi
    pause
}

menu_configure_homebrew() {
    while true; do
        render_header
        show_summary
        prompt_menu "Homebrew configuration" \
            "Toggle packages    - Enable/disable built-in homebrew" \
            "Add custom         - Add a homebrew archive from disk" \
            "Remove entry       - Remove a homebrew entry" \
            "Set auto-launch    - Pick which app Dashlaunch opens (XeUnshackle only)" \
            "Back"
        case $REPLY_INDEX in
            0) menu_homebrew_toggle ;;
            1) menu_add_custom_homebrew ;;
            2) menu_remove_homebrew ;;
            3) menu_set_launch_homebrew ;;
            4) return ;;
        esac
    done
}

# ── Menu: Dashboard update ────────────────────────────────────────────────────
menu_update_dashboard() {
    render_header
    echo -e "${GRAY}BadUpdate requires dashboard version 2.0.17559.0.${RESET}"
    echo -e "${GRAY}Enable this only if your Xbox 360 is NOT already on that version.${RESET}"
    echo ""
    prompt_menu "Dashboard update" \
        "Enable  - Include the official 2.0.17559.0 update on the drive" \
        "Disable - Do not include update" \
        "Back"
    case $REPLY_INDEX in
        0) FIRMWARE_UPDATE=true;  msg_ok "Firmware update ENABLED." ;;
        1) FIRMWARE_UPDATE=false; msg_ok "Firmware update DISABLED." ;;
        2) return ;;
    esac
    pause
}

# ── Menu: Install ─────────────────────────────────────────────────────────────
menu_install() {
    render_header
    show_summary

    # Validate
    if [[ -z "$TARGET_DISK_PATH" ]]; then
        msg_err "No target drive selected."
        pause; return
    fi

    if [[ -n "$LAUNCH_HB_ID" && "$SELECTED_BOOTSTRAP" != "XeUnshackle" ]]; then
        msg_err "Auto-launch requires XeUnshackle bootstrap."
        pause; return
    fi

    echo -e "${ORANGE}${BOLD}WARNING:${RESET} All data on ${TARGET_DISK_NAME} (${TARGET_DISK_PATH}) will be erased."
    if ! confirm "Are you sure you want to format ${TARGET_DISK_PATH}?"; then
        return
    fi

    echo ""
    mkdir -p "$DOWNLOAD_DIR" "$STAGING_DIR"

    # 1. Format drive
    format_fat32 "$TARGET_DISK_PATH"
    echo ""

    # 2. Download & install artifacts
    if $FIRMWARE_UPDATE; then
        install_dashboard_update "$MOUNT_POINT"
    else
        install_exploit "$SELECTED_EXPLOIT" "$MOUNT_POINT"

        [[ "$SELECTED_EXPLOIT" == "BadUpdate" ]] && \
            install_badupdate_gamedata "$MOUNT_POINT"

        install_bootstrap "$SELECTED_BOOTSTRAP" "$MOUNT_POINT"

        # Homebrew
        for i in "${!HB_IDS[@]}"; do
            [[ "${HB_ENABLED[$i]}" == "1" ]] || continue
            install_homebrew_entry "$i" "$MOUNT_POINT"
        done

        # Configure Dashlaunch auto-launch
        if [[ -n "$LAUNCH_HB_ID" ]]; then
            local lhb_idx; lhb_idx="$(_hb_index_by_id "$LAUNCH_HB_ID")"
            if (( lhb_idx >= 0 )); then
                update_launch_ini "$MOUNT_POINT" \
                    "${HB_NAMES[$lhb_idx]}" \
                    "${HB_ENTRY[$lhb_idx]}"
            fi
        fi
    fi

    # 3. Write info files
    echo "USB Storage Device" > "${MOUNT_POINT}/name.txt"
    {
        echo "This drive was created with BadBuilder by Pdawg."
        echo "https://github.com/Pdawg-bytes/BadBuilder"
        echo ""
        echo "Configuration:"
        echo "  Exploit:    $SELECTED_EXPLOIT"
        echo "  Bootstrap:  $SELECTED_BOOTSTRAP"
        echo "  Firmware:   $FIRMWARE_UPDATE"
        echo "  Drive:      $TARGET_DISK_NAME ($TARGET_DISK_PATH)"
    } > "${MOUNT_POINT}/info.txt"

    # 4. Sync
    sync
    echo ""
    msg_ok "USB drive is ready! Mounted at: $MOUNT_POINT"
    echo -e "${GRAY}Safely eject with: umount ${MOUNT_POINT}${RESET}"
    pause "Press Enter to exit..."
    exit 0
}

# ── Main ──────────────────────────────────────────────────────────────────────
main() {
    # Elevation check
    if (( EUID != 0 )); then
        echo "This application must be run as root (sudo)."
        exit 1
    fi

    check_deps
    mkdir -p "$DOWNLOAD_DIR" "$STAGING_DIR"

    while true; do
        render_header
        show_summary

        if $FIRMWARE_UPDATE; then
            prompt_menu "Select an action" \
                "Target drive" \
                "Update Xbox Dashboard" \
                "Install" \
                "Exit"
            case $REPLY_INDEX in
                0) menu_configure_drive ;;
                1) menu_update_dashboard ;;
                2) menu_install ;;
                3) exit 0 ;;
            esac
        else
            prompt_menu "Select an action" \
                "Target drive" \
                "Exploit" \
                "Post-exploit bootstrap" \
                "Homebrew" \
                "Update Xbox Dashboard" \
                "Install" \
                "Exit"
            case $REPLY_INDEX in
                0) menu_configure_drive ;;
                1) menu_configure_exploit ;;
                2) menu_configure_bootstrap ;;
                3) menu_configure_homebrew ;;
                4) menu_update_dashboard ;;
                5) menu_install ;;
                6) exit 0 ;;
            esac
        fi
    done
}

main "$@"
