// Keep DOM measurements outside Elmish. Only the durable position crosses IPC.
export function attachHistoryScroll(element, onScroll, onRestored, onCancel) {
    let saved;
    let token;
    let ready = false;
    let lastTop;
    let lastLeft;
    const visible = () => element.clientHeight > 0 && element.clientWidth > 0 && !element.closest('[hidden]');
    const rows = () => element.querySelectorAll('[data-history-oid]');
    const viewportTop = () => element.getBoundingClientRect().top + (element.querySelector('thead')?.getBoundingClientRect().height || 0);
    const position = () => {
        const items = rows();
        const top = viewportTop();
        // Row heights can vary with wrapped messages. Find the first visible row.
        let low = 0;
        let high = items.length;
        while (low < high) {
            const mid = (low + high) >>> 1;
            if (items[mid].getBoundingClientRect().bottom <= top) low = mid + 1;
            else high = mid;
        }
        const row = items[low];
        return {
            top: Math.max(0, element.scrollTop),
            left: Math.max(0, element.scrollLeft),
            anchor: row?.dataset.historyOid,
            offset: row ? top - row.getBoundingClientRect().top : 0,
        };
    };
    const remember = () => { lastTop = element.scrollTop; lastLeft = element.scrollLeft; };
    const restore = () => {
        if (!token || !ready || !visible()) return;
        const currentToken = token;
        const row = Array.from(rows()).find(row => row.dataset.historyOid === saved.anchor);
        const top = row ? element.scrollTop + row.getBoundingClientRect().top - viewportTop() + saved.offset : saved.top;
        element.scrollTop = Math.max(0, Math.min(top, element.scrollHeight - element.clientHeight));
        element.scrollLeft = Math.max(0, Math.min(saved.left, element.scrollWidth - element.clientWidth));
        remember();
        token = undefined;
        onRestored(currentToken, position());
    };
    const scroll = () => {
        if (token || !visible() || (lastTop === element.scrollTop && lastLeft === element.scrollLeft)) return;
        remember();
        onScroll(position());
    };
    const interrupt = event => {
        if (!token || !visible()) return;
        if (event.type === 'keydown' && !['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) return;
        token = undefined;
        onCancel();
    };
    element.addEventListener('scroll', scroll, { passive: true });
    for (const name of ['wheel', 'pointerdown', 'keydown']) element.addEventListener(name, interrupt, { passive: true });
    // Hidden tabs and history behind a diff have no usable geometry until shown.
    const observer = new ResizeObserver(restore);
    observer.observe(element);
    return {
        update(value, restoreToken, canRestore) {
            saved = value;
            token = restoreToken;
            ready = canRestore;
            restore();
        },
        dispose() {
            observer.disconnect();
            element.removeEventListener('scroll', scroll);
            for (const name of ['wheel', 'pointerdown', 'keydown']) element.removeEventListener(name, interrupt);
        },
    };
}
