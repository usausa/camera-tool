// WebSocket message header (little endian), see StreamProtocol.cs
const HEADER_VERSION = 1;

const FACE_SECTION_FLAG = 0x1;

const MOTION_SECTION_FLAG = 0x2;

const OBJECT_SECTION_FLAG = 0x4;

const KEY_FRAME_FLAG = 0x8;

// H.264 in full range (otherwise video range)
const FULL_RANGE_FLAG = 0x10;

const BASE_HEADER_LENGTH = 32;

const FACE_SECTION_LENGTH = 8;

const FACE_LENGTH = 20;

// Followed by the cell mask
const MOTION_SECTION_LENGTH = 8;

const OBJECT_SECTION_LENGTH = 8;

const OBJECT_LENGTH = 24;

const BOX_COLOR = '#00e676';

// Object boxes by the group of the class
const GROUP_COLORS = { person: '#2979ff', animal: '#ffc400', vehicle: '#e040fb', other: '#ffffff' };

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

// H.264 frames in the decoder, more delta frames are dropped until a key frame (the decoder is slower than the stream)
const MAX_DECODE_QUEUE = 3;

// H.264 frames kept for the output of the decoder
const MAX_DECODE_FRAMES = 64;

// A key frame is requested at most once per interval
const KEY_FRAME_REQUEST_INTERVAL = 500;

const canvas = document.getElementById('canvas');
const context = canvas.getContext('2d');
const image = document.getElementById('image');
const state = document.getElementById('state');
const detection = document.getElementById('detection');
const objectDetection = document.getElementById('objects');
const motionDetection = document.getElementById('motion');
const motionGate = document.getElementById('motionGate');
const areaEdit = document.getElementById('areaEdit');
const areaReset = document.getElementById('areaReset');

// Check boxes synced with the server (status: reads the value from /api/status)
const options = [
    { element: detection, url: '/api/detection', key: 'enabled', status: (s) => s.faces.enabled, syncTime: 0 },
    { element: objectDetection, url: '/api/objects', key: 'enabled', status: (s) => s.objects.enabled, syncTime: 0 },
    { element: motionDetection, url: '/api/motion', key: 'enabled', status: (s) => s.motionEnabled, syncTime: 0 },
    { element: motionGate, url: '/api/detection', key: 'motionGate', status: (s) => s.motionGate, syncTime: 0 }
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
    objects: null,
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
    // Version of the server area that the mask is from (null: not loaded, any number can be a version)
    version: null,
    // Not synced with the server until this time after it is sent
    syncTime: 0,
    loading: false,
    drag: null
};

// The area drawn into its own canvas: filling the cells for every frame took most of the overlay time
// (about 0.4 ms at 640x480 with the rasterizing, a copy of the layer about 0.03 ms)
const areaLayer = {
    canvas: document.createElement('canvas'),
    // Drawn for the current mask (the size and the editing are checked when drawing)
    valid: false,
    editing: false,
    // All cells in the area and not editing: nothing to draw
    empty: true
};

const areaLayerContext = areaLayer.canvas.getContext('2d');

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
    ['codec', 'Codec'],
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
    ['motionCells', 'Moving cells'],
    ['objects', 'Objects']
]);

// Rows of the face or object detection (the same items in /api/status)
const detectorRows = (key, labels) => [
    [`${key}State`, labels[0]],
    [`${key}Fps`, labels[1]],
    [`${key}Preprocess`, labels[2]],
    [`${key}Inference`, labels[3]],
    [`${key}Count`, labels[4]]
];

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
    ['h264', 'H.264 encoder'],
    ['h264Clients', 'H.264 clients'],
    ['h264Encode', 'H.264 encode'],
    ['h264Send', 'H.264 send'],
    ['h264Skip', 'H.264 skip'],
    ['h264Drop', 'H.264 drop'],
    ['h264KeyFrames', 'H.264 key frames'],
    ['h264InputTime', 'H.264 input'],
    ['h264EncodeTime', 'H.264 encode time'],
    ['h264StreamLatency', 'H.264 arrival → publish'],
    ['h264SendStartLatency', 'H.264 publish → send start'],
    ['h264SentLatency', 'H.264 publish → sent'],
    ['h264Bitrate', 'H.264 bitrate'],
    ['h264Buffers', 'H.264 buffers'],
    ...detectorRows('faces', ['Detection', 'Detect', 'Preprocess', 'Inference', 'Faces']),
    ['motion', 'Motion'],
    ['motionTime', 'Motion time'],
    ...detectorRows('objects', ['Object detection', 'Object detect', 'Object preprocess', 'Object inference', 'Objects']),
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
    if (mode !== 'mjpeg') {
        clientRows.mode.textContent = mode === 'h264' ? 'WebSocket (H.264)' : 'WebSocket';
        clientRows.codec.textContent = mode === 'h264' ? (h264.codec ?? '-') : 'JPEG';
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
        clientRows.objects.textContent = client.objects !== null ? `${client.objects}` : '-';
    } else {
        // Not measurable with an img element
        clientRows.mode.textContent = 'MJPEG';
        clientRows.codec.textContent = 'JPEG';
        clientRows.resolution.textContent = image.naturalWidth > 0 ? `${image.naturalWidth}x${image.naturalHeight}` : '-';
        for (const key of ['received', 'drawn', 'dropped', 'receiveLatency', 'publishLatency', 'drawLatency', 'decodeTime', 'size', 'bitrate', 'faces', 'detectionLag', 'motionCells', 'objects']) {
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
    serverRows.h264.textContent = status.h264Error ?? (status.h264Enabled ? (status.h264Encoder !== null ? `${status.h264Encoder} (${status.h264Hardware ? 'hardware' : 'software'})` : 'Not started') : 'Off');
    serverRows.h264Clients.textContent = `${status.h264Clients}`;
    serverRows.h264Encode.textContent = formatFps(status.h264EncodeFps);
    serverRows.h264Send.textContent = formatFps(status.h264SendFps);
    serverRows.h264Skip.textContent = formatFps(status.h264SkipFps);
    serverRows.h264Drop.textContent = formatFps(status.h264DropFps);
    serverRows.h264KeyFrames.textContent = `${status.h264KeyFrameFps.toFixed(1)} /s`;
    serverRows.h264InputTime.textContent = formatTime(status.h264InputTime, 3);
    serverRows.h264EncodeTime.textContent = formatTime(status.h264EncodeTime);
    serverRows.h264StreamLatency.textContent = formatTime(status.h264StreamLatency);
    serverRows.h264SendStartLatency.textContent = formatTime(status.h264SendStartLatency);
    serverRows.h264SentLatency.textContent = formatTime(status.h264SentLatency);
    serverRows.h264Bitrate.textContent = `${(status.h264Bitrate / 1000).toFixed(2)} Mbps`;
    serverRows.h264Buffers.textContent = `${status.h264Buffers}`;
    // The gate works only while the motion detection is on
    const gated = status.motionGate && status.motionEnabled;
    showDetector('faces', status.faces, gated);
    serverRows.motion.textContent = status.motionEnabled ? `${status.moving ? 'Moving' : 'Still'} ${status.motionLevel.toFixed(1)} %` : 'Off';
    serverRows.motionTime.textContent = formatTime(status.motionTime, 3);
    showDetector('objects', status.objects, gated);
    const totalCells = area.columns * area.rows;
    serverRows.area.textContent = status.areaCells === totalCells ? 'Full' : `${status.areaCells} / ${totalCells} cells`;
    serverRows.cpu.textContent = `${status.cpuUsage.toFixed(2)} %`;
    serverRows.allocated.textContent = `${status.allocated.toFixed(1)} KB/s`;
    serverRows.gc.textContent = `${status.gc0.toFixed(1)} / ${status.gc1.toFixed(1)} / ${status.gc2.toFixed(1)}`;

    syncOptions(status);
    syncArea(status);
    h264Enabled = status.h264Enabled;
    if ((mode === 'h264') && !h264Enabled) {
        setState(H264_DISABLED);
    }
}

// key: of detectorRows, detector: faces or objects of /api/status
function showDetector(key, detector, gated) {
    serverRows[`${key}State`].textContent = detector.error ?? (detector.enabled ? (gated ? 'On (on motion)' : 'On') : 'Off');
    serverRows[`${key}Fps`].textContent = formatFps(detector.fps);
    serverRows[`${key}Preprocess`].textContent = formatTime(detector.preprocessTime, 3);
    serverRows[`${key}Inference`].textContent = formatTime(detector.inferenceTime);
    serverRows[`${key}Count`].textContent = `${detector.count}`;
}

//--------------------------------------------------------------------------------
// Options
//--------------------------------------------------------------------------------

// A check box is not synced with the server for a while after it is changed
function syncOptions(status) {
    const time = now();
    for (const option of options) {
        if (time > option.syncTime) {
            option.element.checked = option.status(status);
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

// Adds the rectangles of the cells whose bit is the value to the path of the target
function addCells(target, mask, columns, rows, width, height, value) {
    for (let row = 0; row < rows; row++) {
        const top = Math.round((row * height) / rows);
        const bottom = Math.round(((row + 1) * height) / rows);
        for (let column = 0; column < columns; column++) {
            if (isCellSet(mask, (row * columns) + column) === value) {
                const left = Math.round((column * width) / columns);
                const right = Math.round(((column + 1) * width) / columns);
                target.rect(left, top, right - left, bottom - top);
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
    addCells(context, motion.mask, motion.columns, motion.rows, width, height, true);
    context.fill();
}

function readObjects(view, offset) {
    const count = view.getUint16(offset + 4, true);
    const objects = [];
    for (let i = 0; i < count; i++) {
        const position = offset + OBJECT_SECTION_LENGTH + (i * OBJECT_LENGTH);
        objects.push({
            left: view.getFloat32(position, true),
            top: view.getFloat32(position + 4, true),
            right: view.getFloat32(position + 8, true),
            bottom: view.getFloat32(position + 12, true),
            confidence: view.getFloat32(position + 16, true),
            classId: view.getUint16(position + 20, true)
        });
    }
    return { sequence: view.getUint32(offset, true), objects };
}

// Class names and groups by the class id
let objectLabels = [];

async function loadObjectLabels() {
    try {
        const response = await fetch('/api/objects/labels', { cache: 'no-store' });
        if (response.ok) {
            objectLabels = await response.json();
        }
    } catch {
        // Server not reachable, loaded again when the socket is connected
    }
}

function drawObjects(objects, width, height) {
    if (objects.length === 0) {
        return;
    }

    const lineWidth = Math.max(2, Math.round(width / 320));
    const fontSize = Math.max(12, Math.round(height / 30));
    context.lineWidth = lineWidth;
    context.font = `${fontSize}px "Segoe UI", sans-serif`;
    context.textBaseline = 'bottom';
    for (const value of objects) {
        const label = objectLabels[value.classId];
        const color = GROUP_COLORS[label?.group ?? 'other'];
        const x = value.left * width;
        const y = value.top * height;
        context.strokeStyle = color;
        context.fillStyle = color;
        context.strokeRect(x, y, (value.right - value.left) * width, (value.bottom - value.top) * height);
        context.fillText(`${label?.name ?? value.classId} ${Math.floor(value.confidence * 100)}%`, x, Math.max(fontSize, y - lineWidth));
    }
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
    areaLayer.valid = false;
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
        if (response.status === 500) {
            // Applied, but not kept after the server restarts
            setState('The area could not be saved to the file on the server');
        }
    } catch {
        // Server not reachable
    }

    // The area of the server is loaded again
    area.syncTime = 0;
    area.version = null;
}

// The layer is drawn again only when the mask, the size or the editing changes, and copied onto each frame
function drawArea(width, height) {
    const editing = areaEdit.checked;
    if (!areaLayer.valid || (areaLayer.editing !== editing) || (areaLayer.canvas.width !== width) || (areaLayer.canvas.height !== height)) {
        renderAreaLayer(width, height, editing);
    }

    if (!areaLayer.empty) {
        context.drawImage(areaLayer.canvas, 0, 0);
    }

    const drag = area.drag;
    if (editing && (drag !== null)) {
        const left = Math.round((Math.min(drag.column0, drag.column1) * width) / area.columns);
        const top = Math.round((Math.min(drag.row0, drag.row1) * height) / area.rows);
        const right = Math.round(((Math.max(drag.column0, drag.column1) + 1) * width) / area.columns);
        const bottom = Math.round(((Math.max(drag.row0, drag.row1) + 1) * height) / area.rows);
        context.lineWidth = Math.max(1, Math.round(width / 640)) * 2;
        context.strokeStyle = SELECTION_COLOR;
        context.strokeRect(left, top, right - left, bottom - top);
    }
}

// The cells outside of the area (darker while editing) and the grid while editing
function renderAreaLayer(width, height, editing) {
    // Setting the size also clears the canvas
    areaLayer.canvas.width = width;
    areaLayer.canvas.height = height;
    areaLayer.valid = true;
    areaLayer.editing = editing;
    areaLayer.empty = !editing && (area.cells === area.columns * area.rows);

    if (area.cells < area.columns * area.rows) {
        areaLayerContext.fillStyle = editing ? AREA_EDITING_COLOR : AREA_COLOR;
        areaLayerContext.beginPath();
        addCells(areaLayerContext, area.mask, area.columns, area.rows, width, height, false);
        areaLayerContext.fill();
    }

    if (!editing) {
        return;
    }

    areaLayerContext.lineWidth = Math.max(1, Math.round(width / 640));
    areaLayerContext.strokeStyle = GRID_COLOR;
    areaLayerContext.beginPath();
    for (let column = 1; column < area.columns; column++) {
        const x = Math.round((column * width) / area.columns);
        areaLayerContext.moveTo(x, 0);
        areaLayerContext.lineTo(x, height);
    }
    for (let row = 1; row < area.rows; row++) {
        const y = Math.round((row * height) / area.rows);
        areaLayerContext.moveTo(0, y);
        areaLayerContext.lineTo(width, y);
    }
    areaLayerContext.stroke();
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

const H264_DISABLED = 'H.264 is disabled on the server';

// 'ws': WebSocket (JPEG), 'h264': WebSocket (H.264), 'mjpeg': MJPEG
let mode = 'ws';
let socket = null;
let pending = null;
let decoding = false;

// Setting of the server (the connection is refused while it is disabled)
let h264Enabled = true;

function setState(text) {
    state.textContent = text;
}

function openSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socketMode = mode;
    const ws = new WebSocket(`${protocol}//${location.host}${socketMode === 'h264' ? '/ws-h264' : '/ws'}`);
    ws.binaryType = 'arraybuffer';
    ws.onopen = () => {
        setState(socketMode === 'h264' ? 'Connected (H.264)' : 'Connected (WebSocket)');
        if (objectLabels.length === 0) {
            loadObjectLabels();
        }
    };
    ws.onmessage = (e) => {
        if (socket === ws) {
            receive(e.data);
        }
    };
    ws.onclose = () => {
        if (socket !== ws) {
            return;
        }

        socket = null;
        // The server sends the frames from a key frame after the reconnection
        resetDecoder();
        setState((socketMode === 'h264') && !h264Enabled ? H264_DISABLED : 'Disconnected, reconnecting...');
        setTimeout(() => {
            if ((mode === socketMode) && (socket === null)) {
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

    // The sections are in the order of the faces, the motion and the objects
    const flags = view.getUint32(12, true);
    let offset = BASE_HEADER_LENGTH;
    let faces = null;
    if ((flags & FACE_SECTION_FLAG) !== 0) {
        faces = readFaces(view, offset);
        offset += FACE_SECTION_LENGTH + (faces.boxes.length * FACE_LENGTH);
    }

    let motion = null;
    if ((flags & MOTION_SECTION_FLAG) !== 0) {
        motion = readMotion(view, offset);
        offset += MOTION_SECTION_LENGTH + motion.mask.length;
    }

    const frame = {
        headerLength: view.getUint16(2, true),
        width: view.getUint16(4, true),
        height: view.getUint16(6, true),
        sequence: view.getUint32(8, true),
        captureTime: view.getFloat64(16, true),
        encodedTime: view.getFloat64(24, true),
        keyFrame: (flags & KEY_FRAME_FLAG) !== 0,
        fullRange: (flags & FULL_RANGE_FLAG) !== 0,
        faces,
        motion,
        objects: (flags & OBJECT_SECTION_FLAG) !== 0 ? readObjects(view, offset) : null,
        data
    };

    client.received++;
    client.bytes += data.byteLength;
    client.receiveLatency.add(receivedAt - frame.captureTime);
    client.publishLatency.add(receivedAt - frame.encodedTime);
    client.faces = frame.faces !== null ? frame.faces.boxes.length : null;
    client.motionCells = frame.motion !== null ? frame.motion.cells : null;
    client.objects = frame.objects !== null ? frame.objects.objects.length : null;
    if ((frame.faces !== null) && (frame.faces.sequence > 0)) {
        client.detectionLag.add(frame.sequence - frame.faces.sequence);
    }

    if (mode === 'h264') {
        decodeH264(frame);
        return;
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

// WebCodecs ImageDecoder where it decodes JPEG (about twice as fast as createImageBitmap of a Blob, the same colors)
const jpegDecoderSupported = typeof ImageDecoder !== 'undefined'
    ? ImageDecoder.isTypeSupported('image/jpeg').catch(() => false)
    : Promise.resolve(false);

// A VideoFrame (ImageDecoder) or an ImageBitmap, closed by the caller
async function decodeJpeg(data) {
    if (await jpegDecoderSupported) {
        const decoder = new ImageDecoder({ data, type: 'image/jpeg' });
        try {
            return (await decoder.decode()).image;
        } finally {
            decoder.close();
        }
    }

    return createImageBitmap(new Blob([data], { type: 'image/jpeg' }));
}

async function decodeLoop() {
    decoding = true;
    while (pending !== null) {
        const frame = pending;
        pending = null;
        try {
            const start = now();
            const image = await decodeJpeg(new Uint8Array(frame.data, frame.headerLength));
            client.decodeTime.add(now() - start);
            try {
                // displayWidth of a VideoFrame, width of an ImageBitmap
                draw(image, image.displayWidth ?? image.width, image.displayHeight ?? image.height, frame);
            } finally {
                image.close();
            }
        } catch (e) {
            console.warn('Decode failed.', e);
        }
    }
    decoding = false;
}

// The image is an ImageBitmap (JPEG) or a VideoFrame (H.264)
function draw(source, width, height, frame) {
    if ((canvas.width !== width) || (canvas.height !== height)) {
        canvas.width = width;
        canvas.height = height;
    }

    context.drawImage(source, 0, 0);
    drawArea(width, height);
    if (frame.motion !== null) {
        drawMotion(frame.motion, width, height);
    }
    if (frame.objects !== null) {
        drawObjects(frame.objects.objects, width, height);
    }
    if (frame.faces !== null) {
        drawFaces(frame.faces.boxes, width, height);
    }

    client.drawn++;
    client.width = width;
    client.height = height;
    client.drawLatency.add(now() - frame.captureTime);
}

//--------------------------------------------------------------------------------
// H.264 (WebCodecs)
//--------------------------------------------------------------------------------

const h264 = {
    decoder: null,
    // Configuration of the decoder
    codec: null,
    width: 0,
    height: 0,
    fullRange: false,
    // The browser cannot decode the stream (no key frame is requested then)
    unsupported: false,
    // Frames in the decoder by the chunk timestamp
    frames: new Map(),
    nextTimestamp: 0,
    // Delta frames are not decoded until a key frame
    waitingKeyFrame: true,
    lastKeyFrameRequest: 0
};

const isWebCodecsAvailable = () => typeof VideoDecoder !== 'undefined';

// Codec string for WebCodecs (avc1.PPCCLL: profile_idc, constraint flags, level_idc) from the SPS, null without an SPS
function getCodecString(data) {
    const hex = (value) => value.toString(16).toUpperCase().padStart(2, '0');
    for (let i = 0; i + 6 < data.length; i++) {
        if ((data[i] === 0) && (data[i + 1] === 0) && (data[i + 2] === 1)) {
            if ((data[i + 3] & 0x1f) === 7) {
                return `avc1.${hex(data[i + 4])}${hex(data[i + 5])}${hex(data[i + 6])}`;
            }
            i += 2;
        }
    }
    return null;
}

function resetDecoder() {
    if ((h264.decoder !== null) && (h264.decoder.state !== 'closed')) {
        h264.decoder.close();
    }
    h264.decoder = null;
    h264.codec = null;
    h264.frames.clear();
    h264.waitingKeyFrame = true;
    h264.unsupported = false;
}

// Decoding is given up until the mode is selected again, and the connection is closed so that the server stops encoding
// (closeSocket clears the socket first, so the connection is not opened again)
function setUnsupported(e) {
    console.warn('H.264 is not supported.', e);
    setState(`H.264 is not supported by this browser: ${e.message}`);
    h264.unsupported = true;
    h264.decoder = null;
    h264.codec = null;
    h264.frames.clear();
    if (mode === 'h264') {
        closeSocket();
    }
}

// Any text message asks the server for a key frame
function requestKeyFrame() {
    h264.waitingKeyFrame = true;
    const time = now();
    if ((socket !== null) && (socket.readyState === WebSocket.OPEN) && (time - h264.lastKeyFrameRequest > KEY_FRAME_REQUEST_INTERVAL)) {
        h264.lastKeyFrameRequest = time;
        socket.send('key');
    }
}

function createDecoder() {
    return new VideoDecoder({
        output: (videoFrame) => {
            const frame = h264.frames.get(videoFrame.timestamp);
            h264.frames.delete(videoFrame.timestamp);
            if (frame !== undefined) {
                client.decodeTime.add(now() - frame.decodeStart);
                draw(videoFrame, videoFrame.displayWidth, videoFrame.displayHeight, frame);
            }
            videoFrame.close();
        },
        error: (e) => {
            // A configuration the browser cannot decode does not change with a key frame
            if (e.name === 'NotSupportedError') {
                setUnsupported(e);
                return;
            }

            // The decoder is closed, created again with the next key frame
            console.warn('Decode failed.', e);
            setState(`H.264 decode failed: ${e.message}`);
            h264.decoder = null;
            h264.codec = null;
            h264.frames.clear();
            requestKeyFrame();
        }
    });
}

function decodeH264(frame) {
    if (h264.unsupported) {
        client.dropped++;
        return;
    }

    const data = new Uint8Array(frame.data, frame.headerLength);
    if (frame.keyFrame) {
        // The key frame has the SPS, the decoder is configured again when the stream changes
        const codec = getCodecString(data);
        if ((codec !== null) &&
            ((h264.decoder === null) || (codec !== h264.codec) || (frame.width !== h264.width) || (frame.height !== h264.height) || (frame.fullRange !== h264.fullRange))) {
            try {
                h264.decoder ??= createDecoder();
                // Annex B (no description), the frames are output in the decode order.
                // The colors are given because the software encoder writes the range without the colour description,
                // which a decoder ignores (the YUV is BT.601 as the JPEG, the primaries and the transfer are BT.709 / sRGB).
                // The browser decides on the hardware decoding ('prefer-hardware' fails where it is not available).
                h264.decoder.configure({
                    codec,
                    codedWidth: frame.width,
                    codedHeight: frame.height,
                    optimizeForLatency: true,
                    colorSpace: { primaries: 'bt709', transfer: 'bt709', matrix: 'bt470bg', fullRange: frame.fullRange }
                });
                h264.codec = codec;
                h264.width = frame.width;
                h264.height = frame.height;
                h264.fullRange = frame.fullRange;
            } catch (e) {
                // An invalid configuration does not change with a key frame either
                setUnsupported(e);
                client.dropped++;
                return;
            }
        }
        if (h264.decoder !== null) {
            h264.waitingKeyFrame = false;
        }
    } else if (!h264.waitingKeyFrame && (h264.decoder !== null) && (h264.decoder.decodeQueueSize >= MAX_DECODE_QUEUE)) {
        // The decoder is slower than the stream
        requestKeyFrame();
    }

    if ((h264.decoder === null) || h264.waitingKeyFrame) {
        client.dropped++;
        requestKeyFrame();
        return;
    }

    const timestamp = h264.nextTimestamp++;
    frame.decodeStart = now();
    h264.frames.set(timestamp, frame);
    // A frame the decoder did not output is not kept
    h264.frames.delete(timestamp - MAX_DECODE_FRAMES);
    try {
        h264.decoder.decode(new EncodedVideoChunk({ type: frame.keyFrame ? 'key' : 'delta', timestamp, data }));
    } catch (e) {
        console.warn('Decode failed.', e);
        resetDecoder();
        requestKeyFrame();
    }
}

//--------------------------------------------------------------------------------
// Mode
//--------------------------------------------------------------------------------

function setMode(value) {
    closeSocket();
    pending = null;
    resetDecoder();
    mode = value;
    if (mode !== 'mjpeg') {
        image.removeAttribute('src');
        image.hidden = true;
        canvas.hidden = false;
        areaEdit.disabled = false;
        if ((mode === 'h264') && !isWebCodecsAvailable()) {
            setState('WebCodecs is not available (localhost or HTTPS is needed)');
            return;
        }
        openSocket();
    } else {
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
