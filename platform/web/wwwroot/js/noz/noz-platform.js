// NoZ Platform - Browser Implementation

let canvas = null;
let dotNetRef = null;
let lastClickTime = 0;
let clickCount = 0;

export function init(dotNet, width, height) {
    dotNetRef = dotNet;

    canvas = document.getElementById('noz-canvas');
    if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.id = 'noz-canvas';
        document.body.appendChild(canvas);
    }

    canvas.width = width;
    canvas.height = height;
    canvas.style.display = 'block';

    // Prevent context menu on right click
    canvas.addEventListener('contextmenu', e => e.preventDefault());

    // Keyboard events
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    // Mouse events
    canvas.addEventListener('mousedown', onMouseDown);
    canvas.addEventListener('mouseup', onMouseUp);
    canvas.addEventListener('mousemove', onMouseMove);
    canvas.addEventListener('wheel', onMouseWheel, { passive: false });

    // Touch events (basic support)
    canvas.addEventListener('touchstart', onTouchStart, { passive: false });
    canvas.addEventListener('touchend', onTouchEnd);
    canvas.addEventListener('touchmove', onTouchMove, { passive: false });

    // Resize
    window.addEventListener('resize', onResize);

    // Prevent default behaviors that interfere with games
    canvas.tabIndex = 1;
    canvas.focus();
}

export function shutdown() {
    window.removeEventListener('keydown', onKeyDown);
    window.removeEventListener('keyup', onKeyUp);
    window.removeEventListener('resize', onResize);

    if (canvas) {
        canvas.removeEventListener('mousedown', onMouseDown);
        canvas.removeEventListener('mouseup', onMouseUp);
        canvas.removeEventListener('mousemove', onMouseMove);
        canvas.removeEventListener('wheel', onMouseWheel);
        canvas.removeEventListener('touchstart', onTouchStart);
        canvas.removeEventListener('touchend', onTouchEnd);
        canvas.removeEventListener('touchmove', onTouchMove);
    }
}

export function getCanvas() {
    return canvas;
}

export function setCursor(cursorStyle) {
    if (canvas) {
        canvas.style.cursor = cursorStyle;
    }
}

function onKeyDown(e) {
    // Prevent default for game keys (arrows, space, etc.)
    if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', ' ', 'Tab'].includes(e.key)) {
        e.preventDefault();
    }
    dotNetRef.invokeMethodAsync('OnKeyDown', e.key);
}

function onKeyUp(e) {
    dotNetRef.invokeMethodAsync('OnKeyUp', e.key);
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
    dotNetRef.invokeMethodAsync('OnMouseDown', e.button, clickCount);
}

function onMouseUp(e) {
    dotNetRef.invokeMethodAsync('OnMouseUp', e.button);
}

function onMouseMove(e) {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    dotNetRef.invokeMethodAsync('OnMouseMove', x, y);
}

function onMouseWheel(e) {
    e.preventDefault();
    // Normalize scroll values
    const deltaX = e.deltaX > 0 ? 1 : e.deltaX < 0 ? -1 : 0;
    const deltaY = e.deltaY > 0 ? -1 : e.deltaY < 0 ? 1 : 0; // Inverted for natural scrolling
    dotNetRef.invokeMethodAsync('OnMouseWheel', deltaX, deltaY);
}

function onTouchStart(e) {
    e.preventDefault();
    if (e.touches.length > 0) {
        const touch = e.touches[0];
        const rect = canvas.getBoundingClientRect();
        const x = touch.clientX - rect.left;
        const y = touch.clientY - rect.top;
        dotNetRef.invokeMethodAsync('OnMouseMove', x, y);
        dotNetRef.invokeMethodAsync('OnMouseDown', 0, 1); // Simulate left click
    }
}

function onTouchEnd(e) {
    dotNetRef.invokeMethodAsync('OnMouseUp', 0);
}

function onTouchMove(e) {
    e.preventDefault();
    if (e.touches.length > 0) {
        const touch = e.touches[0];
        const rect = canvas.getBoundingClientRect();
        const x = touch.clientX - rect.left;
        const y = touch.clientY - rect.top;
        dotNetRef.invokeMethodAsync('OnMouseMove', x, y);
    }
}

function onResize() {
    // Could auto-resize canvas to window, or notify C# to decide
    dotNetRef.invokeMethodAsync('OnResize', window.innerWidth, window.innerHeight);
}
