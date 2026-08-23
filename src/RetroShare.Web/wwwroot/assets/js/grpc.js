/**
 * Minimal gRPC-Web client for the RetroShare data plane (FileTransfer service).
 *
 * The service speaks standard gRPC-Web with protobuf frames; this module implements
 * just enough of the protobuf wire format (varints, length-delimited fields) for the
 * Upload/Download messages — no generated code, no build step.
 *
 * Wire contract (see src/RetroShare.Infrastructure/Protos/file_transfer.proto):
 *   UploadRequest  { UploadInit init = 1; bytes chunk = 2 }
 *   UploadInit     { string file_name = 1; int64 size = 2; string mime_type = 3; string folder_id = 4 }
 *   UploadResponse{ string file_id = 1; int64 total_bytes = 2 }
 *   DownloadRequest{ string file_id = 1; string share_token = 2; string share_password = 3 }
 *   DownloadResponse{ DownloadMeta meta = 1; bytes chunk = 2 }
 *   DownloadMeta   { string file_name = 1; int64 size = 2; string mime_type = 3 }
 */
import { Auth } from './api.js';

const SERVICE = '/filetransfer.FileTransfer';
const UPLOAD_CHUNK = 256 * 1024;

export class GrpcError extends Error {
    constructor(message, code) {
        super(message);
        this.grpcCode = code;
    }
}

/* ------------------------------ protobuf codec ------------------------------ */

function varint(value) {
    const out = [];
    let n = Math.floor(value);
    while (n > 0x7f) {
        out.push((n & 0x7f) | 0x80);
        n = Math.floor(n / 128);
    }
    out.push(n);
    return out;
}

function key(field, wireType) {
    return varint((field << 3) | wireType);
}

function lenDelimited(field, bytes) {
    const k = key(field, 2);
    const len = varint(bytes.length);
    return new Uint8Array([...k, ...len, ...bytes]);
}

const encoder = new TextEncoder();
const strField = (field, value) => value ? lenDelimited(field, encoder.encode(value)) : new Uint8Array(0);
const bytesField = (field, value) => value && value.length ? lenDelimited(field, value) : new Uint8Array(0);
const int64Field = (field, value) => new Uint8Array([...key(field, 0), ...varint(value)]);
const msgField = (field, encoded) => lenDelimited(field, encoded);

/** Parses length-delimited fields of a message into {fieldNumber: Uint8Array} pairs. */
function* parseFields(buf) {
    let pos = 0;
    const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    const readVarint = () => {
        let result = 0, shift = 0, byte;
        do {
            byte = buf[pos++];
            result += (byte & 0x7f) * Math.pow(2, shift);
            shift += 7;
        } while (byte & 0x80);
        return result;
    };

    while (pos < buf.length) {
        const tag = readVarint();
        const field = tag >>> 3;
        const wireType = tag & 7;
        if (wireType === 0) {
            const value = readVarint();
            yield { field, value };
        } else if (wireType === 2) {
            const len = readVarint();
            yield { field, bytes: buf.subarray(pos, pos + len) };
            pos += len;
        } else if (wireType === 5) {
            yield { field, bytes: buf.subarray(pos, pos + 4) };
            pos += 4;
        } else if (wireType === 1) {
            yield { field, bytes: buf.subarray(pos, pos + 8) };
            pos += 8;
        } else {
            throw new GrpcError(`Unsupported protobuf wire type ${wireType}`, 'PROTO');
        }
    }
}

const decoder = new TextDecoder();

function parseUploadResponse(buf) {
    const out = {};
    for (const f of parseFields(buf)) {
        if (f.field === 1 && f.bytes) out.fileId = decoder.decode(f.bytes);
        if (f.field === 2) out.totalBytes = f.value;
    }
    return out;
}

function parseDownloadResponse(buf) {
    const out = {};
    for (const f of parseFields(buf)) {
        if (f.field === 1 && f.bytes) {
            const meta = {};
            for (const m of parseFields(f.bytes)) {
                if (m.field === 1 && m.bytes) meta.fileName = decoder.decode(m.bytes);
                if (m.field === 2) meta.size = m.value;
                if (m.field === 3 && m.bytes) meta.mimeType = decoder.decode(m.bytes);
            }
            out.meta = meta;
        }
        if (f.field === 2 && f.bytes) out.chunk = f.bytes;
    }
    return out;
}

/* ------------------------------ gRPC framing ------------------------------ */

/** Wraps a protobuf payload in a gRPC-Web data frame: [flag=0x00][len:u32be][payload]. */
function frame(payload) {
    const out = new Uint8Array(5 + payload.length);
    out[0] = 0;
    new DataView(out.buffer).setUint32(1, payload.length, false);
    out.set(payload, 5);
    return out;
}

function headers(withAuth) {
    const h = {
        'Content-Type': 'application/grpc-web+proto',
        'X-Grpc-Web': '1',
    };
    if (withAuth && Auth.access) h['Authorization'] = `Bearer ${Auth.access}`;
    return h;
}

/** Reads a gRPC-Web response body: yields data frames' payloads, returns trailers text. */
async function readResponse(res, onData) {
    const reader = res.body.getReader();
    let buffer = new Uint8Array(0);
    let trailers = '';

    const append = (chunk) => {
        const merged = new Uint8Array(buffer.length + chunk.length);
        merged.set(buffer);
        merged.set(chunk, buffer.length);
        buffer = merged;
    };

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        append(value);

        while (buffer.length >= 5) {
            const flags = buffer[0];
            const len = new DataView(buffer.buffer, buffer.byteOffset).getUint32(1, false);
            if (buffer.length < 5 + len) break;
            const payload = buffer.subarray(5, 5 + len);
            buffer = buffer.subarray(5 + len);

            if (flags & 0x80) {
                trailers = decoder.decode(payload);
            } else {
                onData?.(payload);
            }
        }
    }

    return trailers;
}

function checkTrailers(trailers) {
    const match = trailers.match(/grpc-status\s*:\s*(\d+)/i);
    const status = match ? Number(match[1]) : 2; // 2 = UNKNOWN
    if (status === 0) return;

    const messageMatch = trailers.match(/grpc-message\s*:\s*(.+)/i);
    let message = messageMatch ? messageMatch[1].trim() : 'Transfer failed.';
    try { message = decodeURIComponent(message); } catch { /* keep raw */ }
    throw new GrpcError(message, status);
}

/* ------------------------------ public API ------------------------------ */

/**
 * Streams a File to the server over gRPC-Web upload, reporting progress.
 * Resolves with { fileId, totalBytes }.
 */
export async function uploadFile(file, { folderId = null, onProgress, signal } = {}) {
    const init =
        msgField(1, new Uint8Array([
            ...strField(1, file.name),
            ...int64Field(2, file.size),
            ...strField(3, file.type || 'application/octet-stream'),
            ...strField(4, folderId || ''),
        ]));

    let offset = 0;

    // gRPC-Web request bodies must be buffered: browsers can only stream a
    // request body over HTTP/2, and dev origins are HTTP/1.1. The framing is
    // unchanged (one message per 256 KiB chunk), so the server still parses
    // and persists the upload incrementally under quota control.
    const frames = [frame(init)];
    onProgress?.(0, file.size);
    while (offset < file.size) {
        const slice = file.slice(offset, Math.min(offset + UPLOAD_CHUNK, file.size));
        const buf = await slice.arrayBuffer();
        const chunk = new Uint8Array(buf);
        offset += chunk.length;
        frames.push(frame(bytesField(2, chunk)));
        onProgress?.(offset, file.size);
        if (signal?.aborted) throw new DOMException('Upload cancelled.', 'AbortError');
    }

    const res = await fetch(`${SERVICE}/Upload`, {
        method: 'POST',
        headers: headers(true),
        body: new Blob(frames),
        signal,
    });

    if (!res.ok && res.body === null) {
        throw new GrpcError(`Upload failed with HTTP ${res.status}`, 'HTTP');
    }

    let response = null;
    const trailers = await readResponse(res, (payload) => {
        response = parseUploadResponse(payload);
    });
    checkTrailers(trailers);
    if (!response?.fileId) throw new GrpcError('Upload completed without a file id.', 'PROTO');
    return response;
}

/**
 * Downloads a file (by id when authenticated, or by share token anonymously) over
 * gRPC-Web streaming and triggers a browser save dialog. Reports progress.
 */
export async function downloadFile({ fileId = null, shareToken = null, sharePassword = null, onProgress, signal } = {}) {
    const request = new Uint8Array([
        ...strField(1, fileId || ''),
        ...strField(2, shareToken || ''),
        ...strField(3, sharePassword || ''),
    ]);

    const res = await fetch(`${SERVICE}/Download`, {
        method: 'POST',
        headers: headers(!shareToken), // authenticated owner download vs anonymous share
        body: frame(request),
        signal,
    });

    let meta = null;
    let received = 0;
    const parts = [];

    const trailers = await readResponse(res, (payload) => {
        const message = parseDownloadResponse(payload);
        if (message.meta) {
            meta = message.meta;
            return;
        }
        if (message.chunk) {
            parts.push(message.chunk);
            received += message.chunk.length;
            if (onProgress && meta) onProgress(received, meta.size);
        }
    });

    checkTrailers(trailers);
    if (!meta) throw new GrpcError('Download response missing metadata.', 'PROTO');

    const blob = new Blob(parts, { type: meta.mimeType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = meta.fileName || 'download';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);

    return { fileName: meta.fileName, size: received };
}
