// NoZ WebGPU Bridge - Browser Implementation
// Bridges .NET WASM to browser WebGPU API
// Version: 2 (blend mode fix)

// Core WebGPU state
let gpu = null;
let adapter = null;
let device = null;
let queue = null;
let canvas = null;
let context = null;
let presentFormat = null;
let surfaceWidth = 0;
let surfaceHeight = 0;

// Current frame state
let currentCommandEncoder = null;
let currentRenderPass = null;
let currentSurfaceTexture = null;
let currentSurfaceTextureView = null;

// Resource maps (ID -> WebGPU object)
const buffers = new Map();
const textures = new Map();
const textureViews = new Map();
const samplers = new Map();
const shaderModules = new Map();
const bindGroupLayouts = new Map();
const pipelineLayouts = new Map();
const renderPipelines = new Map();
const bindGroups = new Map();

let nextBufferId = 1;
let nextTextureId = 2; // 1 reserved for white texture
let nextShaderId = 1;
let nextPipelineId = 1;
let nextBindGroupId = 1;
let nextSamplerId = 1;

// Global samplers
let linearSampler = null;
let nearestSampler = null;

// ============================================================================
// Initialization
// ============================================================================

export async function init(canvasSelector) {
    if (!navigator.gpu) {
        throw new Error("WebGPU not supported in this browser");
    }

    gpu = navigator.gpu;
    adapter = await gpu.requestAdapter({ powerPreference: "high-performance" });

    if (!adapter) {
        throw new Error("Failed to get WebGPU adapter");
    }

    device = await adapter.requestDevice();

    if (!device) {
        throw new Error("Failed to get WebGPU device");
    }

    queue = device.queue;

    canvas = document.querySelector(canvasSelector);
    if (!canvas) {
        throw new Error(`Canvas not found: ${canvasSelector}`);
    }

    context = canvas.getContext("webgpu");
    presentFormat = gpu.getPreferredCanvasFormat();

    surfaceWidth = canvas.width;
    surfaceHeight = canvas.height;

    context.configure({
        device: device,
        format: presentFormat,
        alphaMode: "premultiplied"
    });

    // Create global samplers
    linearSampler = device.createSampler({
        magFilter: "linear",
        minFilter: "linear",
        mipmapFilter: "nearest",
        addressModeU: "clamp-to-edge",
        addressModeV: "clamp-to-edge",
        addressModeW: "clamp-to-edge"
    });

    nearestSampler = device.createSampler({
        magFilter: "nearest",
        minFilter: "nearest",
        mipmapFilter: "nearest",
        addressModeU: "clamp-to-edge",
        addressModeV: "clamp-to-edge",
        addressModeW: "clamp-to-edge"
    });

    return {
        width: surfaceWidth,
        height: surfaceHeight,
        format: presentFormat
    };
}

export function shutdown() {
    // Resources are automatically cleaned up when device is lost
    device = null;
    adapter = null;
    gpu = null;
    context = null;
    canvas = null;

    buffers.clear();
    textures.clear();
    textureViews.clear();
    samplers.clear();
    shaderModules.clear();
    bindGroupLayouts.clear();
    pipelineLayouts.clear();
    renderPipelines.clear();
    bindGroups.clear();
}

export function getSurfaceSize() {
    return { width: surfaceWidth, height: surfaceHeight };
}

export function checkResize() {
    const newWidth = canvas.width;
    const newHeight = canvas.height;

    if (newWidth !== surfaceWidth || newHeight !== surfaceHeight) {
        surfaceWidth = newWidth;
        surfaceHeight = newHeight;

        context.configure({
            device: device,
            format: presentFormat,
            alphaMode: "premultiplied"
        });

        return true;
    }
    return false;
}

// ============================================================================
// Buffer Management
// ============================================================================

export function createBuffer(size, usage, label) {
    const id = nextBufferId++;
    const buffer = device.createBuffer({
        size: size,
        usage: usage,
        label: label || `buffer_${id}`,
        mappedAtCreation: false
    });
    buffers.set(id, buffer);
    return id;
}

export function writeBuffer(bufferId, offset, data) {
    const buffer = buffers.get(bufferId);
    if (!buffer) {
        console.error(`Buffer ${bufferId} not found`);
        return;
    }
    // Data from C# MemoryView may be a view into WASM memory
    // Convert to a proper typed array that WebGPU can use
    const typedData = ensureTypedArray(data);
    if (typedData && typedData.byteLength > 0) {
        queue.writeBuffer(buffer, offset, typedData);
    }
}

// Helper to ensure data is a proper typed array for WebGPU
function ensureTypedArray(data) {
    if (!data) return null;
    // If it's already a typed array with a buffer, use it directly
    if (data instanceof Uint8Array || data instanceof Float32Array) {
        return data;
    }
    // If it has a slice method (Span/MemoryView), convert to Uint8Array
    if (data.slice) {
        return new Uint8Array(data.slice());
    }
    // If it has buffer property (ArrayBufferView)
    if (data.buffer instanceof ArrayBuffer) {
        return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
    }
    // If it's an ArrayBuffer
    if (data instanceof ArrayBuffer) {
        return new Uint8Array(data);
    }
    console.warn('Unknown data type for buffer write:', typeof data, data);
    return null;
}

export function destroyBuffer(bufferId) {
    const buffer = buffers.get(bufferId);
    if (buffer) {
        buffer.destroy();
        buffers.delete(bufferId);
    }
}

// ============================================================================
// Mesh Management (Vertex + Index Buffer pairs)
// ============================================================================

export function createMesh(maxVertices, maxIndices, vertexStride, label) {
    const vertexSize = maxVertices * vertexStride;
    const indexSize = maxIndices * 2; // uint16

    const vertexBuffer = device.createBuffer({
        size: vertexSize,
        usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        label: `${label || 'mesh'}_vertices`
    });

    const indexBuffer = device.createBuffer({
        size: indexSize,
        usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST,
        label: `${label || 'mesh'}_indices`
    });

    const id = nextBufferId++;
    buffers.set(id, {
        type: 'mesh',
        vertexBuffer: vertexBuffer,
        indexBuffer: indexBuffer,
        stride: vertexStride,
        maxVertices: maxVertices,
        maxIndices: maxIndices
    });

    return id;
}

export function updateMesh(meshId, vertexData, indexData) {
    const mesh = buffers.get(meshId);
    if (!mesh || mesh.type !== 'mesh') {
        console.error(`Mesh ${meshId} not found`);
        return;
    }

    const vData = ensureTypedArray(vertexData);
    if (vData && vData.byteLength > 0) {
        queue.writeBuffer(mesh.vertexBuffer, 0, vData);
    }

    const iData = ensureTypedArray(indexData);
    if (iData && iData.byteLength > 0) {
        queue.writeBuffer(mesh.indexBuffer, 0, iData);
    }
}

export function destroyMesh(meshId) {
    const mesh = buffers.get(meshId);
    if (mesh && mesh.type === 'mesh') {
        mesh.vertexBuffer.destroy();
        mesh.indexBuffer.destroy();
        buffers.delete(meshId);
    }
}

// ============================================================================
// Texture Management
// ============================================================================

const formatMap = {
    'rgba8': 'rgba8unorm',
    'r8': 'r8unorm',
    'rg8': 'rg8unorm',
    'rgba32f': 'rgba32float',
    'bgra8': 'bgra8unorm'
};

export function createTexture(width, height, format, usage, label) {
    const id = nextTextureId++;

    const gpuFormat = formatMap[format] || 'rgba8unorm';

    const texture = device.createTexture({
        size: { width, height, depthOrArrayLayers: 1 },
        format: gpuFormat,
        usage: usage,
        label: label || `texture_${id}`
    });

    const textureView = texture.createView({
        format: gpuFormat,
        dimension: '2d'
    });

    textures.set(id, {
        texture: texture,
        view: textureView,
        width: width,
        height: height,
        format: gpuFormat,
        layers: 1,
        isArray: false
    });

    return id;
}

export function createTextureArray(width, height, layers, format, label) {
    const id = nextTextureId++;

    const gpuFormat = formatMap[format] || 'rgba8unorm';

    const texture = device.createTexture({
        size: { width, height, depthOrArrayLayers: layers },
        format: gpuFormat,
        usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
        label: label || `texture_array_${id}`
    });

    const textureView = texture.createView({
        format: gpuFormat,
        dimension: '2d-array',
        arrayLayerCount: layers
    });

    textures.set(id, {
        texture: texture,
        view: textureView,
        width: width,
        height: height,
        format: gpuFormat,
        layers: layers,
        isArray: true
    });

    return id;
}

export function writeTexture(textureId, data, width, height, bytesPerRow, layer) {
    const tex = textures.get(textureId);
    if (!tex) {
        console.error(`Texture ${textureId} not found`);
        return;
    }

    const typedData = ensureTypedArray(data);
    if (!typedData || typedData.byteLength === 0) return;

    queue.writeTexture(
        {
            texture: tex.texture,
            origin: { x: 0, y: 0, z: layer || 0 }
        },
        typedData,
        { bytesPerRow: bytesPerRow, rowsPerImage: height },
        { width, height, depthOrArrayLayers: 1 }
    );
}

export function writeTextureRegion(textureId, data, x, y, width, height, bytesPerRow) {
    const tex = textures.get(textureId);
    if (!tex) {
        console.error(`Texture ${textureId} not found`);
        return;
    }

    const typedData = ensureTypedArray(data);
    if (!typedData || typedData.byteLength === 0) return;

    queue.writeTexture(
        {
            texture: tex.texture,
            origin: { x: x, y: y, z: 0 }
        },
        typedData,
        { bytesPerRow: bytesPerRow, rowsPerImage: height },
        { width, height, depthOrArrayLayers: 1 }
    );
}

export function destroyTexture(textureId) {
    const tex = textures.get(textureId);
    if (tex) {
        tex.texture.destroy();
        textures.delete(textureId);
    }
}

export function getTextureView(textureId) {
    const tex = textures.get(textureId);
    return tex ? tex.view : null;
}

// ============================================================================
// Shader Management
// ============================================================================

export function createShaderModule(code, label) {
    const id = nextShaderId++;

    const module = device.createShaderModule({
        code: code,
        label: label || `shader_${id}`
    });

    shaderModules.set(id, module);
    return id;
}

export function destroyShaderModule(shaderId) {
    shaderModules.delete(shaderId);
}

// ============================================================================
// Pipeline Management
// ============================================================================

export function createBindGroupLayout(entries, label) {
    const id = nextPipelineId++;

    // Convert to proper JS array if needed (C# arrays may come as array-like objects)
    const entriesArray = Array.isArray(entries) ? entries : Array.from(entries);

    const layout = device.createBindGroupLayout({
        entries: entriesArray,
        label: label || `bind_group_layout_${id}`
    });

    bindGroupLayouts.set(id, layout);
    return id;
}

export function createPipelineLayout(bindGroupLayoutIds, label) {
    const id = nextPipelineId++;

    // Convert to proper JS array if needed (C# arrays may come as array-like objects)
    const layoutIds = Array.isArray(bindGroupLayoutIds)
        ? bindGroupLayoutIds
        : Array.from(bindGroupLayoutIds);

    const layouts = layoutIds.map(layoutId => {
        const layout = bindGroupLayouts.get(layoutId);
        if (!layout) {
            console.error(`BindGroupLayout ${layoutId} not found`);
        }
        return layout;
    });

    // Filter out any undefined layouts
    const validLayouts = layouts.filter(l => l !== undefined);

    const layout = device.createPipelineLayout({
        bindGroupLayouts: validLayouts,
        label: label || `pipeline_layout_${id}`
    });

    pipelineLayouts.set(id, layout);
    return id;
}

export function createRenderPipeline(descriptor) {
    const id = nextPipelineId++;

    // Resolve references
    const vertexModule = shaderModules.get(descriptor.vertexModuleId);
    const fragmentModule = shaderModules.get(descriptor.fragmentModuleId);
    const pipelineLayout = pipelineLayouts.get(descriptor.pipelineLayoutId);

    // Parse vertex buffers from JSON string (to avoid JSObject proxy issues)
    const vertexBuffers = JSON.parse(descriptor.vertexBuffersJson);

    // Resolve blend mode from string (to avoid JSObject proxy issues)
    // BlendModes['none'] is null, meaning no blending - we must omit the property entirely
    const blendMode = descriptor.blendMode || 'none';
    const blend = BlendModes[blendMode];

    // Build target - only include blend if it's defined (null/undefined means no blending)
    const target = {
        format: descriptor.targetFormat || presentFormat,
        writeMask: GPUColorWrite.ALL
    };
    if (blend) {
        target.blend = blend;
    }

    const pipeline = device.createRenderPipeline({
        layout: pipelineLayout,
        vertex: {
            module: vertexModule,
            entryPoint: descriptor.vertexEntryPoint || 'vs_main',
            buffers: vertexBuffers
        },
        fragment: {
            module: fragmentModule,
            entryPoint: descriptor.fragmentEntryPoint || 'fs_main',
            targets: [target]
        },
        primitive: {
            topology: descriptor.topology || 'triangle-list',
            cullMode: descriptor.cullMode || 'none',
            frontFace: descriptor.frontFace || 'ccw'
        },
        multisample: {
            count: descriptor.sampleCount || 1
        },
        label: descriptor.label || `render_pipeline_${id}`
    });

    renderPipelines.set(id, pipeline);
    return id;
}

export function destroyRenderPipeline(pipelineId) {
    renderPipelines.delete(pipelineId);
}

// ============================================================================
// Bind Group Management
// ============================================================================

export function createBindGroup(layoutId, entries, label) {
    const id = nextBindGroupId++;

    const layout = bindGroupLayouts.get(layoutId);
    if (!layout) {
        console.error(`BindGroupLayout ${layoutId} not found for bind group ${label || id}`);
        return -1;
    }

    // Convert to proper JS array if needed (C# arrays may come as array-like objects)
    const entriesArray = Array.isArray(entries) ? entries : Array.from(entries);

    // Resolve resource references in entries
    const resolvedEntries = entriesArray.map(entry => {
        const resolved = { binding: entry.binding };

        if (entry.bufferId !== undefined && entry.bufferId !== null) {
            let buffer = buffers.get(entry.bufferId);
            if (!buffer) {
                console.error(`Buffer ${entry.bufferId} not found for binding ${entry.binding}`);
                return null;
            }
            if (buffer.type === 'mesh') {
                buffer = buffer.vertexBuffer;
            }
            resolved.resource = {
                buffer: buffer,
                offset: entry.offset || 0,
                size: entry.size
            };
        } else if (entry.textureViewId !== undefined && entry.textureViewId !== null) {
            const tex = textures.get(entry.textureViewId);
            if (!tex) {
                console.error(`Texture ${entry.textureViewId} not found for binding ${entry.binding}`);
                return null;
            }
            resolved.resource = tex.view;
        } else if (entry.samplerId !== undefined && entry.samplerId !== null) {
            resolved.resource = entry.samplerId === 1 ? linearSampler : nearestSampler;
        } else if (entry.useLinearSampler !== undefined && entry.useLinearSampler !== null) {
            resolved.resource = entry.useLinearSampler ? linearSampler : nearestSampler;
        } else {
            console.error(`Entry at binding ${entry.binding} has no recognized resource type`);
            return null;
        }

        return resolved;
    }).filter(e => e !== null);

    const bindGroup = device.createBindGroup({
        layout: layout,
        entries: resolvedEntries,
        label: label || `bind_group_${id}`
    });

    bindGroups.set(id, bindGroup);
    return id;
}

export function destroyBindGroup(bindGroupId) {
    bindGroups.delete(bindGroupId);
}

// Alternative createBindGroup that takes JSON string (more reliable than JSObject array marshalling)
export function createBindGroupFromJson(layoutId, entriesJson, label) {
    const id = nextBindGroupId++;

    const layout = bindGroupLayouts.get(layoutId);
    if (!layout) {
        console.error(`BindGroupLayout ${layoutId} not found for bind group ${label || id}`);
        return -1;
    }

    // Parse entries from JSON
    let entries;
    try {
        entries = JSON.parse(entriesJson);
    } catch (e) {
        console.error(`Failed to parse bind group entries JSON:`, e, entriesJson);
        return -1;
    }

    // Resolve resource references in entries
    const resolvedEntries = entries.map(entry => {
        const resolved = { binding: entry.binding };

        if (entry.type === 'buffer') {
            let buffer = buffers.get(entry.bufferId);
            if (!buffer) {
                console.error(`Buffer ${entry.bufferId} not found for binding ${entry.binding}`);
                return null;
            }
            resolved.resource = {
                buffer: buffer,
                offset: entry.offset || 0,
                size: entry.size
            };
        } else if (entry.type === 'texture') {
            const tex = textures.get(entry.textureId);
            if (!tex) {
                console.error(`Texture ${entry.textureId} not found for binding ${entry.binding}`);
                return null;
            }
            resolved.resource = tex.view;
        } else if (entry.type === 'sampler') {
            resolved.resource = entry.useLinear ? linearSampler : nearestSampler;
        }

        return resolved;
    }).filter(e => e !== null);

    const bindGroup = device.createBindGroup({
        layout: layout,
        entries: resolvedEntries,
        label: label || `bind_group_${id}`
    });

    bindGroups.set(id, bindGroup);
    return id;
}

// ============================================================================
// Frame Management
// ============================================================================

export function beginFrame() {
    // Check for resize
    checkResize();

    // Get current surface texture
    currentSurfaceTexture = context.getCurrentTexture();

    if (!currentSurfaceTexture) {
        console.error('beginFrame: failed to get surface texture');
        return false;
    }

    // Create command encoder
    currentCommandEncoder = device.createCommandEncoder({
        label: 'frame_command_encoder'
    });

    return true;
}

export function endFrame() {
    if (!currentCommandEncoder) return;

    // Submit commands
    const commandBuffer = currentCommandEncoder.finish();
    queue.submit([commandBuffer]);

    currentCommandEncoder = null;
    currentSurfaceTexture = null;
    currentSurfaceTextureView = null;
}

// ============================================================================
// Render Pass Management
// ============================================================================

export function beginRenderPass(colorAttachments, depthAttachment, label) {
    if (!currentCommandEncoder) {
        console.error("No command encoder - call beginFrame first");
        return false;
    }

    // Convert to proper JS array if needed (C# arrays may come as array-like objects)
    const attachmentsArray = Array.isArray(colorAttachments) ? colorAttachments : Array.from(colorAttachments);

    const resolvedColorAttachments = attachmentsArray.map(att => {
        let view;
        if (att.textureId === -1) {
            // Use current surface texture
            view = currentSurfaceTexture.createView();
            currentSurfaceTextureView = view;
        } else {
            const tex = textures.get(att.textureId);
            view = tex ? tex.view : null;
        }

        let resolveTarget;
        if (att.resolveTextureId) {
            const tex = textures.get(att.resolveTextureId);
            resolveTarget = tex ? tex.view : null;
        }

        return {
            view: view,
            resolveTarget: resolveTarget,
            loadOp: att.loadOp || 'clear',
            storeOp: att.storeOp || 'store',
            clearValue: att.clearValue || { r: 0, g: 0, b: 0, a: 1 }
        };
    });

    // Only include depthStencilAttachment if it's a valid object (not null/undefined/empty proxy)
    const renderPassDescriptor = {
        colorAttachments: resolvedColorAttachments,
        label: label || 'render_pass'
    };

    // Check if depthAttachment is a valid object with required properties
    if (depthAttachment && typeof depthAttachment === 'object' && depthAttachment.view) {
        renderPassDescriptor.depthStencilAttachment = depthAttachment;
    }

    currentRenderPass = currentCommandEncoder.beginRenderPass(renderPassDescriptor);

    return true;
}

export function endRenderPass() {
    if (currentRenderPass) {
        currentRenderPass.end();
        currentRenderPass = null;
    }

    if (currentSurfaceTextureView) {
        currentSurfaceTextureView = null;
    }
}

// ============================================================================
// Render Commands
// ============================================================================

export function setViewport(x, y, width, height, minDepth, maxDepth) {
    if (currentRenderPass) {
        currentRenderPass.setViewport(x, y, width, height, minDepth || 0, maxDepth || 1);
    }
}

export function setScissorRect(x, y, width, height) {
    if (currentRenderPass) {
        currentRenderPass.setScissorRect(x, y, width, height);
    }
}

export function setPipeline(pipelineId) {
    if (currentRenderPass) {
        const pipeline = renderPipelines.get(pipelineId);
        if (pipeline) {
            currentRenderPass.setPipeline(pipeline);
        }
    }
}

export function setBindGroup(index, bindGroupId) {
    if (currentRenderPass) {
        const bindGroup = bindGroups.get(bindGroupId);
        if (bindGroup) {
            currentRenderPass.setBindGroup(index, bindGroup);
        }
    }
}

export function setVertexBuffer(slot, meshId) {
    if (currentRenderPass) {
        const mesh = buffers.get(meshId);
        if (mesh && mesh.type === 'mesh') {
            currentRenderPass.setVertexBuffer(slot, mesh.vertexBuffer);
        }
    }
}

export function setIndexBuffer(meshId) {
    if (currentRenderPass) {
        const mesh = buffers.get(meshId);
        if (mesh && mesh.type === 'mesh') {
            currentRenderPass.setIndexBuffer(mesh.indexBuffer, 'uint16');
        }
    }
}

export function drawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance) {
    if (currentRenderPass) {
        currentRenderPass.drawIndexed(
            indexCount,
            instanceCount || 1,
            firstIndex || 0,
            baseVertex || 0,
            firstInstance || 0
        );
    } else {
        console.warn('drawIndexed called without active render pass!');
    }
}

export function draw(vertexCount, instanceCount, firstVertex, firstInstance) {
    if (currentRenderPass) {
        currentRenderPass.draw(
            vertexCount,
            instanceCount || 1,
            firstVertex || 0,
            firstInstance || 0
        );
    }
}

// ============================================================================
// Offscreen Rendering
// ============================================================================

// ============================================================================
// Render Texture (for capturing to image)
// ============================================================================

const renderTextures = new Map();
let currentRenderTexturePass = null;

export function createRenderTexture(width, height, format, label) {
    // Allocate from shared texture ID space so RT can be used with bind groups
    const id = nextTextureId++;
    const gpuFormat = formatMap[format] || 'rgba8unorm';

    const texture = device.createTexture({
        size: { width, height, depthOrArrayLayers: 1 },
        format: gpuFormat,
        // RenderAttachment for drawing, CopySrc for readback, TextureBinding for sampling
        usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC | GPUTextureUsage.TEXTURE_BINDING,
        label: label || `render_texture_${id}`
    });

    // D2 view for render pass color attachment
    const view = texture.createView({ format: gpuFormat, dimension: '2d' });

    renderTextures.set(id, {
        texture: texture,
        view: view,
        width: width,
        height: height,
        format: gpuFormat
    });

    // D2Array view for sampling (sprite shader expects texture_2d_array)
    const arrayView = texture.createView({ format: gpuFormat, dimension: '2d-array', arrayLayerCount: 1 });

    textures.set(id, {
        texture: texture,
        view: arrayView,
        width: width,
        height: height,
        format: gpuFormat,
        layers: 1,
        isArray: true
    });

    return id;
}

export function destroyRenderTexture(textureId) {
    const rt = renderTextures.get(textureId);
    if (rt) {
        rt.texture.destroy();
        renderTextures.delete(textureId);
    }
    textures.delete(textureId);
}

export function beginRenderTexturePass(textureId, clearR, clearG, clearB, clearA) {
    const rt = renderTextures.get(textureId);
    if (!rt || !currentCommandEncoder) {
        console.error(`beginRenderTexturePass: render texture ${textureId} not found or no command encoder`);
        return;
    }

    currentRenderTexturePass = rt;

    currentRenderPass = currentCommandEncoder.beginRenderPass({
        colorAttachments: [{
            view: rt.view,
            loadOp: 'clear',
            storeOp: 'store',
            clearValue: { r: clearR, g: clearG, b: clearB, a: clearA }
        }],
        label: 'render_texture_pass'
    });
}

export function endRenderTexturePass() {
    if (currentRenderPass) {
        currentRenderPass.end();
        currentRenderPass = null;
    }
    currentRenderTexturePass = null;
}

export async function readRenderTexturePixels(textureId) {
    const rt = renderTextures.get(textureId);
    if (!rt) throw new Error(`Render texture ${textureId} not found`);

    const bytesPerPixel = 4;
    const bytesPerRow = rt.width * bytesPerPixel;
    // WebGPU requires 256-byte alignment for bytesPerRow
    const alignedBytesPerRow = Math.ceil(bytesPerRow / 256) * 256;
    const bufferSize = alignedBytesPerRow * rt.height;

    // Create staging buffer
    const stagingBuffer = device.createBuffer({
        size: bufferSize,
        usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
        label: 'readback_staging'
    });

    // Copy texture to buffer
    const encoder = device.createCommandEncoder();
    encoder.copyTextureToBuffer(
        { texture: rt.texture, origin: { x: 0, y: 0, z: 0 } },
        { buffer: stagingBuffer, bytesPerRow: alignedBytesPerRow, rowsPerImage: rt.height },
        { width: rt.width, height: rt.height, depthOrArrayLayers: 1 }
    );

    queue.submit([encoder.finish()]);

    // Map and read
    await stagingBuffer.mapAsync(GPUMapMode.READ);
    const mappedData = new Uint8Array(stagingBuffer.getMappedRange());

    // Copy to result, removing row padding
    const result = new Uint8Array(rt.width * rt.height * bytesPerPixel);
    for (let y = 0; y < rt.height; y++) {
        const srcOffset = y * alignedBytesPerRow;
        const dstOffset = y * bytesPerRow;
        result.set(mappedData.subarray(srcOffset, srcOffset + bytesPerRow), dstOffset);
    }

    stagingBuffer.unmap();
    stagingBuffer.destroy();

    // Convert to base64 for JS interop (returning Uint8Array directly doesn't work well)
    let binary = '';
    for (let i = 0; i < result.length; i++) {
        binary += String.fromCharCode(result[i]);
    }
    return btoa(binary);
}

// ============================================================================
// Utility Functions
// ============================================================================

export function getPresentFormat() {
    return presentFormat;
}

export function getDevice() {
    return device;
}

// Blend mode presets
export const BlendModes = {
    none: null,
    alpha: {
        color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
    },
    additive: {
        color: { srcFactor: 'src-alpha', dstFactor: 'one', operation: 'add' },
        alpha: { srcFactor: 'src-alpha', dstFactor: 'one', operation: 'add' }
    },
    multiply: {
        color: { srcFactor: 'dst', dstFactor: 'zero', operation: 'add' },
        alpha: { srcFactor: 'dst', dstFactor: 'zero', operation: 'add' }
    },
    premultiplied: {
        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' },
        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' }
    }
};

export function getBlendState(blendMode) {
    return BlendModes[blendMode] || BlendModes.alpha;
}

// GPUBufferUsage constants for C# interop
export const BufferUsage = {
    MAP_READ: GPUBufferUsage.MAP_READ,
    MAP_WRITE: GPUBufferUsage.MAP_WRITE,
    COPY_SRC: GPUBufferUsage.COPY_SRC,
    COPY_DST: GPUBufferUsage.COPY_DST,
    INDEX: GPUBufferUsage.INDEX,
    VERTEX: GPUBufferUsage.VERTEX,
    UNIFORM: GPUBufferUsage.UNIFORM,
    STORAGE: GPUBufferUsage.STORAGE,
    INDIRECT: GPUBufferUsage.INDIRECT,
    QUERY_RESOLVE: GPUBufferUsage.QUERY_RESOLVE
};

// GPUTextureUsage constants for C# interop
export const TextureUsage = {
    COPY_SRC: GPUTextureUsage.COPY_SRC,
    COPY_DST: GPUTextureUsage.COPY_DST,
    TEXTURE_BINDING: GPUTextureUsage.TEXTURE_BINDING,
    STORAGE_BINDING: GPUTextureUsage.STORAGE_BINDING,
    RENDER_ATTACHMENT: GPUTextureUsage.RENDER_ATTACHMENT
};

// ============================================================================
// Object Creation Helpers (for C# interop - replaces eval-based creation)
// ============================================================================

// Bind Group Layout Entry Creators
export function createUniformBufferLayoutEntry(binding) {
    return {
        binding: binding,
        visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
        buffer: { type: 'uniform' }
    };
}

export function createTexture2DLayoutEntry(binding) {
    return {
        binding: binding,
        visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
        texture: { sampleType: 'float', viewDimension: '2d' }
    };
}

export function createTexture2DArrayLayoutEntry(binding) {
    return {
        binding: binding,
        visibility: GPUShaderStage.FRAGMENT,
        texture: { sampleType: 'float', viewDimension: '2d-array' }
    };
}

export function createUnfilterableTexture2DLayoutEntry(binding) {
    return {
        binding: binding,
        visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
        texture: { sampleType: 'unfilterable-float', viewDimension: '2d' }
    };
}

export function createSamplerLayoutEntry(binding) {
    return {
        binding: binding,
        visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
        sampler: { type: 'filtering' }
    };
}

// Bind Group Entry Creators
export function createBufferBindGroupEntry(binding, bufferId, offset, size) {
    return {
        binding: binding,
        bufferId: bufferId,
        offset: offset,
        size: size
    };
}

export function createTextureBindGroupEntry(binding, textureViewId) {
    return {
        binding: binding,
        textureViewId: textureViewId
    };
}

export function createSamplerBindGroupEntry(binding, useLinearSampler) {
    return {
        binding: binding,
        useLinearSampler: useLinearSampler
    };
}

// Render Pipeline Descriptor Creator
export function createRenderPipelineDescriptor(
    vertexModuleId,
    fragmentModuleId,
    pipelineLayoutId,
    vertexEntryPoint,
    fragmentEntryPoint,
    vertexBuffersJson,
    blendMode,
    topology,
    cullMode,
    frontFace,
    sampleCount,
    targetFormat,
    label
) {
    // Keep everything as primitive values - don't parse JSON here
    // All resolution happens in createRenderPipeline to avoid JSObject proxy issues
    return {
        vertexModuleId: vertexModuleId,
        fragmentModuleId: fragmentModuleId,
        pipelineLayoutId: pipelineLayoutId,
        vertexEntryPoint: vertexEntryPoint,
        fragmentEntryPoint: fragmentEntryPoint,
        vertexBuffersJson: vertexBuffersJson,  // Keep as string, parse in createRenderPipeline
        blendMode: blendMode,  // Keep as string, resolve in createRenderPipeline
        topology: topology,
        cullMode: cullMode,
        frontFace: frontFace,
        sampleCount: sampleCount,
        targetFormat: targetFormat,
        label: label
    };
}

// Color Attachment Creator
export function createColorAttachment(textureId, resolveTextureId, loadOp, storeOp, clearR, clearG, clearB, clearA) {
    return {
        textureId: textureId,
        resolveTextureId: resolveTextureId,
        loadOp: loadOp,
        storeOp: storeOp,
        clearValue: { r: clearR, g: clearG, b: clearB, a: clearA }
    };
}
