import { invoke, isTauri } from '@tauri-apps/api/core';

const MAX_MESSAGE = 4000; // <= 16 KiB even when UTF-8 encoded
const MAX_QUEUE = 200;
const MAX_BATCH = 32;

function format(value) {
    try {
        if (value instanceof Error) return value.stack || `${value.name}: ${value.message}`;
        if (typeof value === 'string') return value;
        const seen = new WeakSet();
        return JSON.stringify(value, (_, item) => {
            if (typeof item === 'bigint') return String(item);
            if (item && typeof item === 'object') {
                if (seen.has(item)) return '[Circular]';
                seen.add(item);
            }
            return item;
        }) ?? String(value);
    } catch {
        return '[Unserializable log value]';
    }
}

// Injectable transport and event target keep regression checks independent of Tauri.
export function createLogger({ send, available, consoleTarget, eventTarget }) {
    const original = {};
    const wrappers = {};
    const levels = { log: 'info', info: 'info', warn: 'warn', error: 'error', debug: 'debug', trace: 'trace' };
    let queue = [];
    let timer;
    let inFlight;
    let started = false;
    let dropped = 0;
    let warned = false;

    function enqueue(level, args) {
        if (!started || !available()) return;
        if (queue.length >= MAX_QUEUE) {
            dropped++;
            return;
        }
        const message = args.map(format).join(' ');
        queue.push({ level, message: message.length > MAX_MESSAGE
            ? message.slice(0, MAX_MESSAGE - 14) + '… [truncated]' : message });
        if (level === 'error' || level === 'warn') {
            void drain();
        } else if (!timer && !inFlight) {
            timer = setTimeout(() => { timer = undefined; void drain(); }, 50);
        }
    }

    async function drain() {
        if (inFlight) return inFlight;
        clearTimeout(timer);
        timer = undefined;
        if (!queue.length) return;
        const run = async () => {
            while (queue.length) {
                const entries = queue.splice(0, MAX_BATCH);
                try {
                    await send(entries);
                    warned = false;
                } catch (error) {
                    queue = [];
                    dropped = 0;
                    // Never route transport failures through the patched console.
                    if (!warned) original.warn?.call(consoleTarget, 'File logging unavailable:', error);
                    warned = true;
                    break;
                }
                if (dropped && queue.length < MAX_QUEUE) {
                    queue.push({ level: 'warn', message: `Frontend log queue overflow: ${dropped} messages dropped` });
                    dropped = 0;
                }
            }
        };
        inFlight = run();
        try {
            await inFlight;
        } finally {
            inFlight = undefined;
        }
    }

    function write(level, message) {
        const method = level in levels ? level : 'info';
        const output = original[method] || consoleTarget[method];
        output?.call(consoleTarget, message);
        enqueue(levels[method], [message]);
    }

    const onError = event => enqueue('error', [
        'Uncaught JavaScript error:', event.error || `${event.message || 'Unknown error'} (${event.filename || ''}:${event.lineno || 0})`,
    ]);
    const onRejection = event => enqueue('error', ['Unhandled promise rejection:', event.reason]);

    function start() {
        if (started) return () => {};
        started = true;
        for (const [method, level] of Object.entries(levels)) {
            original[method] = consoleTarget[method];
            wrappers[method] = (...args) => {
                original[method]?.apply(consoleTarget, args);
                enqueue(level, args);
            };
            consoleTarget[method] = wrappers[method];
        }
        eventTarget?.addEventListener('error', onError);
        eventTarget?.addEventListener('unhandledrejection', onRejection);
        write('info', 'Frontend ready');
        return () => {
            started = false;
            for (const method of Object.keys(levels)) {
                if (consoleTarget[method] === wrappers[method]) consoleTarget[method] = original[method];
            }
            eventTarget?.removeEventListener('error', onError);
            eventTarget?.removeEventListener('unhandledrejection', onRejection);
            void flush();
        };
    }

    async function flush() {
        // An unavailable IPC transport must not prevent closing the application.
        let timeout;
        try {
            await Promise.race([drain(), new Promise(resolve => { timeout = setTimeout(resolve, 1500); })]);
        } finally {
            clearTimeout(timeout);
        }
    }

    return { start, write, flush };
}

let logger;
function current() {
    return logger ??= createLogger({
        send: entries => invoke('frontend_log', { entries }),
        available: isTauri,
        consoleTarget: console,
        eventTarget: typeof window === 'undefined' ? undefined : window,
    });
}
export const start = () => current().start();
export const write = (level, message) => current().write(level, message);
export const flush = () => current().flush();
