/** Small UI helpers shared by every page: formatting, toasts, dialogs, icons, escaping. */

export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

export function formatBytes(bytes) {
    if (bytes === null || bytes === undefined) return '—';
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB'];
    const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    const value = bytes / Math.pow(1024, i);
    return `${value >= 100 || i === 0 ? Math.round(value) : value.toFixed(1)} ${units[i]}`;
}

export function formatSpeed(bytesPerSecond) {
    return `${formatBytes(bytesPerSecond)}/s`;
}

export function formatEta(seconds) {
    if (!Number.isFinite(seconds) || seconds < 0) return '—';
    if (seconds < 60) return `${Math.ceil(seconds)}s`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
    return `${Math.floor(seconds / 3600)}h ${Math.round((seconds % 3600) / 60)}m`;
}

export function formatDate(iso) {
    if (!iso) return '—';
    const date = new Date(iso);
    return date.toLocaleString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit',
    });
}

/** Speed tracker: feed bytes, read out bytes/second over a rolling window. */
export class SpeedTracker {
    constructor(windowMs = 3000) {
        this.windowMs = windowMs;
        this.samples = [];
    }
    update(bytes) {
        const now = performance.now();
        this.samples.push({ time: now, bytes });
        while (this.samples.length > 0 && now - this.samples[0].time > this.windowMs) {
            this.samples.shift();
        }
    }
    get bytesPerSecond() {
        if (this.samples.length < 2) return 0;
        const first = this.samples[0];
        const last = this.samples[this.samples.length - 1];
        const seconds = (last.time - first.time) / 1000;
        if (seconds <= 0) return 0;
        return (last.bytes - first.bytes) / seconds;
    }
}

const ICON_COLORS = {
    image: 'var(--rs-purple)',
    video: 'var(--rs-amber)',
    audio: 'var(--rs-green)',
    archive: 'var(--rs-red)',
    document: 'var(--rs-cyan)',
    other: '#6d7f99',
};

const CATEGORY_BY_EXT = {
    image: ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp', 'svg', 'ico', 'tif', 'tiff'],
    document: ['pdf', 'doc', 'docx', 'txt', 'md', 'xls', 'xlsx', 'ppt', 'pptx', 'csv', 'rtf', 'odt', 'json', 'xml', 'yml', 'yaml'],
    video: ['mp4', 'avi', 'mkv', 'mov', 'webm', 'wmv', 'flv'],
    audio: ['mp3', 'wav', 'ogg', 'flac', 'm4a', 'aac'],
    archive: ['zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz'],
};

export function fileCategory(extension) {
    const ext = String(extension || '').toLowerCase().replace('.', '');
    for (const [category, extensions] of Object.entries(CATEGORY_BY_EXT)) {
        if (extensions.includes(ext)) return category;
    }
    return 'other';
}

/** Renders the small colored extension tile used in file cards and tables. */
export function fileIcon(extension) {
    const ext = String(extension || '').toLowerCase().replace('.', '') || '?';
    const category = fileCategory(ext);
    const label = ext.length > 4 ? `${ext.slice(0, 3)}…` : ext;
    return `<span class="rs-fileicon" style="background:${ICON_COLORS[category]}">${escapeHtml(label)}</span>`;
}

/* ------------------------------ toasts ------------------------------ */

let toastContainer = null;

export function toast(message, type = 'ok', timeout = 4200) {
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.className = 'toast-container position-fixed top-0 end-0 p-3';
        toastContainer.style.zIndex = '3000';
        document.body.appendChild(toastContainer);
    }

    const element = document.createElement('div');
    element.className = `toast rs-toast-${type === 'ok' ? 'ok' : 'err'}`;
    element.setAttribute('role', 'alert');
    element.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">${type === 'ok' ? '▚ ' : '✖ '}${escapeHtml(message)}</div>
            <button type="button" class="btn-close btn-close-white ms-auto me-2 m-auto" data-bs-dismiss="toast"></button>
        </div>`;
    toastContainer.appendChild(element);

    const bsToast = new bootstrap.Toast(element, { delay: timeout });
    bsToast.show();
    element.addEventListener('hidden.bs.toast', () => element.remove());
}

/* ------------------------------ dialogs ------------------------------ */

export function confirmDialog(title, message, { danger = false, confirmLabel = 'Confirm' } = {}) {
    return new Promise((resolve) => {
        const id = `rs-confirm-${Date.now()}`;
        const element = document.createElement('div');
        element.className = 'modal fade';
        element.id = id;
        element.tabIndex = -1;
        element.innerHTML = `
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">${escapeHtml(title)}</h5>
                        <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
                    </div>
                    <div class="modal-body">${message}</div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">Cancel</button>
                        <button type="button" class="btn ${danger ? 'btn-danger' : 'btn-primary'} btn-sm rs-confirm-ok">${escapeHtml(confirmLabel)}</button>
                    </div>
                </div>
            </div>`;
        document.body.appendChild(element);

        const modal = new bootstrap.Modal(element);
        let result = false;
        element.querySelector('.rs-confirm-ok').addEventListener('click', () => {
            result = true;
            modal.hide();
        });
        element.addEventListener('hidden.bs.modal', () => {
            element.remove();
            resolve(result);
        });
        modal.show();
    });
}

export function debounce(fn, wait = 300) {
    let timer;
    return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), wait);
    };
}
