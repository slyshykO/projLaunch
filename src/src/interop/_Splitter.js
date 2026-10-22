// Resize locally during a gesture; persist one Elmish update on release.
export function attachSplitter(handle, onResize) {
    const container = handle.parentElement;
    let drag;
    const horizontal = () => getComputedStyle(container).flexDirection === 'row';
    const variable = axis => axis ? '--files-width' : '--files-height';
    const value = axis => parseFloat(getComputedStyle(container).getPropertyValue(variable(axis))) || 40;
    const clamp = size => Math.max(20, Math.min(70, size));
    const announce = () => {
        const axis = horizontal();
        handle.setAttribute('aria-orientation', axis ? 'vertical' : 'horizontal');
        handle.setAttribute('aria-valuenow', String(Math.round(value(axis))));
    };
    const show = (axis, size) => {
        container.style.setProperty(variable(axis), `${size}%`);
        announce();
    };
    const finish = () => {
        if (!drag) return;
        const { axis, pointerId, size } = drag;
        drag = undefined;
        if (handle.hasPointerCapture(pointerId)) handle.releasePointerCapture(pointerId);
        onResize(axis, size);
    };
    const down = event => {
        if (event.button !== 0 || drag) return;
        event.preventDefault();
        handle.focus();
        const axis = horizontal();
        drag = { axis, pointerId: event.pointerId, size: value(axis) };
        handle.setPointerCapture(event.pointerId);
    };
    const move = event => {
        if (!drag || event.pointerId !== drag.pointerId) return;
        const rect = container.getBoundingClientRect();
        const span = drag.axis ? rect.width : rect.height;
        if (span <= 0) return;
        const offset = drag.axis ? rect.right - event.clientX : rect.bottom - event.clientY;
        drag.size = clamp(offset / span * 100);
        show(drag.axis, drag.size);
    };
    const key = event => {
        const axis = horizontal();
        let size = value(axis);
        const step = event.shiftKey ? 10 : 2;
        if (event.key === (axis ? 'ArrowLeft' : 'ArrowUp')) size += step;
        else if (event.key === (axis ? 'ArrowRight' : 'ArrowDown')) size -= step;
        else if (event.key === 'Home') size = 20;
        else if (event.key === 'End') size = 70;
        else return;
        event.preventDefault();
        size = clamp(size);
        show(axis, size);
        onResize(axis, size);
    };
    const reset = () => {
        const axis = horizontal();
        show(axis, 40);
        onResize(axis, 40);
    };
    const end = event => { if (drag?.pointerId === event.pointerId) finish(); };
    const events = { pointerdown: down, pointermove: move, pointerup: end, pointercancel: end, lostpointercapture: end, keydown: key, dblclick: reset };
    for (const [name, listener] of Object.entries(events)) handle.addEventListener(name, listener);
    const observer = new ResizeObserver(announce);
    observer.observe(container);
    announce();
    return () => {
        for (const [name, listener] of Object.entries(events)) handle.removeEventListener(name, listener);
        observer.disconnect();
        if (drag && handle.hasPointerCapture(drag.pointerId)) handle.releasePointerCapture(drag.pointerId);
        drag = undefined;
    };
}

// The left rail keeps its width locally while the repository tab stays mounted.
export function attachBranchSplitter(handle, onResize) {
    const panel = handle.parentElement;
    const clamp = size => Math.max(180, Math.min(400, size));
    const value = () => parseFloat(panel.style.getPropertyValue('--branches-width')) || 240;
    let drag;
    const show = size => {
        panel.style.setProperty('--branches-width', `${size}px`);
        handle.setAttribute('aria-valuenow', String(Math.round(size)));
    };
    const finish = event => {
        if (!drag || event.pointerId !== drag.id) return;
        const { id, size } = drag;
        drag = undefined;
        if (handle.hasPointerCapture(id)) handle.releasePointerCapture(id);
        onResize(size);
    };
    const events = {
        pointerdown(event) {
            if (event.button !== 0 || drag) return;
            event.preventDefault();
            handle.focus();
            drag = { id: event.pointerId, size: value() };
            handle.setPointerCapture(event.pointerId);
        },
        pointermove(event) {
            if (!drag || event.pointerId !== drag.id) return;
            drag.size = clamp(event.clientX - panel.getBoundingClientRect().left);
            show(drag.size);
        },
        pointerup: finish, pointercancel: finish, lostpointercapture: finish,
        keydown(event) {
            const step = event.shiftKey ? 32 : 8;
            let size = value();
            if (event.key === 'ArrowRight') size += step;
            else if (event.key === 'ArrowLeft') size -= step;
            else if (event.key === 'Home') size = 180;
            else if (event.key === 'End') size = 400;
            else return;
            event.preventDefault();
            size = clamp(size);
            show(size);
            onResize(size);
        },
        dblclick() { show(240); onResize(240); },
    };
    for (const [name, listener] of Object.entries(events)) handle.addEventListener(name, listener);
    show(value());
    return () => {
        for (const [name, listener] of Object.entries(events)) handle.removeEventListener(name, listener);
        if (drag && handle.hasPointerCapture(drag.id)) handle.releasePointerCapture(drag.id);
    };
}
