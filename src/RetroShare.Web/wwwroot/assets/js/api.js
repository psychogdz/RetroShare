/**
 * RetroShare REST client.
 * Keeps JWT access/refresh tokens in localStorage, transparently refreshes the
 * access token once on a 401 and retries the original request.
 */
export class ApiError extends Error {
    constructor(message, code, status, errors) {
        super(message);
        this.code = code || 'ERROR';
        this.status = status || 0;
        this.errors = errors || null;
    }
}

const ACCESS_KEY = 'rs_access';
const REFRESH_KEY = 'rs_refresh';
const USER_KEY = 'rs_user';

let refreshInFlight = null;

export const Auth = {
    get access() { return localStorage.getItem(ACCESS_KEY); },
    get refresh() { return localStorage.getItem(REFRESH_KEY); },
    get user() {
        try { return JSON.parse(localStorage.getItem(USER_KEY)); } catch { return null; }
    },
    setSession({ accessToken, refreshToken, user }) {
        localStorage.setItem(ACCESS_KEY, accessToken);
        localStorage.setItem(REFRESH_KEY, refreshToken);
        if (user) localStorage.setItem(USER_KEY, JSON.stringify(user));
    },
    setUser(user) { localStorage.setItem(USER_KEY, JSON.stringify(user)); },
    clear() {
        localStorage.removeItem(ACCESS_KEY);
        localStorage.removeItem(REFRESH_KEY);
        localStorage.removeItem(USER_KEY);
    },
    get isAuthenticated() { return !!localStorage.getItem(ACCESS_KEY); },
};

async function tryRefresh() {
    const token = Auth.refresh;
    if (!token) return false;

    if (!refreshInFlight) {
        refreshInFlight = (async () => {
            try {
                const res = await fetch('/api/auth/refresh', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ refreshToken: token }),
                });
                if (!res.ok) return false;
                const data = await res.json();
                Auth.setSession(data);
                return true;
            } catch {
                return false;
            } finally {
                setTimeout(() => { refreshInFlight = null; }, 0);
            }
        })();
    }

    return refreshInFlight;
}

async function request(path, { method = 'GET', body, retry = true, raw = false } = {}) {
    const headers = {};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (Auth.access) headers['Authorization'] = `Bearer ${Auth.access}`;

    let res;
    try {
        res = await fetch(path, {
            method,
            headers,
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
    } catch (err) {
        throw new ApiError('Network error — is the server running?', 'NETWORK_ERROR', 0);
    }

    if (res.status === 401 && retry && Auth.refresh && !path.startsWith('/api/auth/')) {
        const refreshed = await tryRefresh();
        if (refreshed) return request(path, { method, body, retry: false, raw });
        Auth.clear();
        window.location.href = '/login.html?expired=1';
        throw new ApiError('Session expired.', 'SESSION_EXPIRED', 401);
    }

    if (raw) return res;

    if (!res.ok) {
        let payload = {};
        try { payload = await res.json(); } catch { /* non-JSON error */ }
        throw new ApiError(
            payload.message || `Request failed (${res.status})`,
            payload.code,
            res.status,
            payload.errors);
    }

    if (res.status === 204) return null;
    return res.json();
}

export const api = {
    request,

    // ---- auth ----
    register: (body) => request('/api/auth/register', { method: 'POST', body }),
    login: (body) => request('/api/auth/login', { method: 'POST', body }),
    logout: () => Auth.refresh
        ? request('/api/auth/logout', { method: 'POST', body: { refreshToken: Auth.refresh } }).catch(() => null)
        : Promise.resolve(null),
    me: () => request('/api/auth/me'),

    // ---- dashboard / activity ----
    dashboard: () => request('/api/dashboard'),
    adminDashboard: () => request('/api/dashboard/admin'),
    activity: (page = 1, pageSize = 20) => request(`/api/activity?page=${page}&pageSize=${pageSize}`),
    activityAll: (qs = '') => request(`/api/activity/all${qs}`),

    // ---- files & folders ----
    files: (qs = '') => request(`/api/files${qs}`),
    file: (id) => request(`/api/files/${id}`),
    renameFile: (id, name) => request(`/api/files/${id}`, { method: 'PUT', body: { name } }),
    moveFile: (id, folderId) => request(`/api/files/${id}/move`, { method: 'POST', body: { folderId } }),
    deleteFile: (id, permanent = false) => request(`/api/files/${id}${permanent ? '?permanent=true' : ''}`, { method: 'DELETE' }),
    restoreFile: (id) => request(`/api/files/${id}/restore`, { method: 'POST' }),
    trash: (qs = '') => request(`/api/trash${qs}`),
    trashRestore: (id) => request(`/api/trash/${id}/restore`, { method: 'POST' }),
    trashDelete: (id) => request(`/api/trash/${id}`, { method: 'DELETE' }),

    folders: () => request('/api/folders'),
    folderContents: (qs = '') => request(`/api/folders/contents${qs}`),
    createFolder: (name, parentId) => request('/api/folders', { method: 'POST', body: { name, parentId } }),
    renameFolder: (id, name) => request(`/api/folders/${id}`, { method: 'PUT', body: { name } }),
    moveFolder: (id, parentId) => request(`/api/folders/${id}/move`, { method: 'POST', body: { parentId } }),
    deleteFolder: (id) => request(`/api/folders/${id}`, { method: 'DELETE' }),

    // ---- shares ----
    createShare: (fileId, body) => request(`/api/files/${fileId}/share`, { method: 'POST', body }),
    shares: (page = 1) => request(`/api/shares?page=${page}`),
    sharesAll: (page = 1) => request(`/api/shares/all?page=${page}`),
    revokeShare: (id) => request(`/api/shares/${id}`, { method: 'DELETE' }),
    shareInfo: (token) => request(`/api/shares/${encodeURIComponent(token)}`),

    // ---- profile ----
    profile: () => request('/api/profile'),
    updateProfile: (displayName) => request('/api/profile', { method: 'PUT', body: { displayName } }),
    changePassword: (currentPassword, newPassword) =>
        request('/api/profile/password', { method: 'POST', body: { currentPassword, newPassword } }),

    // ---- admin ----
    users: (qs = '') => request(`/api/users${qs}`),
    user: (id) => request(`/api/users/${id}`),
    updateUser: (id, body) => request(`/api/users/${id}`, { method: 'PUT', body }),
    setUserRoles: (id, roleIds) => request(`/api/users/${id}/roles`, { method: 'PUT', body: { roleIds } }),
    deleteUser: (id) => request(`/api/users/${id}`, { method: 'DELETE' }),

    roles: () => request('/api/roles'),
    role: (id) => request(`/api/roles/${id}`),
    createRole: (body) => request('/api/roles', { method: 'POST', body }),
    updateRole: (id, body) => request(`/api/roles/${id}`, { method: 'PUT', body }),
    deleteRole: (id) => request(`/api/roles/${id}`, { method: 'DELETE' }),
    permissions: () => request('/api/permissions'),

    systemMonitor: () => request('/api/system/monitor'),

    adminFiles: (qs = '') => request(`/api/files/all${qs}`),
};
