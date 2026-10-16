/**
 * Carga docs/preview.html en un DOM simulado, con carga de recursos locales,
 * y comprueba que la demo funciona de punta a punta (sin errores de consola).
 */
const fs = require('fs');
const path = require('path');
const { JSDOM, VirtualConsole } = require('jsdom');

const PREVIEW = path.resolve(__dirname, '..', '..', 'docs', 'preview.html');

let failures = 0;
function check(name, cond, extra) {
    if (cond) console.log('  ✓ ' + name);
    else { failures++; console.log('  ✗ ' + name + (extra ? '  → ' + extra : '')); }
}

(async () => {
    const errors = [];
    const vc = new VirtualConsole();
    vc.on('jsdomError', (e) => errors.push('jsdomError: ' + (e && e.message)));
    vc.on('error', (...a) => errors.push('console.error: ' + a.join(' ')));
    vc.on('warn', (...a) => { /* avisos ignorados */ });

    const dom = await JSDOM.fromFile(PREVIEW, {
        runScripts: 'dangerously',
        resources: 'usable',
        pretendToBeVisual: true,
        virtualConsole: vc
    });

    const w = dom.window;
    await new Promise((r) => setTimeout(r, 2500));

    console.log('\n=== docs/preview.html ===');
    check('sin errores de JS al cargar', errors.length === 0, errors.join(' | '));
    check('CSS del plugin enlazado', !!w.document.querySelector('link[href*="roulette.css"]'));
    check('ApiClient simulado presente', typeof w.ApiClient === 'object' && typeof w.ApiClient.ajax === 'function');
    check('roulette.js cargado', !!(w.JellyRoulette && w.JellyRoulette.uiLoaded));

    const overlay = w.document.getElementById('jr-overlay');
    check('la ruleta se abrió sola', !!overlay && overlay.dataset.open === 'true');

    await new Promise((r) => setTimeout(r, 900));
    const chips = overlay ? overlay.querySelectorAll('.jr-chip').length : 0;
    check('géneros simulados pintados', chips > 5, 'chips=' + chips);

    const cards = overlay ? overlay.querySelectorAll('.jr-card').length : 0;
    check('anillo con pósters', cards >= 6, 'cartas=' + cards);

    const imgs = overlay ? overlay.querySelectorAll('.jr-card img') : [];
    check('pósters SVG generados', imgs.length > 0 && /^data:image\/svg/.test(imgs[0].src),
        imgs.length ? imgs[0].src.slice(0, 40) : 'sin imágenes');

    // Tirada completa con el simulador
    w.JellyRoulette.spin();
    await new Promise((r) => setTimeout(r, 6500));

    const result = w.document.getElementById('jr-result');
    check('la tirada muestra un ganador', result && result.dataset.show === 'true');
    const title = result ? result.querySelector('.jr-hero-title') : null;
    check('título del ganador visible', !!(title && title.textContent.length > 2), title && title.textContent);

    const ring = overlay.querySelector('.jr-ring');
    const winnerCard = ring.querySelector('.jr-card--winner');
    const all = ring.querySelectorAll('.jr-card');
    const step = 360 / all.length;
    const pos = winnerCard ? Array.prototype.indexOf.call(all, winnerCard) : -1;
    const ang = parseFloat(ring.style.getPropertyValue('--jr-angle')) || 0;
    const exp = ((pos * step) % 360 + 360) % 360;
    const act = ((ang % 360) + 360) % 360;
    const diff = Math.min(Math.abs(act - exp), 360 - Math.abs(act - exp));
    check('parada exacta en el ganador (demo)', pos >= 0 && diff < 0.6,
        'pos=' + pos + ' ang=' + act.toFixed(2) + ' exp=' + exp.toFixed(2));

    check('sin errores tras la tirada', errors.length === 0, errors.join(' | '));

    console.log('\n' + (failures === 0 ? 'PREVIEW OK' : failures + ' FALLOS'));
    process.exit(failures === 0 ? 0 : 1);
})();
