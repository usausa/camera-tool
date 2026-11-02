// WebSocket message header (little endian), see StreamProtocol.cs
const HEADER_VERSION = 1;

const FACE_SECTION_FLAG = 0x1;

const FACE_SECTION_OFFSET = 32;

const FACE_OFFSET = 40;

const FACE_LENGTH = 20;

const BOX_COLOR = '#00e676';

const STATS_INTERVAL = 1000;

const RECONNECT_DELAY = 1000;

// The checkbox is not synced with the server for a while after it is changed
const SYNC_DELAY = 2000;

const canvas = document.getElementById('canvas');
const context = canvas.getContext('2d');
const image = document.getElementById('image');
const state = document.getElementById('state');
const detection = document.getElementById('detection');

// Unix time in milliseconds with sub-millisecond resolution
const now = () => performance.timeOrigin + performance.now();

//--------------------------------------------------------------------------------
// Statistics
//--------------------------------------------------------------------------------

class Counter {
    constructor() {
        this.reset();
    }

    add(value) {
        this.total += value;
        this.max = Math.max(this.max, value);
        this.count++;
    }

    take() {
        const result = this.count > 0 ? { average: this.total / this.count, max: this.max } : null;
        this.reset();
        return result;
    }

    reset() {
        this.total = 0;
        this.max = 0;
        this.count = 0;
    }
}

const client = {
    received: 0,
    drawn: 0,
    dropped: 0,
    bytes: 0,
    width: 0,
    height: 0,
    faces: null,
    receiveLatency: new Counter(),
    publishLatency: new Counter(),
    drawLatency: new Counter(),
    decodeTime: new Counter(),
    detectionLag: new Counter()
};

function createRows(id, items) {
    const list = document.getElementById(id);
    const rows = {};
    for (const [key, label] of items) {
        const term = document.createElement('dt');
        term.textContent = label;
        const value = document.createElement('dd');
        value.textContent = '-';
        list.append(term, value);
        rows[key] = value;
    }
    return rows;
}

const clientRows = createRows('client', [
    ['mode', 'Mode'],
    ['resolution', 'Resolution'],
    ['received', 'Received'],
    ['drawn', 'Drawn'],
    ['dropped', 'Dropped'],
    ['receiveLatency', 'Capture → receive'],
    ['publishLatency', 'Publish → receive'],
    ['drawLatency', 'Capture → draw'],
    ['decodeTime', 'Decode'],
    ['size', 'Frame size'],
    ['bitrate', 'Bitrate'],
    ['faces', 'Faces'],
    ['detectionLag', 'Detection lag']
]);

const serverRows = createRows('server', [
    ['camera', 'Camera'],
    ['format', 'Format'],
    ['clients', 'Clients'],
    ['capture', 'Capture'],
    ['encode', 'Encode'],
    ['send', 'Send'],
    ['skip', 'Skip'],
    ['captureLatency', 'Capture latency'],
    ['convertTime', 'Convert'],
    ['encodeTime', 'Encode time'],
    ['streamLatency', 'Arrival → publish'],
    ['sendStartLatency', 'Publish → send start'],
    ['sentLatency', 'Publish → sent'],
    ['jpegSize', 'JPEG size'],
    ['buffers', 'Frame buffers'],
    ['detection', 'Detection'],
    ['detect', 'Detect'],
    ['preprocessTime', 'Preprocess'],
    ['inferenceTime', 'Inference'],
    ['faces', 'Faces'],
    ['cpu', 'CPU'],
    ['allocated', 'Allocated'],
    ['gc', 'GC 0/1/2']
]);

const formatFps = (value) => `${value.toFixed(1)} fps`;

const formatTime = (value, digits = 2) => (value ? `${value.average.toFixed(digits)} / ${value.max.toFixed(digits)} ms` : '-');

const formatFrames = (value) => (value ? `${value.average.toFixed(1)} / ${value.max} frames` : '-');

let lastStatsTime = now();

let detectionSyncTime = 0;

function updateClientStats() {
    const time = now();
    const elapsed = (time - lastStatsTime) / 1000;
    lastStatsTime = time;

    const receiveLatency = client.receiveLatency.take();
    const publishLatency = client.publishLatency.take();
    const drawLatency = client.drawLatency.take();
    const decodeTime = client.decodeTime.take();
    const detectionLag = client.detectionLag.take();
    if (mode === 'ws') {
        clientRows.mode.textContent = 'WebSocket';
        clientRows.resolution.textContent = client.width > 0 ? `${client.width}x${client.height}` : '-';
        clientRows.received.textContent = formatFps(client.received / elapsed);
        clientRows.drawn.textContent = formatFps(client.drawn / elapsed);
        clientRows.dropped.textContent = formatFps(client.dropped / elapsed);
        clientRows.receiveLatency.textContent = formatTime(receiveLatency);
        clientRows.publishLatency.textContent = formatTime(publishLatency);
        clientRows.drawLatency.textContent = formatTime(drawLatency);
        clientRows.decodeTime.textContent = formatTime(decodeTime);
        clientRows.size.textContent = client.received > 0 ? `${(client.bytes / client.received / 1024).toFixed(1)} KB` : '-';
        clientRows.bitrate.textContent = `${((client.bytes * 8) / elapsed / 1000000).toFixed(2)} Mbps`;
        clientRows.faces.textContent = client.faces !== null ? `${client.faces}` : '-';
        clientRows.detectionLag.textContent = formatFrames(detectionLag);
    } else {
        // Not measurable with an img element
        clientRows.mode.textContent = 'MJPEG';
        clientRows.resolution.textContent = image.naturalWidth > 0 ? `${image.naturalWidth}x${image.naturalHeight}` : '-';
        for (const key of ['received', 'drawn', 'dropped', 'receiveLatency', 'publishLatency', 'drawLatency', 'decodeTime', 'size', 'bitrate', 'faces', 'detectionLag']) {
            clientRows[key].textContent = '-';
        }
    }

    client.received = 0;
    client.drawn = 0;
    client.dropped = 0;
    client.bytes = 0;
}

async function updateServerStats() {
    let status;
    try {
        const response = await fetch('/api/status', { cache: 'no-store' });
        if (!response.ok) {
            return;
        }
        status = await response.json();
    } catch {
        // Server not reachable
        return;
    }

    serverRows.camera.textContent = status.running ? (status.device ?? 'Running') : (status.error ?? 'Stopped');
    serverRows.format.textContent = status.format ?? '-';
    serverRows.clients.textContent = `${status.clients}`;
    serverRows.capture.textContent = formatFps(status.captureFps);
    serverRows.encode.textContent = formatFps(status.encodeFps);
    serverRows.send.textContent = formatFps(status.sendFps);
    serverRows.skip.textContent = formatFps(status.skipFps);
    serverRows.captureLatency.textContent = formatTime(status.captureLatency);
    serverRows.convertTime.textContent = formatTime(status.convertTime, 3);
    serverRows.encodeTime.textContent = formatTime(status.encodeTime);
    serverRows.streamLatency.textContent = formatTime(status.streamLatency);
    serverRows.sendStartLatency.textContent = formatTime(status.sendStartLatency);
    serverRows.sentLatency.textContent = formatTime(status.sentLatency);
    serverRows.jpegSize.textContent = `${status.jpegSize.toFixed(1)} KB`;
    serverRows.buffers.textContent = `${status.buffers}`;
    serverRows.detection.textContent = status.detectionError ?? (status.detectionEnabled ? 'On' : 'Off');
    serverRows.detect.textContent = formatFps(status.detectFps);
    serverRows.preprocessTime.textContent = formatTime(status.preprocessTime, 3);
    serverRows.inferenceTime.textContent = formatTime(status.inferenceTime);
    serverRows.faces.textContent = `${status.faces}`;
    serverRows.cpu.textContent = `${status.cpuUsage.toFixed(2)} %`;
    serverRows.allocated.textContent = `${status.allocated.toFixed(1)} KB/s`;
    serverRows.gc.textContent = `${status.gc0.toFixed(1)} / ${status.gc1.toFixed(1)} / ${status.gc2.toFixed(1)}`;

    if (now() > detectionSyncTime) {
        detection.checked = status.detectionEnabled;
    }
}

//--------------------------------------------------------------------------------
// Detection
//--------------------------------------------------------------------------------

detection.addEventListener('change', async () => {
    detectionSyncTime = now() + SYNC_DELAY;
    try {
        await fetch('/api/detection', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled: detection.checked })
        });
    } catch {
        // Server not reachable
    }
});

function readFaces(view) {
    const count = view.getUint16(FACE_SECTION_OFFSET + 4, true);
    const boxes = [];
    for (let i = 0; i < count; i++) {
        const offset = FACE_OFFSET + (i * FACE_LENGTH);
        boxes.push({
            left: view.getFloat32(offset, true),
            top: view.getFloat32(offset + 4, true),
            right: view.getFloat32(offset + 8, true),
            bottom: view.getFloat32(offset + 12, true),
            confidence: view.getFloat32(offset + 16, true)
        });
    }
    return { sequence: view.getUint32(FACE_SECTION_OFFSET, true), boxes };
}

function drawFaces(boxes, width, height) {
    if (boxes.length === 0) {
        return;
    }

    const lineWidth = Math.max(2, Math.round(width / 320));
    const fontSize = Math.max(12, Math.round(height / 30));
    context.lineWidth = lineWidth;
    context.strokeStyle = BOX_COLOR;
    context.fillStyle = BOX_COLOR;
    context.font = `${fontSize}px "Segoe UI", sans-serif`;
    context.textBaseline = 'bottom';
    for (const box of boxes) {
        const x = box.left * width;
        const y = box.top * height;
        context.strokeRect(x, y, (box.right - box.left) * width, (box.bottom - box.top) * height);
        context.fillText(`${Math.floor(box.confidence * 100)}%`, x, Math.max(fontSize, y - lineWidth));
    }
}

//--------------------------------------------------------------------------------
// WebSocket
//--------------------------------------------------------------------------------

let mode = 'ws';
let socket = null;
let pending = null;
let decoding = false;

function setState(text) {
    state.textContent = text;
}

function openSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${location.host}/ws`);
    ws.binaryType = 'arraybuffer';
    ws.onopen = () => setState('Connected (WebSocket)');
    ws.onmessage = (e) => receive(e.data);
    ws.onclose = () => {
        if (socket !== ws) {
            return;
        }

        socket = null;
        setState('Disconnected, reconnecting...');
        setTimeout(() => {
            if ((mode === 'ws') && (socket === null)) {
                openSocket();
            }
        }, RECONNECT_DELAY);
    };
    socket = ws;
}

function closeSocket() {
    if (socket !== null) {
        const ws = socket;
        socket = null;
        ws.close();
    }
}

function receive(data) {
    const receivedAt = now();
    const view = new DataView(data);
    if (view.getUint16(0, true) !== HEADER_VERSION) {
        return;
    }

    const frame = {
        headerLength: view.getUint16(2, true),
        width: view.getUint16(4, true),
        height: view.getUint16(6, true),
        sequence: view.getUint32(8, true),
        captureTime: view.getFloat64(16, true),
        encodedTime: view.getFloat64(24, true),
        faces: (view.getUint32(12, true) & FACE_SECTION_FLAG) !== 0 ? readFaces(view) : null,
        data
    };

    client.received++;
    client.bytes += data.byteLength;
    client.receiveLatency.add(receivedAt - frame.captureTime);
    client.publishLatency.add(receivedAt - frame.encodedTime);
    client.faces = frame.faces !== null ? frame.faces.boxes.length : null;
    if ((frame.faces !== null) && (frame.faces.sequence > 0)) {
        client.detectionLag.add(frame.sequence - frame.faces.sequence);
    }

    // Keep only the latest frame while decoding
    if (pending !== null) {
        client.dropped++;
    }
    pending = frame;
    if (!decoding) {
        decodeLoop();
    }
}

async function decodeLoop() {
    decoding = true;
    while (pending !== null) {
        const frame = pending;
        pending = null;
        try {
            const start = now();
            const bitmap = await createImageBitmap(new Blob([new Uint8Array(frame.data, frame.headerLength)], { type: 'image/jpeg' }));
            client.decodeTime.add(now() - start);
            draw(bitmap, frame);
            bitmap.close();
        } catch (e) {
            console.warn('Decode failed.', e);
        }
    }
    decoding = false;
}

function draw(bitmap, frame) {
    if ((canvas.width !== bitmap.width) || (canvas.height !== bitmap.height)) {
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
    }

    context.drawImage(bitmap, 0, 0);
    if (frame.faces !== null) {
        drawFaces(frame.faces.boxes, bitmap.width, bitmap.height);
    }

    client.drawn++;
    client.width = bitmap.width;
    client.height = bitmap.height;
    client.drawLatency.add(now() - frame.captureTime);
}

//--------------------------------------------------------------------------------
// Mode
//--------------------------------------------------------------------------------

function setMode(value) {
    mode = value;
    if (mode === 'ws') {
        image.removeAttribute('src');
        image.hidden = true;
        canvas.hidden = false;
        openSocket();
    } else {
        closeSocket();
        pending = null;
        canvas.hidden = true;
        image.hidden = false;
        image.src = `/stream.mjpg?t=${Date.now()}`;
        setState('Connected (MJPEG)');
    }
}

image.addEventListener('error', () => {
    if (mode === 'mjpeg') {
        setState('MJPEG stream error');
    }
});

for (const input of document.querySelectorAll('input[name="mode"]')) {
    input.addEventListener('change', () => setMode(input.value));
}

setInterval(() => {
    updateClientStats();
    updateServerStats();
}, STATS_INTERVAL);

setMode('ws');
