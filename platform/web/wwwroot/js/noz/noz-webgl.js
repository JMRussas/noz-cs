// NoZ WebGL Render Backend

let gl = null;
let canvas = null;
let isWebGL2 = false;

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

    // Try WebGL2 first, fall back to WebGL1
    gl = canvas.getContext('webgl2');
    if (gl) {
        isWebGL2 = true;
        console.log('NoZ: Using WebGL 2.0');
    } else {
        gl = canvas.getContext('webgl');
        if (!gl) {
            throw new Error('WebGL not supported');
        }
        isWebGL2 = false;
        console.log('NoZ: Using WebGL 1.0');
    }

    // Set default state
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    // Create VAO for mesh vertex format (WebGL2 only)
    if (isWebGL2) {
        meshVao = gl.createVertexArray();
        gl.bindVertexArray(meshVao);
    }

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
    if (isWebGL2) {
        fences.forEach((fence) => gl.deleteSync(fence));
        fences.clear();
        if (meshVao) gl.deleteVertexArray(meshVao);
    }
    gl = null;
}

export function beginFrame() {
    if (isWebGL2 && meshVao) {
        gl.bindVertexArray(meshVao);
    }
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

export function updateVertexBufferRange(id, offsetBytes, data) {
    const buf = buffers.get(id);
    if (!buf) return;

    gl.bindBuffer(gl.ARRAY_BUFFER, buf.glBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, offsetBytes, data);
}

export function updateIndexBufferRange(id, offsetBytes, data) {
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

    // Setup vertex attributes for MeshVertex layout (68 bytes total)
    const stride = 68;

    // Position: vec2 at offset 0
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);

    // UV: vec2 at offset 8
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);

    // Normal: vec2 at offset 16
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 2, gl.FLOAT, false, stride, 16);

    // Color: vec4 at offset 24
    gl.enableVertexAttribArray(3);
    gl.vertexAttribPointer(3, 4, gl.FLOAT, false, stride, 24);

    // Opacity: float at offset 40
    gl.enableVertexAttribArray(4);
    gl.vertexAttribPointer(4, 1, gl.FLOAT, false, stride, 40);

    if (isWebGL2) {
        // Bone: int at offset 44
        gl.enableVertexAttribArray(5);
        gl.vertexAttribIPointer(5, 1, gl.INT, stride, 44);

        // Atlas: int at offset 48
        gl.enableVertexAttribArray(6);
        gl.vertexAttribIPointer(6, 1, gl.INT, stride, 48);

        // FrameCount: int at offset 52
        gl.enableVertexAttribArray(7);
        gl.vertexAttribIPointer(7, 1, gl.INT, stride, 52);

        // FrameWidth: float at offset 56
        gl.enableVertexAttribArray(8);
        gl.vertexAttribPointer(8, 1, gl.FLOAT, false, stride, 56);

        // FrameRate: float at offset 60
        gl.enableVertexAttribArray(9);
        gl.vertexAttribPointer(9, 1, gl.FLOAT, false, stride, 60);

        // AnimStartTime: float at offset 64
        gl.enableVertexAttribArray(10);
        gl.vertexAttribPointer(10, 1, gl.FLOAT, false, stride, 64);
    }
}

export function bindIndexBuffer(id) {
    const buf = buffers.get(id);
    if (!buf) return;

    if (boundIndexBuffer === buf.glBuffer) return;
    boundIndexBuffer = buf.glBuffer;

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, buf.glBuffer);
}

// === Texture Management ===

export function createTexture(width, height, data) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    const id = nextTextureId++;
    textures.set(id, texture);
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

    // Bind attribute locations for WebGL1
    if (!isWebGL2) {
        gl.bindAttribLocation(program, 0, 'aPosition');
        gl.bindAttribLocation(program, 1, 'aUV');
        gl.bindAttribLocation(program, 2, 'aNormal');
        gl.bindAttribLocation(program, 3, 'aColor');
        gl.bindAttribLocation(program, 4, 'aOpacity');
        gl.bindAttribLocation(program, 5, 'aDepth');
    }

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

export function setUniformMatrix4x4(name, values) {
    if (!boundShader) return;

    // Find the shader that's bound
    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    // Cache uniform location
    let location = shader.uniformLocations.get(name);
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, name);
        shader.uniformLocations.set(name, location);
    }
    if (location === null) return;

    gl.uniformMatrix4fv(location, false, values);
}

export function setUniformInt(name, value) {
    if (!boundShader) return;

    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    let location = shader.uniformLocations.get(name);
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, name);
        shader.uniformLocations.set(name, location);
    }
    if (location === null) return;

    gl.uniform1i(location, value);
}

export function setUniformFloat(name, value) {
    if (!boundShader) return;

    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    let location = shader.uniformLocations.get(name);
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, name);
        shader.uniformLocations.set(name, location);
    }
    if (location === null) return;

    gl.uniform1f(location, value);
}

export function setUniformVec2(name, x, y) {
    if (!boundShader) return;

    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    let location = shader.uniformLocations.get(name);
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, name);
        shader.uniformLocations.set(name, location);
    }
    if (location === null) return;

    gl.uniform2f(location, x, y);
}

export function setUniformVec4(name, x, y, z, w) {
    if (!boundShader) return;

    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    let location = shader.uniformLocations.get(name);
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, name);
        shader.uniformLocations.set(name, location);
    }
    if (location === null) return;

    gl.uniform4f(location, x, y, z, w);
}

export function setBoneTransforms(data) {
    if (!boundShader || !isWebGL2) return;

    let shader = null;
    for (const [, s] of shaders) {
        if (s.program === boundShader) {
            shader = s;
            break;
        }
    }
    if (!shader) return;

    let location = shader.uniformLocations.get('uBones');
    if (location === undefined) {
        location = gl.getUniformLocation(shader.program, 'uBones');
        shader.uniformLocations.set('uBones', location);
    }
    if (location === null) return;

    gl.uniform3fv(location, data);
}

// === Texture Array Management ===

let textureArrays = new Map(); // id -> { texture, width, height, layers }

export function createTextureArray(width, height, layers) {
    if (!isWebGL2) return 0;

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
    if (!isWebGL2) return;

    const arr = textureArrays.get(id);
    if (!arr) return;

    gl.bindTexture(gl.TEXTURE_2D_ARRAY, arr.texture);
    gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, 0, 0, 0, layer, arr.width, arr.height, 1, gl.RGBA, gl.UNSIGNED_BYTE, data);
}

export function bindTextureArray(slot, id) {
    if (!isWebGL2) return;

    const arr = textureArrays.get(id);
    if (!arr) return;

    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D_ARRAY, arr.texture);
}

export function updateTexture(id, width, height, data) {
    const tex = textures.get(id);
    if (!tex) return;

    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, data);
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

export function drawIndexedRange(firstIndex, indexCount, baseVertex = 0) {
    if (baseVertex === 0 || !isWebGL2) {
        gl.drawElements(gl.TRIANGLES, indexCount, gl.UNSIGNED_SHORT, firstIndex * 2);
    } else {
        // WebGL2 supports drawElementsBaseVertex via extension
        // Note: This requires WEBGL_draw_instanced_base_vertex_base_instance extension
        // For now, fall back to regular drawElements (indices should be adjusted on CPU if needed)
        // TODO: Use extension if available
        gl.drawElements(gl.TRIANGLES, indexCount, gl.UNSIGNED_SHORT, firstIndex * 2);
    }
}

// === Synchronization ===

export function createFence() {
    if (!isWebGL2) return 0; // No fence support in WebGL1

    const fence = gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE, 0);
    const id = nextFenceId++;
    fences.set(id, fence);
    return id;
}

export function waitFence(id) {
    if (!isWebGL2) return;

    const fence = fences.get(id);
    if (!fence) return;

    // Wait with 1 second timeout
    gl.clientWaitSync(fence, gl.SYNC_FLUSH_COMMANDS_BIT, 1000000000);
}

export function deleteFence(id) {
    if (!isWebGL2) return;

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
    return isWebGL2;
}
