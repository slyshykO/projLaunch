// One subscription lifetime, including disconnect recovery. Dependencies are
// supplied by the F# boundary so the lifecycle can be exercised without Tauri.
export function watchRepository(listen, subscribe, unsubscribe, matches, onChange, onStatus, newId) {
    let disposed = false;
    let wake = () => {};
    let delay = 1000;
    const done = (async () => {
        while (!disposed) {
            const id = newId();
            let unlisten;
            let failure;
            let subscribed = false;
            const interrupted = new Promise(resolve => { wake = resolve; });
            try {
                unlisten = await listen(event => {
                    if (disposed || !matches(event) || (event.subscriptionId != null && event.subscriptionId !== id)) return;
                    if (event.error != null) {
                        failure = event.error;
                        wake();
                    } else {
                        onChange(event.history, event.workingTree);
                    }
                });
                if (disposed) break;
                await subscribe(id);
                subscribed = true;
                if (disposed) break;
                if (failure == null) {
                    delay = 1000;
                    onStatus(undefined);
                    // Covers changes between the initial read and registration,
                    // and anything missed while the connection was unavailable.
                    onChange(true, true);
                }
                await interrupted;
                if (failure != null) throw new Error(failure);
            } catch (error) {
                if (!disposed) onStatus(String(error));
            } finally {
                // Also unsubscribe after a failed invocation: the request might
                // have reached the server before its response was lost.
                if (subscribed || unlisten) {
                    try { await unsubscribe(id); } catch { /* disconnected */ }
                }
                unlisten?.();
            }
            if (!disposed) {
                await new Promise(resolve => {
                    const timer = setTimeout(resolve, delay);
                    wake = () => { clearTimeout(timer); resolve(); };
                });
                delay = Math.min(delay * 2, 30000);
            }
        }
    })();
    return { dispose() { disposed = true; wake(); }, done };
}
