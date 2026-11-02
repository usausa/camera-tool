// WebSocket message header (little endian), see StreamProtocol.cs
const HEADER_VERSION = 1;

const FACE_SECTION_FLAG = 0x1;

const MOTION_SECTION_FLAG = 0x2;

const BASE_HEADER_LENGTH = 32;

const FACE_SECTION_LENGTH = 8;

const FACE_LENGTH = 20;

// Followed by the cell mask
const MOTION_SECTION_LENGTH = 8;

const BOX_COLOR = '#00e676';

const CELL_COLOR = 'rgba(255, 145, 0, 0.31)';

// Outside the detection area
const AREA_COLOR = 'rgba(0, 0, 0, 0.38)';

const AREA_EDITING_COLOR = 'rgba(0, 0, 0, 0.6)';

const GRID_COLOR = 'rgba(255, 255, 255, 0.31)';

const SELECTION_COLOR = '#ffffff';

const STATS_INTERVAL = 1000;

const RECONNECT_DELAY = 1000;

// The checkbox is not synced with the server for a while after it is changed
const SYNC_DELAY = 2000;

const canvas = document.getElementById('canvas');
const context = canvas.getContext('2d');
const image = document.getElementById('image');
const state = document.getElementById('state');
const detection = document.getElementById('detection');
const motionDetection = document.getElementById('motion');
const motionGate = document.getElementById('motionGate');
const areaEdit = document.getElementById('areaEdit');
const areaReset = document.getElementById('areaReset');

// Check boxes synced with the server
const options = [
    { element: detection, url: '/api/detection', key: 'enabled', status: 'detectionEnabled', syncTime: 0 },
    { element: motionDetection, url: '/api/motion', key: 'enabled', status: 'motionEnabled', syncTime: 0 },
    { element: motionGate, url: '/api/detection', key: 'motionGate', status: 'motionGate', syncTime: 0 }
];

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
    motionCells: null,
    receiveLatency: new Counter(),
    publishLatency: new Counter(),
    drawLatency: new Counter(),
    decodeTime: new Counter(),
    detectionLag: new Counter()
};

// Detection area in the same layout as the motion mask
const area = {
    columns: 32,
    rows: 24,
    mask: new Uint8Array(96).fill(0xff),
    cells: 768,
    // Version of the server area that the mask is from (-1: not loaded)
    version: -1,
    // Not synced with the server until this time after it is sent
    syncTime: 0,
    loading: false,
    drag: null
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
    ['detectionLag', 'Detection lag'],
    ['motionCells', 'Moving cells']
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
    ['motion', 'Motion'],
    ['motionTime', 'Motion time'],
    ['area', 'Area'],
    ['cpu', 'CPU'],
    ['allocated', 'Allocated'],
    ['gc', 'GC 0/1/2']
]);

const formatFps = (value) => `${value.toFixed(1)} fps`;

const formatTime = (value, digits = 2) => (value ? `${value.average.toFixed(digits)} / ${value.max.toFixed(digits)} ms` : '-');

const formatFrames = (value) => (value ? `${value.average.toFixed(1)} / ${value.max} frames` : '-');

let lastStatsTime = now();

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
        clientRows.motionCells.textContent = client.motionCells !== null ? `${client.motionCells}` : '-';
    } else {
        // Not measurable with an img element
        clientRows.mode.textContent = 'MJPEG';
        clientRows.resolution.textContent = image.naturalWidth > 0 ? `${image.naturalWidth}x${image.naturalHeight}` : '-';
        for (const key of ['received', 'drawn', 'dropped', 'receiveLatency', 'publishLatency', 'drawLatency', 'decodeTime', 'size', 'bitrate', 'faces', 'detectionLag', 'motionCells']) {
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
    serverRows.detection.textContent = status.detectionError ?? (status.detectionEnabled ? (status.motionGate ? 'On (on motion)' : 'On') : 'Off');
    serverRows.detect.textContent = formatFps(status.detectFps);
    serverRows.preprocessTime.textContent = formatTime(status.preprocessTime, 3);
    serverRows.inferenceTime.textContent = formatTime(status.inferenceTime);
    serverRows.faces.textContent = `${status.faces}`;
    serverRows.motion.textContent = status.motionEnabled ? `${status.moving ? 'Moving' : 'Still'} ${status.motionLevel.toFixed(1)} %` : 'Off';
    serverRows.motionTime.textContent = formatTime(status.motionTime, 3);
    const totalCells = area.columns * area.rows;
    serverRows.area.textContent = status.areaCells === totalCells ? 'Full' : `${status.areaCells} / ${totalCells} cells`;
    serverRows.cpu.textContent = `${status.cpuUsage.toFixed(2)} %`;
    serverRows.allocated.textContent = `${status.allocated.toFixed(1)} KB/s`;
    serverRows.gc.textContent = `${status.gc0.toFixed(1)} / ${status.gc1.toFixed(1)} / ${status.gc2.toFixed(1)}`;

    syncOptions(status);
    syncArea(status);
}

//--------------------------------------------------------------------------------
// Options
//--------------------------------------------------------------------------------

// A check box is not synced with the server for a while after it is changed
function syncOptions(status) {
    const time = now();
    for (const option of options) {
        if (time > option.syncTime) {
            option.element.checked = status[option.status];
        }
    }
    updateOptionState();
}

function updateOptionState() {
    motionGate.disabled = !motionDetection.checked;
}

for (const option of options) {
    option.element.addEventListener('change', async () => {
        option.syncTime = now() + SYNC_DELAY;
        updateOptionState();
        try {
            await fetch(option.url, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ [option.key]: option.element.checked })
            });
        } catch {
            // Server not reachable
        }
    });
}

//--------------------------------------------------------------------------------
// Detection
//--------------------------------------------------------------------------------

function readFaces(view, offset) {
    const count = view.getUint16(offset + 4, true);
    const boxes = [];
    for (let i = 0; i < count; i++) {
        const position = offset + FACE_SECTION_LENGTH + (i * FACE_LENGTH);
        boxes.push({
            left: view.getFloat32(position, true),
            top: view.getFloat32(position + 4, true),
            right: view.getFloat32(position + 8, true),
            bottom: view.getFloat32(position + 12, true),
            confidence: view.getFloat32(position + 16, true)
        });
    }
    return { sequence: view.getUint32(offset, true), boxes };
}

function readMotion(view, offset) {
    const columns = view.getUint8(offset + 6);
    const rows = view.getUint8(offset + 7);
    return {
        sequence: view.getUint32(offset, true),
        cells: view.getUint16(offset + 4, true),
        columns,
        rows,
        // Row major, the lowest bit first
        mask: new Uint8Array(view.buffer, offset + MOTION_SECTION_LENGTH, Math.ceil((columns * rows) / 8))
    };
}

// Row major, the lowest bit first
const isCellSet = (mask, index) => (mask[index >> 3] & (1 << (index & 7))) !== 0;

// Adds the rectangles of the cells whose bit is the value to the path
function addCells(mask, columns, rows, width, height, value) {
    for (let row = 0; row < rows; row++) {
        const top = Math.round((row * height) / rows);
        const bottom = Math.round(((row + 1) * height) / rows);
        for (let column = 0; column < columns; column++) {
            if (isCellSet(mask, (row * columns) + column) === value) {
                const left = Math.round((column * width) / columns);
                const right = Math.round(((column + 1) * width) / columns);
                context.rect(left, top, right - left, bottom - top);
            }
        }
    }
}

function drawMotion(motion, width, height) {
    if (motion.cells === 0) {
        return;
    }

    context.fillStyle = CELL_COLOR;
    context.beginPath();
    addCells(motion.mask, motion.columns, motion.rows, width, height, true);
    context.fill();
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
// Area
//--------------------------------------------------------------------------------

// Rows from the top, one character per cell ('1': in the area, '0': outside)
function fromRows(data) {
    const mask = new Uint8Array(Math.ceil((data.columns * data.rows) / 8));
    for (let row = 0; row < data.rows; row++) {
        for (let column = 0; column < data.columns; column++) {
            if (data.cells[row][column] === '1') {
                const index = (row * data.columns) + column;
                mask[index >> 3] |= 1 << (index & 7);
            }
        }
    }
    return mask;
}

function toRows() {
    const rows = [];
    for (let row = 0; row < area.rows; row++) {
        let text = '';
        for (let column = 0; column < area.columns; column++) {
            text += isCellSet(area.mask, (row * area.columns) + column) ? '1' : '0';
        }
        rows.push(text);
    }
    return rows;
}

function setAreaMask(mask) {
    let cells = 0;
    for (let bits of mask) {
        for (; bits !== 0; bits &= bits - 1) {
            cells++;
        }
    }
    area.mask = mask;
    area.cells = cells;
}

// Sets the cells in the rectangle of the drag on a copy of the base mask
function applyDrag(drag) {
    const mask = drag.base.slice();
    for (let row = Math.min(drag.row0, drag.row1); row <= Math.max(drag.row0, drag.row1); row++) {
        for (let column = Math.min(drag.column0, drag.column1); column <= Math.max(drag.column0, drag.column1); column++) {
            const index = (row * area.columns) + column;
            if (drag.included) {
                mask[index >> 3] |= 1 << (index & 7);
            } else {
                mask[index >> 3] &= ~(1 << (index & 7));
            }
        }
    }
    setAreaMask(mask);
}

// The area is not synced with the server while dragging and for a while after it is sent
const isAreaSyncable = () => (area.drag === null) && (now() > area.syncTime);

async function loadArea(version) {
    if (area.loading) {
        return;
    }

    area.loading = true;
    try {
        const response = await fetch('/api/area', { cache: 'no-store' });
        if (response.ok) {
            const data = await response.json();
            if (isAreaSyncable()) {
                area.columns = data.columns;
                area.rows = data.rows;
                setAreaMask(fromRows(data));
                area.version = version;
            }
        }
    } catch {
        // Server not reachable
    } finally {
        area.loading = false;
    }
}

function syncArea(status) {
    if ((status.areaVersion !== area.version) && isAreaSyncable()) {
        loadArea(status.areaVersion);
    }
}

async function saveArea() {
    area.syncTime = now() + SYNC_DELAY;
    try {
        const response = await fetch('/api/area', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ columns: area.columns, rows: area.rows, cells: toRows() })
        });
        if (response.ok) {
            return;
        }
    } catch {
        // Server not reachable
    }

    // The area of the server is loaded again
    area.syncTime = 0;
    area.version = -1;
}

function drawArea(width, height) {
    const editing = areaEdit.checked;
    if (area.cells < area.columns * area.rows) {
        context.fillStyle = editing ? AREA_EDITING_COLOR : AREA_COLOR;
        context.beginPath();
        addCells(area.mask, area.columns, area.rows, width, height, false);
        context.fill();
    }

    if (!editing) {
        return;
    }

    const lineWidth = Math.max(1, Math.round(width / 640));
    context.lineWidth = lineWidth;
    context.strokeStyle = GRID_COLOR;
    context.beginPath();
    for (let column = 1; column < area.columns; column++) {
        const x = Math.round((column * width) / area.columns);
        context.moveTo(x, 0);
        context.lineTo(x, height);
    }
    for (let row = 1; row < area.rows; row++) {
        const y = Math.round((row * height) / area.rows);
        context.moveTo(0, y);
        context.lineTo(width, y);
    }
    context.stroke();

    const drag = area.drag;
    if (drag !== null) {
        const left = Math.round((Math.min(drag.column0, drag.column1) * width) / area.columns);
        const top = Math.round((Math.min(drag.row0, drag.row1) * height) / area.rows);
        const right = Math.round(((Math.max(drag.column0, drag.column1) + 1) * width) / area.columns);
        const bottom = Math.round(((Math.max(drag.row0, drag.row1) + 1) * height) / area.rows);
        context.lineWidth = lineWidth * 2;
        context.strokeStyle = SELECTION_COLOR;
        context.strokeRect(left, top, right - left, bottom - top);
    }
}

// Cell at the pointer in the video (object-fit: contain), null when the pointer is outside and clamp is false
function getCell(e, clamp) {
    const rect = canvas.getBoundingClientRect();
    if ((client.width === 0) || (rect.width === 0) || (rect.height === 0)) {
        return null;
    }

    const scale = Math.min(rect.width / canvas.width, rect.height / canvas.height);
    const width = canvas.width * scale;
    const height = canvas.height * scale;
    let x = (e.clientX - rect.left - ((rect.width - width) / 2)) / width;
    let y = (e.clientY - rect.top - ((rect.height - height) / 2)) / height;
    if (!clamp && ((x < 0) || (x >= 1) || (y < 0) || (y >= 1))) {
        return null;
    }

    x = Math.min(Math.max(x, 0), 1);
    y = Math.min(Math.max(y, 0), 1);
    return {
        column: Math.min(Math.floor(x * area.columns), area.columns - 1),
        row: Math.min(Math.floor(y * area.rows), area.rows - 1)
    };
}

// A drag sets the cells in the rectangle from the pressed cell to the current cell:
// they are removed when the pressed cell is in the area, otherwise they are added
canvas.addEventListener('pointerdown', (e) => {
    const cell = areaEdit.checked && (e.button === 0) ? getCell(e, false) : null;
    if (cell === null) {
        return;
    }

    area.drag = {
        pointerId: e.pointerId,
        base: area.mask,
        included: !isCellSet(area.mask, (cell.row * area.columns) + cell.column),
        column0: cell.column,
        row0: cell.row,
        column1: cell.column,
        row1: cell.row
    };
    canvas.setPointerCapture(e.pointerId);
    applyDrag(area.drag);
    e.preventDefault();
});

canvas.addEventListener('pointermove', (e) => {
    const drag = area.drag;
    const cell = (drag !== null) && (e.pointerId === drag.pointerId) ? getCell(e, true) : null;
    if ((cell === null) || ((cell.column === drag.column1) && (cell.row === drag.row1))) {
        return;
    }

    drag.column1 = cell.column;
    drag.row1 = cell.row;
    applyDrag(drag);
});

// The server applies the area when the drag ends
function endDrag(e) {
    if ((area.drag !== null) && (e.pointerId === area.drag.pointerId)) {
        area.drag = null;
        saveArea();
    }
}

canvas.addEventListener('pointerup', endDrag);
canvas.addEventListener('pointercancel', endDrag);
canvas.addEventListener('lostpointercapture', endDrag);

areaEdit.addEventListener('change', () => canvas.classList.toggle('editing', areaEdit.checked));

areaReset.addEventListener('click', () => {
    // All cells
    applyDrag({ base: new Uint8Array(area.mask.length), included: true, column0: 0, row0: 0, column1: area.columns - 1, row1: area.rows - 1 });
    saveArea();
});

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

    // The motion section follows the face section
    const flags = view.getUint32(12, true);
    let offset = BASE_HEADER_LENGTH;
    let faces = null;
    if ((flags & FACE_SECTION_FLAG) !== 0) {
        faces = readFaces(view, offset);
        offset += FACE_SECTION_LENGTH + (faces.boxes.length * FACE_LENGTH);
    }

    const frame = {
        headerLength: view.getUint16(2, true),
        width: view.getUint16(4, true),
        height: view.getUint16(6, true),
        sequence: view.getUint32(8, true),
        captureTime: view.getFloat64(16, true),
        encodedTime: view.getFloat64(24, true),
        faces,
        motion: (flags & MOTION_SECTION_FLAG) !== 0 ? readMotion(view, offset) : null,
        data
    };

    client.received++;
    client.bytes += data.byteLength;
    client.receiveLatency.add(receivedAt - frame.captureTime);
    client.publishLatency.add(receivedAt - frame.encodedTime);
    client.faces = frame.faces !== null ? frame.faces.boxes.length : null;
    client.motionCells = frame.motion !== null ? frame.motion.cells : null;
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
    drawArea(bitmap.width, bitmap.height);
    if (frame.motion !== null) {
        drawMotion(frame.motion, bitmap.width, bitmap.height);
    }
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
        areaEdit.disabled = false;
        openSocket();
    } else {
        closeSocket();
        pending = null;
        // The area is edited on the canvas
        areaEdit.checked = false;
        areaEdit.disabled = true;
        canvas.classList.remove('editing');
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
updateServerStats();
