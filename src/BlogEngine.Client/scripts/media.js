// Upload queue for the media library and the media picker (design 9.1, T2.3, T2.8, T2.11). Bundled by
// src/BlogEngine.Server/scripts/build-js.mjs into BlogEngine.Client/wwwroot/js/media.js.
//
// Files never pass through .NET: this module keeps the File objects, sends each one in its own
// multipart request with XMLHttpRequest (the only browser API that reports upload progress), and tells the
// MediaUploadZone component about progress and results. .NET decides which files to send (size checks) and
// renders the list.

/** How many files upload at the same time. */
const concurrency = 2;

/** Minimum time between progress reports for one file, so .NET re-renders a few times a second at most. */
const progressIntervalMs = 150;

/**
 * Wires a drop zone and a file input to an upload queue.
 * @param {HTMLElement} dropZone Element that accepts dropped files.
 * @param {HTMLInputElement} fileInput Hidden <input type="file" multiple>.
 * @param {any} dotNet DotNetObjectReference of the MediaUploadZone component.
 * @param {{ url: string, headerName: string, token: string | null }} options Endpoint and antiforgery header.
 */
export function createUploader(dropZone, fileInput, dotNet, options) {
    return new Uploader(dropZone, fileInput, dotNet, options);
}

class Uploader {
    constructor(dropZone, fileInput, dotNet, options) {
        this.dropZone = dropZone;
        this.fileInput = fileInput;
        this.dotNet = dotNet;
        this.options = options;
        this.queue = [];
        this.active = 0;
        this.nextId = 1;
        this.requests = new Set();
        this.dragDepth = 0;

        this.onDragEnter = event => {
            if (!hasFiles(event)) {
                return;
            }

            event.preventDefault();
            this.dragDepth++;
            this.dropZone.classList.add('is-dragover');
        };
        this.onDragOver = event => {
            if (hasFiles(event)) {
                // Allows the drop; without it the browser opens the file instead.
                event.preventDefault();
                event.dataTransfer.dropEffect = 'copy';
            }
        };
        this.onDragLeave = () => {
            this.dragDepth = Math.max(0, this.dragDepth - 1);
            if (this.dragDepth === 0) {
                this.dropZone.classList.remove('is-dragover');
            }
        };
        this.onDrop = event => {
            if (!hasFiles(event)) {
                return;
            }

            event.preventDefault();
            this.dragDepth = 0;
            this.dropZone.classList.remove('is-dragover');
            this.enqueue(event.dataTransfer.files);
        };
        this.onChange = () => {
            this.enqueue(this.fileInput.files);
            // Lets the same file be chosen again later.
            this.fileInput.value = '';
        };

        dropZone.addEventListener('dragenter', this.onDragEnter);
        dropZone.addEventListener('dragover', this.onDragOver);
        dropZone.addEventListener('dragleave', this.onDragLeave);
        dropZone.addEventListener('drop', this.onDrop);
        fileInput.addEventListener('change', this.onChange);
    }

    /** Opens the browser's file chooser. */
    openFilePicker() {
        this.fileInput.click();
    }

    /** Updates the antiforgery token, for example after it was refreshed. */
    setToken(token) {
        this.options.token = token;
    }

    /** Stops listening and cancels uploads still in flight. */
    dispose() {
        this.dropZone.removeEventListener('dragenter', this.onDragEnter);
        this.dropZone.removeEventListener('dragover', this.onDragOver);
        this.dropZone.removeEventListener('dragleave', this.onDragLeave);
        this.dropZone.removeEventListener('drop', this.onDrop);
        this.fileInput.removeEventListener('change', this.onChange);
        this.queue = [];
        this.dotNet = null;
        for (const request of this.requests) {
            request.abort();
        }
    }

    /** Reports the files to .NET, which answers with the ids to upload; the others it rejected itself. */
    async enqueue(fileList) {
        const entries = Array.from(fileList ?? []).map(file => ({ id: `upload-${this.nextId++}`, file }));
        if (entries.length === 0 || !this.dotNet) {
            return;
        }

        let accepted;
        try {
            accepted = await this.dotNet.invokeMethodAsync('OnFilesQueued',
                entries.map(entry => ({ id: entry.id, name: entry.file.name, size: entry.file.size })));
        } catch (error) {
            console.error('MediaUploadZone.OnFilesQueued failed', error);
            return;
        }

        const ids = new Set(accepted ?? []);
        this.queue.push(...entries.filter(entry => ids.has(entry.id)));
        this.pump();
    }

    /** Starts queued uploads up to the concurrency limit. */
    pump() {
        while (this.active < concurrency && this.queue.length > 0) {
            const entry = this.queue.shift();
            this.active++;
            this.send(entry).finally(() => {
                this.active--;
                this.pump();
            });
        }
    }

    /** Uploads one file and reports its progress and outcome. */
    send(entry) {
        return new Promise(resolve => {
            const request = new XMLHttpRequest();
            let lastReport = 0;

            const finish = (status, body) => {
                this.requests.delete(request);
                this.invoke('OnUploadFinished', entry.id, status, body ?? '');
                resolve();
            };

            request.upload.addEventListener('progress', event => {
                const now = performance.now();
                if (event.lengthComputable && (now - lastReport >= progressIntervalMs || event.loaded === event.total)) {
                    lastReport = now;
                    this.invoke('OnUploadProgress', entry.id, event.loaded, event.total);
                }
            });
            request.addEventListener('load', () => finish(request.status, request.responseText));
            request.addEventListener('error', () => finish(0, ''));
            request.addEventListener('abort', () => resolve());

            request.open('POST', this.options.url);
            request.setRequestHeader('Accept', 'application/json');
            if (this.options.token) {
                request.setRequestHeader(this.options.headerName, this.options.token);
            }

            const form = new FormData();
            form.append('files', entry.file, entry.file.name);
            this.requests.add(request);
            request.send(form);
        });
    }

    /** Calls the component, ignoring calls after it was disposed. */
    async invoke(method, ...args) {
        if (!this.dotNet) {
            return;
        }

        try {
            await this.dotNet.invokeMethodAsync(method, ...args);
        } catch (error) {
            if (this.dotNet) {
                console.error(`MediaUploadZone.${method} failed`, error);
            }
        }
    }
}

/** Whether a drag carries files (not text or a link dragged from the page). */
function hasFiles(event) {
    return Array.from(event.dataTransfer?.types ?? []).includes('Files');
}
