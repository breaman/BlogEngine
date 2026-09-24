// Visual crop, rotate and flip for the ImageCropper component (design 9.2, T2.9), built on the Cropper.js v2
// web components. Bundled by src/BlogEngine.Server/scripts/build-js.mjs into
// BlogEngine.Client/wwwroot/js/cropper.js.
//
// How rotation works: Cropper.js can rotate the <cropper-image> with a CSS transform, but then the selection
// no longer lines up with image pixels. Instead, this module draws the original rotated and flipped onto a
// canvas (scaled down to a display size) and shows that bitmap unrotated. The selection is then a plain
// rectangle over an axis-aligned image, and .NET (MediaGeometry.ToNatural) maps it to natural pixels of the
// rotated original, which is exactly what the server's "rotate/flip, then crop" expects.

import {
    CropperCanvas,
    CropperCrosshair,
    CropperGrid,
    CropperHandle,
    CropperImage,
    CropperSelection,
    CropperShade
} from 'cropperjs';

for (const element of [CropperCanvas, CropperImage, CropperSelection, CropperShade, CropperHandle, CropperGrid, CropperCrosshair]) {
    element.$define();
}

/** Longest side of the bitmap shown in the editor; the server always works on the full-size original. */
const maxDisplaySize = 1600;

/** Longest side of the preview thumbnail. */
const previewSize = 240;

/** Rounding slack when checking that the selection stays on the image, in CSS pixels. */
const epsilon = 0.5;

/**
 * Creates a cropper in `host` showing the image at `options.src`.
 * @param {HTMLElement} host Empty element owned by this module.
 * @param {HTMLElement} previewHost Empty element that receives the preview thumbnail.
 * @param {any} dotNet DotNetObjectReference of the ImageCropper component.
 * @param {{ src: string, alt?: string, rotate?: number, flipHorizontal?: boolean, flipVertical?: boolean,
 *           crop?: { x: number, y: number, width: number, height: number } | null }} options
 */
export async function createCropper(host, previewHost, dotNet, options) {
    const handle = new ImageCropperHandle(host, previewHost, dotNet, options);
    await handle.load();
    return handle;
}

class ImageCropperHandle {
    constructor(host, previewHost, dotNet, options) {
        this.host = host;
        this.previewHost = previewHost;
        this.dotNet = dotNet;
        this.options = options;
        this.state = {
            rotate: normalize(options.rotate ?? 0),
            flipHorizontal: options.flipHorizontal === true,
            flipVertical: options.flipVertical === true
        };
        this.pendingCrop = options.crop ?? null;
        this.aspectRatio = NaN;
        this.objectUrl = null;
        this.reportFrame = 0;
        this.previewTimer = 0;
        this.disposed = false;

        this.canvas = document.createElement('cropper-canvas');
        this.canvas.setAttribute('background', '');

        // Not rotatable, scalable or translatable: the image stays put and only the selection moves.
        this.image = document.createElement('cropper-image');
        this.image.setAttribute('initial-fit', 'contain');
        this.image.setAttribute('alt', options.alt ?? '');

        const shade = document.createElement('cropper-shade');
        shade.setAttribute('theme-color', 'rgba(0, 0, 0, 0.55)');

        const selectHandle = document.createElement('cropper-handle');
        selectHandle.setAttribute('action', 'select');
        selectHandle.setAttribute('plain', '');

        this.selection = document.createElement('cropper-selection');
        for (const attribute of ['movable', 'resizable', 'outlined', 'keyboard']) {
            this.selection.setAttribute(attribute, '');
        }

        const grid = document.createElement('cropper-grid');
        grid.setAttribute('role', 'grid');
        grid.setAttribute('covered', '');
        const crosshair = document.createElement('cropper-crosshair');
        crosshair.setAttribute('centered', '');
        const moveHandle = document.createElement('cropper-handle');
        moveHandle.setAttribute('action', 'move');
        moveHandle.setAttribute('theme-color', 'rgba(255, 255, 255, 0.35)');
        this.selection.append(grid, crosshair, moveHandle);
        for (const action of ['n', 'e', 's', 'w', 'ne', 'nw', 'se', 'sw']) {
            const resizeHandle = document.createElement('cropper-handle');
            resizeHandle.setAttribute('action', `${action}-resize`);
            this.selection.append(resizeHandle);
        }

        this.canvas.append(this.image, shade, selectHandle, this.selection);
        host.replaceChildren(this.canvas);

        // "change" fires before a move or resize is applied and can be cancelled: keep the selection on the image.
        this.onSelectionChange = event => {
            if (!this.fits(event.detail)) {
                event.preventDefault();
                return;
            }

            this.scheduleReport();
        };
        this.selection.addEventListener('change', this.onSelectionChange);
    }

    /** Loads the original, then shows it with the initial rotation, flips and crop. */
    async load() {
        this.source = await loadImage(this.options.src);
        await this.invoke('OnImageLoaded', this.source.naturalWidth, this.source.naturalHeight);
        await this.render();
    }

    /** Rotates the visible image by `degrees` (a multiple of 90, clockwise) and resets the selection. */
    async rotate(degrees) {
        // The server rotates first and flips second. Rotating what the author sees while exactly one flip is
        // active is the same as rotating the other way before that flip.
        const flipped = this.state.flipHorizontal !== this.state.flipVertical;
        this.state.rotate = normalize(this.state.rotate + (flipped ? -degrees : degrees));
        await this.render();
        return this.getState();
    }

    /** Mirrors the visible image and resets the selection. */
    async flip(horizontal) {
        if (horizontal) {
            this.state.flipHorizontal = !this.state.flipHorizontal;
        } else {
            this.state.flipVertical = !this.state.flipVertical;
        }

        await this.render();
        return this.getState();
    }

    /** Back to the untouched original: no rotation, no flips, the whole image selected. */
    async reset() {
        this.state = { rotate: 0, flipHorizontal: false, flipVertical: false };
        this.aspectRatio = NaN;
        this.selection.aspectRatio = NaN;
        await this.render();
        return this.getState();
    }

    /** Locks the selection to `ratio` (width / height), or frees it when `ratio` is null or 0. */
    setAspectRatio(ratio) {
        this.aspectRatio = ratio > 0 ? ratio : NaN;
        this.selection.aspectRatio = this.aspectRatio;

        const image = this.imageRect();
        if (image && !Number.isNaN(this.aspectRatio)) {
            // The largest rectangle of that ratio that fits the image, centered on the current selection.
            const current = { x: this.selection.x, y: this.selection.y, width: this.selection.width, height: this.selection.height };
            let width = Math.min(image.width, image.height * this.aspectRatio);
            let height = width / this.aspectRatio;
            const centerX = current.width > 0 ? current.x + current.width / 2 : image.x + image.width / 2;
            const centerY = current.height > 0 ? current.y + current.height / 2 : image.y + image.height / 2;
            const x = clamp(centerX - width / 2, image.x, image.x + image.width - width);
            const y = clamp(centerY - height / 2, image.y, image.y + image.height - height);
            this.selection.$change(x, y, width, height, this.aspectRatio);
        }

        this.scheduleReport();
    }

    /** Current rotation and flips. */
    getState() {
        return { ...this.state };
    }

    dispose() {
        this.disposed = true;
        cancelAnimationFrame(this.reportFrame);
        clearTimeout(this.previewTimer);
        this.selection.removeEventListener('change', this.onSelectionChange);
        this.host.replaceChildren();
        this.previewHost?.replaceChildren();
        if (this.objectUrl) {
            URL.revokeObjectURL(this.objectUrl);
        }

        this.dotNet = null;
    }

    /** Draws the rotated and flipped original, shows it, and restores or resets the selection. */
    async render() {
        const source = this.source;
        const turned = this.state.rotate === 90 || this.state.rotate === 270;
        const width = turned ? source.naturalHeight : source.naturalWidth;
        const height = turned ? source.naturalWidth : source.naturalHeight;
        const scale = Math.min(1, maxDisplaySize / Math.max(width, height));

        const bitmap = document.createElement('canvas');
        bitmap.width = Math.max(1, Math.round(width * scale));
        bitmap.height = Math.max(1, Math.round(height * scale));
        const context = bitmap.getContext('2d');
        // Applied to the drawing in reverse order: rotate first, then flip, like the server.
        context.translate(bitmap.width / 2, bitmap.height / 2);
        context.scale(this.state.flipHorizontal ? -1 : 1, this.state.flipVertical ? -1 : 1);
        context.rotate(this.state.rotate * Math.PI / 180);
        context.drawImage(source, -source.naturalWidth * scale / 2, -source.naturalHeight * scale / 2,
            source.naturalWidth * scale, source.naturalHeight * scale);

        const blob = await new Promise(resolve => bitmap.toBlob(resolve, 'image/png'));
        if (this.disposed) {
            return;
        }

        const previousUrl = this.objectUrl;
        this.objectUrl = URL.createObjectURL(blob);
        this.image.src = this.objectUrl;
        await this.image.$ready();
        // Let cropper-image fit itself into the canvas before measuring it.
        await nextFrame();
        await nextFrame();
        if (previousUrl) {
            URL.revokeObjectURL(previousUrl);
        }

        this.placeSelection(width, height);
    }

    /** Restores the initial crop (natural pixels) once, otherwise selects the whole image. */
    placeSelection(naturalWidth, naturalHeight) {
        const image = this.imageRect();
        if (!image) {
            return;
        }

        const crop = this.pendingCrop;
        this.pendingCrop = null;
        if (crop && crop.width > 0 && crop.height > 0) {
            const scaleX = image.width / naturalWidth;
            const scaleY = image.height / naturalHeight;
            this.selection.$change(image.x + crop.x * scaleX, image.y + crop.y * scaleY,
                crop.width * scaleX, crop.height * scaleY, NaN);
        } else if (!Number.isNaN(this.aspectRatio)) {
            this.selection.$change(image.x, image.y, image.width, image.height, NaN);
            this.setAspectRatio(this.aspectRatio);
        } else {
            this.selection.$change(image.x, image.y, image.width, image.height, NaN);
        }

        this.scheduleReport();
    }

    /** Where the image is drawn, relative to the cropper canvas (the selection's coordinate space). */
    imageRect() {
        const canvas = this.canvas.getBoundingClientRect();
        const image = this.image.getBoundingClientRect();
        if (image.width <= 0 || image.height <= 0) {
            return null;
        }

        return { x: image.left - canvas.left, y: image.top - canvas.top, width: image.width, height: image.height };
    }

    /** Whether a proposed selection lies on the image. */
    fits(rect) {
        const image = this.imageRect();
        return !image || (rect.x >= image.x - epsilon && rect.y >= image.y - epsilon
            && rect.x + rect.width <= image.x + image.width + epsilon
            && rect.y + rect.height <= image.y + image.height + epsilon);
    }

    /** Reports the selection to .NET once per frame, after Cropper.js has applied the change. */
    scheduleReport() {
        if (this.reportFrame) {
            return;
        }

        this.reportFrame = requestAnimationFrame(() => {
            this.reportFrame = 0;
            const image = this.imageRect();
            if (!image) {
                return;
            }

            const selection = { x: this.selection.x, y: this.selection.y, width: this.selection.width, height: this.selection.height };
            this.invoke('OnSelectionChanged', selection, image, this.getState());
            this.schedulePreview();
        });
    }

    /** Redraws the preview thumbnail shortly after the selection stops changing. */
    schedulePreview() {
        clearTimeout(this.previewTimer);
        this.previewTimer = setTimeout(async () => {
            if (!this.previewHost || this.selection.width <= 0 || this.selection.height <= 0) {
                return;
            }

            const ratio = this.selection.width / this.selection.height;
            const width = ratio >= 1 ? previewSize : Math.round(previewSize * ratio);
            const height = ratio >= 1 ? Math.round(previewSize / ratio) : previewSize;
            const preview = await this.selection.$toCanvas({ width: Math.max(1, width), height: Math.max(1, height) });
            if (!this.disposed) {
                preview.classList.add('image-cropper-preview-canvas');
                this.previewHost.replaceChildren(preview);
            }
        }, 120);
    }

    async invoke(method, ...args) {
        if (!this.dotNet) {
            return;
        }

        try {
            await this.dotNet.invokeMethodAsync(method, ...args);
        } catch (error) {
            if (this.dotNet) {
                console.error(`ImageCropper.${method} failed`, error);
            }
        }
    }
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.decoding = 'async';
        image.addEventListener('load', () => resolve(image), { once: true });
        image.addEventListener('error', () => reject(new Error(`The image ${src} could not be loaded.`)), { once: true });
        image.src = src;
    });
}

function nextFrame() {
    return new Promise(resolve => requestAnimationFrame(() => resolve()));
}

function normalize(degrees) {
    return ((Math.round(degrees / 90) % 4) + 4) % 4 * 90;
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), Math.max(min, max));
}
