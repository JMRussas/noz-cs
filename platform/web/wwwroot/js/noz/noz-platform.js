// NoZ Platform - Browser Implementation

let canvas = null;
let dotNetRef = null;
let lastClickTime = 0;
let clickCount = 0;
let gameLoop = null;

export async function init(dotNet, width, height) {
    dotNetRef = dotNet;

    // Import game loop module so we can pause/resume on visibility change
    gameLoop = await import('/js/noz/noz-gameloop.js');

    // Use the canvas created by Blazor (same one WebGPU uses)
    canvas = document.getElementById('canvas');
    if (!canvas) {
        console.error('Canvas element #canvas not found!');
        return { width: width, height: height };
    }

    // Set canvas to actual window size, accounting for device pixel ratio for sharp rendering
    const dpr = window.devicePixelRatio || 1;
    const actualWidth = Math.floor(window.innerWidth * dpr);
    const actualHeight = Math.floor(window.innerHeight * dpr);
    canvas.width = actualWidth;
    canvas.height = actualHeight;
    canvas.style.display = 'block';

    // Prevent context menu on right click
    canvas.addEventListener('contextmenu', e => e.preventDefault());

    // Keyboard events (capture phase so we preventDefault before anything else handles it)
    window.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('keyup', onKeyUp, true);

    // Mouse events
    canvas.addEventListener('mousedown', onMouseDown);
    canvas.addEventListener('mouseup', onMouseUp);
    canvas.addEventListener('mousemove', onMouseMove);
    canvas.addEventListener('wheel', onMouseWheel, { passive: false });

    canvas.addEventListener('mouseenter', onMouseEnter);
    canvas.addEventListener('mouseleave', onMouseLeave);

    // Touch events (basic support)
    canvas.addEventListener('touchstart', onTouchStart, { passive: false });
    canvas.addEventListener('touchend', onTouchEnd);
    canvas.addEventListener('touchmove', onTouchMove, { passive: false });

    // Resize
    window.addEventListener('resize', onResize);

    // Release modifier keys when window loses focus (e.g. Alt+Tab, Ctrl+Tab)
    // Without this, modifier keys get "stuck" because the keyup event never fires
    window.addEventListener('blur', onWindowBlur);

    // Warn the user before leaving the page if there are unsaved changes
    window.addEventListener('beforeunload', onBeforeUnload);

    // Notify C# when tab visibility changes (focus gained/lost)
    document.addEventListener('visibilitychange', onVisibilityChange);

    // Prevent default behaviors that interfere with games
    canvas.tabIndex = 1;
    canvas.focus();

    // Return actual window size and DPR so C# can use it
    return { width: actualWidth, height: actualHeight, dpr: dpr, isMobile: window.matchMedia('(pointer: coarse)').matches };
}

export function shutdown() {
    window.removeEventListener('keydown', onKeyDown, true);
    window.removeEventListener('keyup', onKeyUp, true);
    window.removeEventListener('resize', onResize);
    window.removeEventListener('blur', onWindowBlur);
    window.removeEventListener('beforeunload', onBeforeUnload);
    document.removeEventListener('visibilitychange', onVisibilityChange);

    if (canvas) {
        canvas.removeEventListener('mousedown', onMouseDown);
        canvas.removeEventListener('mouseup', onMouseUp);
        canvas.removeEventListener('mousemove', onMouseMove);
        canvas.removeEventListener('wheel', onMouseWheel);
        canvas.removeEventListener('mouseenter', onMouseEnter);
        canvas.removeEventListener('mouseleave', onMouseLeave);
        canvas.removeEventListener('touchstart', onTouchStart);
        canvas.removeEventListener('touchend', onTouchEnd);
        canvas.removeEventListener('touchmove', onTouchMove);
    }
}

export function getCanvas() {
    return canvas;
}

export function getDevicePixelRatio() {
    return window.devicePixelRatio || 1;
}

export function setCursor(cursorStyle) {
    if (canvas) {
        canvas.style.cursor = cursorStyle;
    }
}

export function openURL(url) {
    window.open(url, '_blank');
}

function onWindowBlur() {
    // Release all modifier keys when window loses focus
    syncModifier(false, 'Control');
    syncModifier(false, 'Shift');
    syncModifier(false, 'Alt');
    syncModifier(false, 'Meta');
}

function syncModifiers(e) {
    // Sync modifier key state with the browser's actual state to prevent stuck keys.
    // This catches cases where keyup was missed (e.g. browser dialog stole focus).
    syncModifier(e.ctrlKey, 'Control');
    syncModifier(e.shiftKey, 'Shift');
    syncModifier(e.altKey, 'Alt');
    syncModifier(e.metaKey, 'Meta');
}

function syncModifier(pressed, key) {
    var code = key.toLowerCase();
    if (pressed && !modifierState[code]) {
        modifierState[code] = true;
        dotNetRef.invokeMethod('OnKeyDown', key);
    } else if (!pressed && modifierState[code]) {
        modifierState[code] = false;
        dotNetRef.invokeMethod('OnKeyUp', key);
    }
}

let modifierState = { control: false, shift: false, alt: false, meta: false };

function onKeyDown(e) {
    // Prevent default for game keys (arrows, space, etc.)
    if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', ' ', 'Tab'].includes(e.key)) {
        e.preventDefault();
    }

    // Prevent browser defaults for Ctrl/Cmd shortcuts the app handles
    if ((e.ctrlKey || e.metaKey) && ['s', 'o', 'z', 'y'].includes(e.key.toLowerCase())) {
        e.preventDefault();
    }

    // Sync modifiers with browser state before processing the key
    syncModifiers(e);

    // Don't double-send modifier keys (syncModifiers already handled them)
    if (!['Control', 'Shift', 'Alt', 'Meta'].includes(e.key)) {
        dotNetRef.invokeMethod('OnKeyDown', e.key);
    }

    // Forward printable characters as text input (e.key is a single char for printable keys)
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
        dotNetRef.invokeMethod('OnTextInput', e.key);
    }
}

function onKeyUp(e) {
    syncModifiers(e);

    if (!['Control', 'Shift', 'Alt', 'Meta'].includes(e.key)) {
        dotNetRef.invokeMethod('OnKeyUp', e.key);
    }
}

function onMouseDown(e) {
    const now = Date.now();
    if (now - lastClickTime < 300 && e.button === 0) {
        clickCount++;
    } else {
        clickCount = 1;
    }
    lastClickTime = now;

    const rect = canvas.getBoundingClientRect();
    dotNetRef.invokeMethod('OnMouseDown', e.button, clickCount);
}

function onMouseUp(e) {
    dotNetRef.invokeMethod('OnMouseUp', e.button);
}

function onMouseMove(e) {
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const x = (e.clientX - rect.left) * dpr;
    const y = (e.clientY - rect.top) * dpr;
    dotNetRef.invokeMethod('OnMouseEnter');
    dotNetRef.invokeMethod('OnMouseMove', x, y);
}

function onMouseEnter(e) {
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    dotNetRef.invokeMethod('OnMouseEnter');
    dotNetRef.invokeMethod('OnMouseMove', (e.clientX - rect.left) * dpr, (e.clientY - rect.top) * dpr);
}

function onMouseLeave() {
    dotNetRef.invokeMethod('OnMouseLeave');
}

function onMouseWheel(e) {
    e.preventDefault();
    // Normalize scroll values
    const deltaX = e.deltaX > 0 ? 1 : e.deltaX < 0 ? -1 : 0;
    const deltaY = e.deltaY > 0 ? -1 : e.deltaY < 0 ? 1 : 0; // Inverted for natural scrolling
    dotNetRef.invokeMethod('OnMouseWheel', deltaX, deltaY);
}

function onTouchStart(e) {
    e.preventDefault();
    if (e.touches.length > 0) {
        const touch = e.touches[0];
        const rect = canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        const x = (touch.clientX - rect.left) * dpr;
        const y = (touch.clientY - rect.top) * dpr;
        dotNetRef.invokeMethod('OnMouseMove', x, y);
        dotNetRef.invokeMethod('OnMouseDown', 0, 1); // Simulate left click
    }
}

function onTouchEnd(e) {
    dotNetRef.invokeMethod('OnMouseUp', 0);
}

function onTouchMove(e) {
    e.preventDefault();
    if (e.touches.length > 0) {
        const touch = e.touches[0];
        const rect = canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        const x = (touch.clientX - rect.left) * dpr;
        const y = (touch.clientY - rect.top) * dpr;
        dotNetRef.invokeMethod('OnMouseMove', x, y);
    }
}

function onResize() {
    // Update canvas size to match window, accounting for device pixel ratio
    const dpr = window.devicePixelRatio || 1;
    const width = Math.floor(window.innerWidth * dpr);
    const height = Math.floor(window.innerHeight * dpr);
    if (canvas) {
        canvas.width = width;
        canvas.height = height;
    }
    // Notify C# of the pixel size (not CSS size)
    dotNetRef.invokeMethod('OnResize', width, height);
}

function onBeforeUnload(e) {
    if (dotNetRef && dotNetRef.invokeMethod('ShouldPreventUnload')) {
        e.preventDefault();
        e.returnValue = '';
    }
}

function onVisibilityChange() {
    const visible = document.visibilityState === 'visible';

    // Pause/resume the game loop — prevents calling into WebGPU while the tab is hidden
    // and avoids a huge delta time spike on the first frame back
    if (gameLoop) {
        gameLoop.setPaused(!visible);
    }

    if (dotNetRef) {
        dotNetRef.invokeMethod('OnVisibilityChanged', visible);
    }
}
