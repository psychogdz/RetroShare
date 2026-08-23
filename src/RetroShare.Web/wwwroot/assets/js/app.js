/**
 * Shared application shell: auth guard, sidebar/topbar rendering, permission-aware
 * navigation and logout. Pages call await initShell() and receive the current user.
 */
import { api, Auth } from './api.js';
import { escapeHtml, formatBytes } from './ui.js';

const NAV = [
    { section: 'Files' },
    { href: '/dashboard.html', icon: '▦', label: 'Dashboard', permission: 'profile.view' },
    { href: '/files.html', icon: '▤', label: 'My Files', permission: 'files.view' },
    { href: '/shares.html', icon: '⇄', label: 'Share Links', permission: 'shares.view' },
    { href: '/trash.html', icon: '♲', label: 'Trash', permission: 'files.view' },
    { section: 'Account' },
    { href: '/activity.html', icon: '≡', label: 'Activity', permission: 'profile.view' },
    { href: '/profile.html', icon: '☺', label: 'Profile', permission: 'profile.view' },
    { section: 'System' },
    { href: '/admin.html', icon: '⚙', label: 'Admin Panel', permission: 'system.manage' },
];

export async function initShell(options = {}) {
    if (!Auth.isAuthenticated) {
        window.location.href = '/login.html';
        return null;
    }

    // Refresh the cached profile so permission changes apply immediately.
    let user = Auth.user;
    try {
        user = await api.me();
        Auth.setUser(user);
    } catch (err) {
        if (err.status !== 401 && err.status !== 403) {
            // Keep the cached profile if the server is briefly unavailable.
            if (!user) throw err;
        } else if (!user) {
            throw err;
        }
    }

    if (options.permission && !hasPermission(user, options.permission)) {
        document.body.innerHTML = `
            <div class="container py-5">
                <div class="rs-panel text-center py-5">
                    <h2>ACCESS DENIED</h2>
                    <p class="rs-filemeta">You do not have the "${escapeHtml(options.permission)}" permission.</p>
                    <a class="btn btn-secondary btn-sm" href="/dashboard.html">Back to dashboard</a>
                </div>
            </div>`;
        return user;
    }

    renderShell(user);
    return user;
}

export function hasPermission(user, permission) {
    return !!user?.permissions?.includes(permission);
}

function renderShell(user) {
    const mount = document.getElementById('shell');
    if (!mount) return;

    const permissions = new Set(user?.permissions || []);
    const current = location.pathname;
    const roleLabel = user?.roles?.length ? user.roles.join(', ') : 'user';

    const navHtml = NAV.map((item) => {
        if (item.section) {
            return `<div class="rs-section">${item.section}</div>`;
        }
        if (item.permission && !permissions.has(item.permission)) return '';
        const active = current === item.href ? ' active' : '';
        return `<a class="nav-link${active}" href="${item.href}"><span>${item.icon}</span>${item.label}</a>`;
    }).join('');

    const used = formatBytes(user?.storageUsedBytes ?? 0);
    const quota = formatBytes(user?.storageQuotaBytes ?? 0);
    const percent = user?.storageQuotaBytes
        ? Math.min(100, (user.storageUsedBytes / user.storageQuotaBytes) * 100) : 0;
    const meterClass = percent >= 90 ? 'full' : percent >= 70 ? 'warn' : '';

    mount.classList.add('rs-shell');
    mount.innerHTML = `
        <aside class="rs-sidebar" id="sidebar">
            <a class="rs-logo" href="/dashboard.html"><span class="rs-block"></span>RetroShare</a>
            <nav class="nav flex-column mt-3">${navHtml}</nav>
            <div class="mt-auto pt-3">
                <div class="rs-panel p-2">
                    <div class="rs-filemeta mb-1">STORAGE</div>
                    <div class="rs-meter mb-1"><div class="rs-meter-fill ${meterClass}" style="width:${percent.toFixed(1)}%"></div></div>
                    <div class="rs-filemeta">${used} / ${quota}</div>
                </div>
            </div>
        </aside>
        <div class="rs-main">
            <header class="rs-topbar">
                <div class="d-flex align-items-center gap-2">
                    <button class="btn btn-secondary btn-sm rs-burger" id="sidebar-toggle" aria-label="Menu">▤</button>
                    <span class="rs-filemeta">logged in as <strong style="color:#e8f2ff">${escapeHtml(user?.username ?? '')}</strong> <span class="rs-badge-dim badge">${escapeHtml(roleLabel)}</span></span>
                </div>
                <div class="d-flex gap-2">
                    <a class="btn btn-secondary btn-sm" href="/profile.html">Profile</a>
                    <button class="btn btn-danger btn-sm" id="logout-btn">Log out</button>
                </div>
            </header>
            <div id="page"></div>
        </div>`;

    mount.querySelector('#logout-btn')?.addEventListener('click', async () => {
        await api.logout();
        Auth.clear();
        window.location.href = '/login.html';
    });

    const toggle = mount.querySelector('#sidebar-toggle');
    toggle?.addEventListener('click', () => {
        const sidebar = document.getElementById('sidebar');
        const open = sidebar.classList.toggle('open');
        let backdrop = document.querySelector('.rs-sidebar-backdrop');
        if (open) {
            backdrop = document.createElement('div');
            backdrop.className = 'rs-sidebar-backdrop';
            document.body.appendChild(backdrop);
            backdrop.addEventListener('click', () => toggle.click());
        } else {
            backdrop?.remove();
        }
    });
}
