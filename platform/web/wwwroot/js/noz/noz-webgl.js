// NoZ WebGL Render Backend

let gl = null;
let canvas = null;

// Resource tracking
let nextBufferId = 1;
let nextTextureId = 2; // 1 reserved for white texture
let nextShaderId = 1;
let nextFenceId = 1;

const buffers = new Map();      // id -> { glBuffer, type: 'vertex'|'index' }
const textures = new Map();     // id -> WebGLTexture
const shaders = new Map();      // id -> { program, uniformLocations }
const fences = new Map();       // id -> WebGLSync (WebGL2 only)

// VAO for mesh vertex format
let meshVao = null;
let boundVertexBuffer = null;
let boundIndexBuffer = null;
let boundShader = null;

export function init() {
    canvas = document.getElementById('noz-canvas');
    if (!canvas) {
        throw new Error('Canvas not found. Make sure platform is initialized first.');
    }

    gl = canvas.getContext('webgl2');
    if (!gl) {
        throw new Error('WebGL 2.0 not supported');
    }
    console.log('NoZ: Using WebGL 2.0');

    // Set default state
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    // Create VAO for mesh vertex format
    meshVao = gl.createVertexArray();
    gl.bindVertexArray(meshVao);

    // Create built-in white texture (1x1 white pixel)
    const whiteTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, whiteTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    textures.set(1, whiteTex); // TextureHandle.White

    return true;
}

export function shutdown() {
    // Clean up resources
    buffers.forEach((buf) => gl.deleteBuffer(buf.glBuffer));
    buffers.clear();
    textures.forEach((tex) => gl.deleteTexture(tex));
    textures.clear();
    shaders.forEach((shader) => gl.deleteProgram(shader.program));
    shaders.clear();
    fences.forEach((fence) => gl.deleteSync(fence));
    fences.clear();
    if (meshVao) gl.deleteVertexArray(meshVao);
    gl = null;
}

export function beginFrame() {
    gl.bindVertexArray(meshVao);
}

export function endFrame() {
    gl.flush();
}

export function clear(r, g, b, a) {
    gl.clearColor(r, g, b, a);
    gl.clear(gl.COLOR_BUFFER_BIT);
}

export function setViewport(x, y, width, height) {
    gl.viewport(x, y, width, height);
}

export function setScissor(x, y, width, height) {
    gl.enable(gl.SCISSOR_TEST);
    gl.scissor(x, y, width, height);
}

export function disableScissor() {
    gl.disable(gl.SCISSOR_TEST);
}

// === Mesh Management ===

let nextMeshId = 1;
const meshes = new Map(); // id -> { vao, vbo, ebo, stride }

export function createMesh(maxVertices, maxIndices, stride, usage) {
    const vao = gl.createVertexArray();
    const vbo = gl.createBuffer();
    const ebo = gl.createBuffer();

    gl.bindVertexArray(vao);

    gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
    gl.bufferData(gl.ARRAY_BUFFER, maxVertices * stride, toGLUsage(usage));

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, maxIndices * 2, toGLUsage(usage));

    // Setup vertex attributes for MeshVertex layout (64 bytes total)
    // in_position: vec2 at offset 0
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);

    // in_uv: vec2 at offset 8
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);

    // in_normal: vec2 at offset 16
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 2, gl.FLOAT, false, stride, 16);

    // in_color: vec4 at offset 24
    gl.enableVertexAttribArray(3);
    gl.vertexAttribPointer(3, 4, gl.FLOAT, false, stride, 24);

    // in_bone: int at offset 40
    gl.enableVertexAttribArray(4);
    gl.vertexAttribIPointer(4, 1, gl.INT, stride, 40);

    // in_atlas: int at offset 44
    gl.enableVertexAttribArray(5);
    gl.vertexAttribIPointer(5, 1, gl.INT, stride, 44);

    // in_frame_count: int at offset 48
    gl.enableVertexAttribArray(6);
    gl.vertexAttribIPointer(6, 1, gl.INT, stride, 48);

    // in_frame_width: float at offset 52
    gl.enableVertexAttribArray(7);
    gl.vertexAttribPointer(7, 1, gl.FLOAT, false, stride, 52);

    // in_frame_rate: float at offset 56
    gl.enableVertexAttribArray(8);
    gl.vertexAttribPointer(8, 1, gl.FLOAT, false, stride, 56);

    // in_frame_time: float at offset 60
    gl.enableVertexAttribArray(9);
    gl.vertexAttribPointer(9, 1, gl.FLOAT, false, stride, 60);

    gl.bindVertexArray(null);

    const id = nextMeshId++;
    meshes.set(id, { vao, vbo, ebo, stride });
    return id;
}

export function destroyMesh(id) {
    const mesh = meshes.get(id);
    if (mesh) {
        gl.deleteVertexArray(mesh.vao);
        gl.deleteBuffer(mesh.vbo);
        gl.deleteBuffer(mesh.ebo);
        meshes.delete(id);
    }
}

export function bindMesh(id) {
    const mesh = meshes.get(id);
    if (!mesh) return;

    gl.bindVertexArray(mesh.vao);
}

export function updateMesh(id, vertexData, indexData) {
    const mesh = meshes.get(id);
    if (!mesh) return;

    gl.bindVertexArray(mesh.vao);

    if (vertexData && vertexData.length > 0) {
        gl.bindBuffer(gl.ARRAY_BUFFER, mesh.vbo);
        gl.bufferSubData(gl.ARRAY_BUFFER, 0, new Uint8Array(vertexData));
    }

    if (indexData && indexData.length > 0) {
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, mesh.ebo);
        gl.bufferSubData(gl.ELEMENT_ARRAY_BUFFER, 0, new Uint8Array(indexData));
    }
}

// === Buffer Management ===

export function createVertexBuffer(sizeInBytes, usage) {
    const glBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, glBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, sizeInBytes, toGLUsage(usage));

    const id = nextBufferId++;
    buffers.set(id, { glBuffer, type: 'vertex' });
    return id;
}

export function createIndexBuffer(sizeInBytes, usage) {
    const glBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, glBuffer);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, sizeInBytes, toGLUsage(usage));

    const id = nextBufferId++;
    buffers.set(id, { glBuffer, type: 'index' });
    return id;
}

export function destroyBuffer(id) {
    const buf = buffers.get(id);
    if (buf) {
        gl.deleteBuffer(buf.glBuffer);
        buffers.delete(id);
    }
}

export function updateVertexBuffer(id, offsetBytes, data) {
    const buf = buffers.get(id);
    if (!buf) return;

    gl.bindBuffer(gl.ARRAY_BUFFER, buf.glBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, offsetBytes, data);
}

export function updateIndexBuffer(id, offsetBytes, data) {
    const buf = buffers.get(id);
    if (!buf) return;

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, buf.glBuffer);
    gl.bufferSubData(gl.ELEMENT_ARRAY_BUFFER, offsetBytes, data);
}

export function bindVertexBuffer(id) {
    const buf = buffers.get(id);
    if (!buf) return;

    if (boundVertexBuffer === buf.glBuffer) return;
    boundVertexBuffer = buf.glBuffer;

    gl.bindBuffer(gl.ARRAY_BUFFER, buf.glBuffer);

    // Setup vertex attributes for MeshVertex layout (64 bytes total)
    const stride = 64;

    // in_position: vec2 at offset 0
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);

    // in_uv: vec2 at offset 8
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);

    // in_normal: vec2 at offset 16
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 2, gl.FLOAT, false, stride, 16);

    // in_color: vec4 at offset 24
    gl.enableVertexAttribArray(3);
    gl.vertexAttribPointer(3, 4, gl.FLOAT, false, stride, 24);

    // in_bone: int at offset 40
    gl.enableVertexAttribArray(4);
    gl.vertexAttribIPointer(4, 1, gl.INT, stride, 40);

    // in_atlas: int at offset 44
    gl.enableVertexAttribArray(5);
    gl.vertexAttribIPointer(5, 1, gl.INT, stride, 44);

    // in_frame_count: int at offset 48
    gl.enableVertexAttribArray(6);
    gl.vertexAttribIPointer(6, 1, gl.INT, stride, 48);

    // in_frame_width: float at offset 52
    gl.enableVertexAttribArray(7);
    gl.vertexAttribPointer(7, 1, gl.FLOAT, false, stride, 52);

    // in_frame_rate: float at offset 56
    gl.enableVertexAttribArray(8);
    gl.vertexAttribPointer(8, 1, gl.FLOAT, false, stride, 56);

    // in_frame_time: float at offset 60
    gl.enableVertexAttribArray(9);
    gl.vertexAttribPointer(9, 1, gl.FLOAT, false, stride, 60);
}

export function bindIndexBuffer(id) {
    const buf = buffers.get(id);
    if (!buf) return;

    if (boundIndexBuffer === buf.glBuffer) return;
    boundIndexBuffer = buf.glBuffer;

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, buf.glBuffer);
}

// === Uniform Buffer Management ===

const uniformBuffers = new Map(); // id -> WebGLBuffer

export function createUniformBuffer(sizeInBytes, usage) {
    const glBuffer = gl.createBuffer();
    gl.bindBuffer(gl.UNIFORM_BUFFER, glBuffer);
    gl.bufferData(gl.UNIFORM_BUFFER, sizeInBytes, toGLUsage(usage));

    const id = nextBufferId++;
    uniformBuffers.set(id, glBuffer);
    return id;
}

export function updateUniformBuffer(id, offsetBytes, data) {
    const glBuffer = uniformBuffers.get(id);
    if (!glBuffer) return;

    gl.bindBuffer(gl.UNIFORM_BUFFER, glBuffer);
    gl.bufferSubData(gl.UNIFORM_BUFFER, offsetBytes, new Uint8Array(data));
}

export function bindUniformBuffer(id, slot) {
    const glBuffer = uniformBuffers.get(id);
    if (!glBuffer) return;

    gl.bindBufferBase(gl.UNIFORM_BUFFER, slot, glBuffer);
}

// === Texture Management ===

// TextureFormat enum values (must match C# TextureFormat)
const TextureFormat = {
    RGBA8: 0,
    RGB8: 1,
    R8: 2,
    RG8: 3,
    RGBA32F: 4
};

// TextureFilter enum values
const TextureFilter = {
    Nearest: 0,
    Linear: 1
};

// Store texture info for UpdateTexture
const textureInfo = new Map(); // id -> { format, width, height }

export function createTexture(width, height, data, format = TextureFormat.RGBA8, filter = TextureFilter.Linear) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);

    let internalFormat, pixelFormat, pixelType, typedData;

    switch (format) {
        case TextureFormat.R8:
            internalFormat = gl.R8;
            pixelFormat = gl.RED;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = data && data.length > 0 ? new Uint8Array(data) : null;
            break;
        case TextureFormat.RG8:
            internalFormat = gl.RG8;
            pixelFormat = gl.RG;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = data && data.length > 0 ? new Uint8Array(data) : null;
            break;
        case TextureFormat.RGB8:
            internalFormat = gl.RGB8;
            pixelFormat = gl.RGB;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = data && data.length > 0 ? new Uint8Array(data) : null;
            break;
        case TextureFormat.RGBA32F:
            internalFormat = gl.RGBA32F;
            pixelFormat = gl.RGBA;
            pixelType = gl.FLOAT;
            // Data comes as byte array, need to convert to Float32Array
            typedData = data && data.length > 0 ? new Float32Array(new Uint8Array(data).buffer) : null;
            break;
        case TextureFormat.RGBA8:
        default:
            internalFormat = gl.RGBA8;
            pixelFormat = gl.RGBA;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = data && data.length > 0 ? new Uint8Array(data) : null;
            break;
    }

    gl.texImage2D(gl.TEXTURE_2D, 0, internalFormat, width, height, 0, pixelFormat, pixelType, typedData);

    const minFilter = filter === TextureFilter.Nearest ? gl.NEAREST : gl.LINEAR;
    const magFilter = filter === TextureFilter.Nearest ? gl.NEAREST : gl.LINEAR;
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, minFilter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, magFilter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    const id = nextTextureId++;
    textures.set(id, texture);
    textureInfo.set(id, { format, width, height });
    return id;
}

export function destroyTexture(id) {
    const tex = textures.get(id);
    if (tex) {
        gl.deleteTexture(tex);
        textures.delete(id);
    }
}

export function bindTexture(slot, id) {
    const tex = textures.get(id);
    if (!tex) return;

    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D, tex);
}

// === Shader Management ===

function createShaderInternal(name, vertexSource, fragmentSource) {
    // Compile vertex shader
    const vertexShader = gl.createShader(gl.VERTEX_SHADER);
    gl.shaderSource(vertexShader, vertexSource);
    gl.compileShader(vertexShader);

    if (!gl.getShaderParameter(vertexShader, gl.COMPILE_STATUS)) {
        const info = gl.getShaderInfoLog(vertexShader);
        gl.deleteShader(vertexShader);
        throw new Error(`[${name}] Vertex shader: ${info}`);
    }

    // Compile fragment shader
    const fragmentShader = gl.createShader(gl.FRAGMENT_SHADER);
    gl.shaderSource(fragmentShader, fragmentSource);
    gl.compileShader(fragmentShader);

    if (!gl.getShaderParameter(fragmentShader, gl.COMPILE_STATUS)) {
        const info = gl.getShaderInfoLog(fragmentShader);
        gl.deleteShader(vertexShader);
        gl.deleteShader(fragmentShader);
        throw new Error(`[${name}] Fragment shader: ${info}`);
    }

    // Link program
    const program = gl.createProgram();
    gl.attachShader(program, vertexShader);
    gl.attachShader(program, fragmentShader);

    gl.linkProgram(program);

    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const info = gl.getProgramInfoLog(program);
        gl.deleteShader(vertexShader);
        gl.deleteShader(fragmentShader);
        gl.deleteProgram(program);
        throw new Error(`[${name}] Link: ${info}`);
    }

    gl.detachShader(program, vertexShader);
    gl.detachShader(program, fragmentShader);
    gl.deleteShader(vertexShader);
    gl.deleteShader(fragmentShader);

    return { program, uniformLocations: new Map() };
}

export function createShader(name, vertexSource, fragmentSource) {
    const shader = createShaderInternal(name, vertexSource, fragmentSource);
    const id = nextShaderId++;
    shaders.set(id, shader);
    return id;
}

export function destroyShader(id) {
    const shader = shaders.get(id);
    if (shader) {
        gl.deleteProgram(shader.program);
        shaders.delete(id);
    }
}

export function bindShader(id) {
    const shader = shaders.get(id);
    if (!shader) return;

    if (boundShader === shader.program) return;
    boundShader = shader.program;

    gl.useProgram(shader.program);
}

// === Texture Array Management ===

let textureArrays = new Map(); // id -> { texture, width, height, layers }

export function createTextureArray(width, height, layers) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D_ARRAY, texture);
    gl.texStorage3D(gl.TEXTURE_2D_ARRAY, 1, gl.RGBA8, width, height, layers);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    const id = nextTextureId++;
    textureArrays.set(id, { texture, width, height, layers });
    return id;
}

export function updateTextureArrayLayer(id, layer, data) {
    const arr = textureArrays.get(id);
    if (!arr) return;

    gl.bindTexture(gl.TEXTURE_2D_ARRAY, arr.texture);
    gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, 0, 0, 0, layer, arr.width, arr.height, 1, gl.RGBA, gl.UNSIGNED_BYTE, data);
}

export function bindTextureArray(slot, id) {
    const arr = textureArrays.get(id);
    if (!arr) return;

    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D_ARRAY, arr.texture);
}

export function updateTexture(id, width, height, data) {
    const tex = textures.get(id);
    if (!tex) return;

    const info = textureInfo.get(id);
    const format = info ? info.format : TextureFormat.RGBA8;

    let pixelFormat, pixelType, typedData;

    switch (format) {
        case TextureFormat.R8:
            pixelFormat = gl.RED;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = new Uint8Array(data);
            break;
        case TextureFormat.RG8:
            pixelFormat = gl.RG;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = new Uint8Array(data);
            break;
        case TextureFormat.RGB8:
            pixelFormat = gl.RGB;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = new Uint8Array(data);
            break;
        case TextureFormat.RGBA32F:
            pixelFormat = gl.RGBA;
            pixelType = gl.FLOAT;
            // Data comes as byte array, convert to Float32Array
            typedData = new Float32Array(new Uint8Array(data).buffer);
            break;
        case TextureFormat.RGBA8:
        default:
            pixelFormat = gl.RGBA;
            pixelType = gl.UNSIGNED_BYTE;
            typedData = new Uint8Array(data);
            break;
    }

    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, width, height, pixelFormat, pixelType, typedData);
}

// === State Management ===

export function setBlendMode(mode) {
    switch (mode) {
        case 0: // None
            gl.disable(gl.BLEND);
            break;
        case 1: // Alpha
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            break;
        case 2: // Additive
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
            break;
        case 3: // Multiply
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.DST_COLOR, gl.ZERO);
            break;
        case 4: // Premultiplied
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
            break;
    }
}

// === Drawing ===

export function drawElements(firstIndex, indexCount, baseVertex = 0) {
    // Note: WebGL2 doesn't have native drawElementsBaseVertex - indices are adjusted on CPU if needed
    void baseVertex; // unused, indices adjusted on CPU
    gl.drawElements(gl.TRIANGLES, indexCount, gl.UNSIGNED_SHORT, firstIndex * 2);
}

// === Synchronization ===

export function createFence() {
    const fence = gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE, 0);
    const id = nextFenceId++;
    fences.set(id, fence);
    return id;
}

export function waitFence(id) {
    const fence = fences.get(id);
    if (!fence) return;

    // Wait with 1 second timeout
    gl.clientWaitSync(fence, gl.SYNC_FLUSH_COMMANDS_BIT, 1000000000);
}

export function deleteFence(id) {
    const fence = fences.get(id);
    if (fence) {
        gl.deleteSync(fence);
        fences.delete(id);
    }
}

// === Helpers ===

function toGLUsage(usage) {
    switch (usage) {
        case 0: return gl.STATIC_DRAW;  // Static
        case 1: return gl.DYNAMIC_DRAW; // Dynamic
        case 2: return gl.STREAM_DRAW;  // Stream
        default: return gl.DYNAMIC_DRAW;
    }
}

// Get the WebGL context for advanced use
export function getContext() {
    return gl;
}

export function isWebGL2Supported() {
    return true;
}
