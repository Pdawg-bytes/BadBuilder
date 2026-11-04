#!/usr/bin/env bash
# ==============================================================================
# BadBuilder v3.0.0 - Xbox 360 BadUpdate USB Builder
# Linux native shell version (no .NET required)
# Fork: https://github.com/raphaelmaria/BadBuilder
# ==============================================================================
set -euo pipefail
IFS=$'\n\t'

# ── Colours ───────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
DKGREEN='\033[0;32m'
ORANGE='\033[38;5;208m'
YELLOW='\033[1;33m'
GRAY='\033[0;90m'
BOLD='\033[1m'
RESET='\033[0m'

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)"
WORK_DIR="${SCRIPT_DIR}/Work"
DOWNLOAD_DIR="${WORK_DIR}/Downloads"
STAGING_DIR="${WORK_DIR}/Staging"

# ── Content source ────────────────────────────────────────────────────────────
CONTENT_URL="https://archive.org/download/abapendrive.-7z/ABApendrive.7z"

# ── Config state ──────────────────────────────────────────────────────────────
TARGET_DISK_ID=""
TARGET_DISK_NAME=""
TARGET_DISK_SIZE_GB=""
TARGET_DISK_PATH=""
MOUNT_POINT=""

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
    echo -e "${GREEN}──────────────────────────────────────────────────────────v3.0.0${RESET}"
    echo -e "────────────────────Xbox 360 ${ORANGE}BadUpdate${RESET} USB Builder──────────────────────"
    echo -e "                          ${GRAY}Created by Pdawg | Fork raphaelmaria${RESET}"
    echo -e "${GREEN}──────────────────────────────────────────────────────────────────────${RESET}"
    echo ""
}

show_summary() {
    echo -e "┌──────────────────────────────────────────────────────────┐"
    printf  "│  %-16s  %-38s│\n" "Drive" \
        "${TARGET_DISK_NAME:-Nenhum selecionado} ${TARGET_DISK_PATH:+($TARGET_DISK_PATH)}"
    printf  "│  %-16s  %-38s│\n" "Fonte" "archive.org/ABApendrive"
    echo -e "└──────────────────────────────────────────────────────────┘"
    echo ""
}

# All user-facing messages go to stderr so they never corrupt $() captures
msg_info()  { echo -e "${YELLOW}[*]${RESET} $*" >&2; }
msg_ok()    { echo -e "${GREEN}[+]${RESET} $*" >&2; }
msg_warn()  { echo -e "${ORANGE}[!]${RESET} $*" >&2; }
msg_err()   { echo -e "${RED}[-]${RESET} $*" >&2; }

pause() {
    local msg="${1:-Pressione Enter para continuar...}"
    echo -e "\n${GRAY}${msg}${RESET}"
    read -r _
}

# Numbered selection menu → sets REPLY_INDEX (0-based). Returns 255 on last option (Back).
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
        read -rp "Selecione [1-${#options[@]}]: " choice
        if [[ "$choice" =~ ^[0-9]+$ ]] && (( choice >= 1 && choice <= ${#options[@]} )); then
            REPLY_INDEX=$(( choice - 1 ))
            return 0
        fi
        echo -e "${RED}Opção inválida.${RESET}"
    done
}

confirm() {
    while true; do
        read -rp "${1} [s/N]: " yn
        case "${yn,,}" in
            s|sim|y|yes) return 0 ;;
            n|nao|não|no|"") return 1 ;;
        esac
    done
}

# ── Dependency check ──────────────────────────────────────────────────────────
check_deps() {
    local missing=()
    for cmd in lsblk parted mkfs.fat mount umount curl 7z; do
        command -v "$cmd" &>/dev/null || missing+=("$cmd")
    done
    if [[ ${#missing[@]} -gt 0 ]]; then
        msg_err "Dependências ausentes:"
        for dep in "${missing[@]}"; do echo "    - $dep"; done
        echo ""
        echo "Debian/Ubuntu: sudo apt install util-linux dosfstools parted curl p7zip-full"
        echo "Fedora/RHEL:   sudo dnf install util-linux dosfstools parted curl p7zip p7zip-plugins"
        exit 1
    fi
}

# ── Disk helpers ──────────────────────────────────────────────────────────────
list_disks() {
    lsblk -J -d -b -o NAME,SIZE,TYPE,TRAN,MODEL,RM 2>/dev/null | \
    jq -r '.blockdevices[] |
        select(.type == "disk") |
        select(.name | test("^loop|^sr|^fd"; "i") | not) |
        [
          .name,
          (.model // .name | ltrimstr(" ") | rtrimstr(" ")),
          ((.size // 0) | tonumber / 1073741824 | . * 100 | round / 100 | tostring),
          (if .rm == true or .rm == 1 or (.rm | tostring) == "1" or .tran == "usb" then "Removível" else "Fixo" end),
          ("/dev/" + .name)
        ] | join("|")'
}

umount_partitions() {
    local dev="$1"
    local devname; devname="$(basename "$dev")"
    [[ -f /proc/mounts ]] || return 0
    while IFS=' ' read -r mdev mpoint _rest; do
        local mname; mname="$(basename "$mdev")"
        if [[ "$mname" == "${devname}"* && "$mname" != "$devname" ]]; then
            umount "$mdev" 2>/dev/null || true
        fi
    done < /proc/mounts
}

format_fat32() {
    local dev="$1"
    local devname; devname="$(basename "$dev")"

    msg_info "Desmontando partições em $dev..."
    umount_partitions "$dev"

    msg_info "Escrevendo tabela de partições..."
    parted -s "$dev" mklabel msdos
    parted -s "$dev" mkpart primary fat32 1MiB 100%
    parted -s "$dev" set 1 lba on

    partprobe "$dev" 2>/dev/null || blockdev --rereadpt "$dev" 2>/dev/null || true
    udevadm settle 2>/dev/null || sleep 2

    local part=""
    for _try in $(seq 1 10); do
        if   [[ -b "${dev}1"  ]]; then part="${dev}1";  break
        elif [[ -b "${dev}p1" ]]; then part="${dev}p1"; break
        fi
        sleep 0.5
    done
    [[ -n "$part" ]] || { msg_err "Partição ${dev}1 não apareceu."; exit 1; }

    msg_info "Formatando $part como FAT32..."
    mkfs.fat -F 32 -n "BADUPDATE" "$part"

    MOUNT_POINT="/tmp/badbuilder_${devname}"
    umount "$MOUNT_POINT" 2>/dev/null || true
    mkdir -p "$MOUNT_POINT"
    mount "$part" "$MOUNT_POINT"
    msg_ok "Drive formatado e montado em $MOUNT_POINT"
}

# ── File helpers ──────────────────────────────────────────────────────────────
first_subdir() {
    find "$1" -mindepth 1 -maxdepth 1 -type d | sort | head -1
}

copy_dir_contents() {
    local src="$1" dst="$2"
    [[ -d "$src" ]] || { msg_err "Diretório de origem não encontrado: $src"; exit 1; }
    mkdir -p "$dst"
    cp -r "$src"/. "$dst"/
}

eject_drive() {
    local dev="$1"
    local mnt="$2"

    msg_info "Sincronizando dados no disco..."
    sync

    msg_info "Desmontando o pendrive..."
    umount "$mnt" 2>/dev/null || true
    rmdir  "$mnt" 2>/dev/null || true
    MOUNT_POINT=""

    if command -v udisksctl &>/dev/null; then
        udisksctl power-off -b "$dev" 2>/dev/null && return
    fi
    if command -v eject &>/dev/null; then
        eject "$dev" 2>/dev/null && return
    fi
}

# ── Content downloader ────────────────────────────────────────────────────────
download_pendrive_content() {
    local usb="$1"
    local archive_file="${DOWNLOAD_DIR}/ABApendrive.7z"
    local stage="${STAGING_DIR}/pendrive"
    mkdir -p "$DOWNLOAD_DIR" "$STAGING_DIR"

    msg_info "Baixando conteúdo do pendrive..."
    msg_info "Fonte: ${CONTENT_URL}"
    if ! curl -fL --retry 3 --progress-bar -o "$archive_file" "$CONTENT_URL"; then
        msg_err "Falha no download. Verifique sua conexão com a internet."
        exit 1
    fi
    msg_ok "Download concluído."

    msg_info "Extraindo arquivos..."
    rm -rf "$stage"
    mkdir -p "$stage"
    if ! 7z x "$archive_file" -o"$stage" -y > /dev/null; then
        msg_err "Falha ao extrair o arquivo. O download pode estar corrompido."
        exit 1
    fi

    # If archive wraps content in a top-level folder, descend into it
    local root_dir; root_dir="$(first_subdir "$stage")"
    local src_dir="$stage"; [[ -n "$root_dir" ]] && src_dir="$root_dir"

    msg_ok "Copiando arquivos para o pendrive..."
    copy_dir_contents "$src_dir" "$usb"
}

# ── Menu: Configure drive ─────────────────────────────────────────────────────
menu_configure_drive() {
    render_header
    msg_info "Escaneando discos..."
    local disk_lines=()
    while IFS= read -r line; do
        disk_lines+=("$line")
    done < <(list_disks)

    if [[ ${#disk_lines[@]} -eq 0 ]]; then
        msg_err "Nenhum disco encontrado."
        pause; return
    fi

    local labels=()
    for line in "${disk_lines[@]}"; do
        IFS='|' read -r did dname dsize dtype dpath <<< "$line"
        labels+=("${dname} (${dsize} GB) - ${dtype} [${dpath}]")
    done
    labels+=("Voltar")

    prompt_menu "Selecione o drive alvo  [TODOS OS DADOS SERÃO APAGADOS]" "${labels[@]}"

    if (( REPLY_INDEX == ${#disk_lines[@]} )); then return; fi

    IFS='|' read -r TARGET_DISK_ID TARGET_DISK_NAME TARGET_DISK_SIZE_GB _ TARGET_DISK_PATH \
        <<< "${disk_lines[$REPLY_INDEX]}"
    msg_ok "Selecionado: ${TARGET_DISK_NAME} (${TARGET_DISK_PATH})"
    pause
}

# ── Menu: Install ─────────────────────────────────────────────────────────────
menu_install() {
    render_header
    show_summary

    if [[ -z "$TARGET_DISK_PATH" ]]; then
        msg_err "Nenhum drive selecionado."
        pause; return
    fi

    echo -e "${ORANGE}${BOLD}AVISO:${RESET} Todos os dados em ${TARGET_DISK_NAME} (${TARGET_DISK_PATH}) serão apagados."
    if ! confirm "Confirma a formatação de ${TARGET_DISK_PATH}?"; then
        return
    fi

    echo ""

    # 1. Format
    format_fat32 "$TARGET_DISK_PATH"
    echo ""

    # 2. Download & copy
    download_pendrive_content "$MOUNT_POINT"
    echo ""

    # 3. Info files
    echo "USB Storage Device" > "${MOUNT_POINT}/name.txt"
    {
        echo "This drive was created with BadBuilder by Pdawg."
        echo "https://github.com/raphaelmaria/BadBuilder"
        echo ""
        echo "Source: ${REPO_OWNER}/${REPO_NAME}/${REPO_CONTENT_PATH}"
        echo "Drive:  ${TARGET_DISK_NAME} (${TARGET_DISK_PATH})"
    } > "${MOUNT_POINT}/info.txt"

    # 4. Eject
    local _dev="$TARGET_DISK_PATH"
    local _mnt="$MOUNT_POINT"
    eject_drive "$_dev" "$_mnt"

    # 5. Success banner
    echo ""
    echo -e "${GREEN}┌──────────────────────────────────────────────────────────────┐${RESET}"
    echo -e "${GREEN}│                                                              │${RESET}"
    echo -e "${GREEN}│   ✓  PROCESSO CONCLUÍDO COM SUCESSO!                         │${RESET}"
    echo -e "${GREEN}│                                                              │${RESET}"
    echo -e "${GREEN}│   O pendrive está pronto para uso no Xbox 360.               │${RESET}"
    echo -e "${GREEN}│   O dispositivo foi desmontado — pode removê-lo com          │${RESET}"
    echo -e "${GREEN}│   segurança do computador.                                   │${RESET}"
    echo -e "${GREEN}│                                                              │${RESET}"
    printf  "${GREEN}│   Drive: %-52s│${RESET}\n" "$_dev"
    echo -e "${GREEN}│                                                              │${RESET}"
    echo -e "${GREEN}└──────────────────────────────────────────────────────────────┘${RESET}"
    echo ""
    pause "Pressione Enter para sair..."
    exit 0
}

# ── Main ──────────────────────────────────────────────────────────────────────
main() {
    if (( EUID != 0 )); then
        echo "Este script deve ser executado como root (sudo)."
        exit 1
    fi

    check_deps
    mkdir -p "$DOWNLOAD_DIR" "$STAGING_DIR"

    while true; do
        render_header
        show_summary

        prompt_menu "Selecione uma ação" \
            "Drive alvo" \
            "Instalar" \
            "Sair"

        case $REPLY_INDEX in
            0) menu_configure_drive ;;
            1) menu_install ;;
            2) exit 0 ;;
        esac
    done
}

main "$@"
