/**
 * Prueba de humo del frontend de JellyRoulette en un DOM simulado (jsdom).
 * No sustituye a probarlo en Jellyfin de verdad, pero verifica que:
 *   - el loader inyecta los puntos de entrada en los dos layouts,
 *   - el overlay se construye y se abre,
 *   - los filtros se pintan desde la API simulada,
 *   - una tirada termina con el ganador EXACTAMENTE delante de la aguja.
 */
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

// Resuelto desde la ubicación de este archivo: funciona desde cualquier cwd.
const WEB = path.resolve(__dirname, '..', '..', 'Jellyfin.Plugin.JellyRoulette', 'Web');
const loaderSrc = fs.readFileSync(path.join(WEB, 'roulette.loader.js'), 'utf8');
const uiSrc = fs.readFileSync(path.join(WEB, 'roulette.js'), 'utf8');

let failures = 0;
function check(name, cond, extra) {
    if (cond) {
        console.log('  ✓ ' + name);
    } else {
        failures++;
        console.log('  ✗ ' + name + (extra ? '  → ' + extra : ''));
    }
}

function makeDom(layout, mobile) {
    const dom = new JSDOM(`<!DOCTYPE html><html><head></head><body>
        ${layout === 'legacy'
            ? '<div class="skinHeader"><div class="headerLeft"></div><div class="headerRight"><button class="headerButton headerSearchButton"></button><button class="headerButton headerUserButton"></button></div></div><div class="mainDrawer"><div class="drawerContent"><a class="navMenuOption" href="#/home">Home</a></div></div>'
            : '<header class="MuiAppBar-root"><div class="MuiToolbar-root"><button class="menu">m</button><div class="nav"></div><div class="buttons"></div><div class="usermenu"></div></div></header>'}
    </body></html>`, {
        url: 'http://jellyfin.local/web/index.html',
        pretendToBeVisual: true,
        runScripts: 'outside-only'
    });

    const w = dom.window;

    // jsdom no aplica media queries reales: las simulamos para probar el modo móvil.
    w.matchMedia = (query) => ({
        media: query,
        matches: !!mobile && /max-width:\s*700px/.test(query),
        addListener() {}, removeListener() {},
        addEventListener() {}, removeEventListener() {}, dispatchEvent() { return false; }
    });

    // --- ApiClient falso, con el mismo contrato que usa el plugin ---
    const ITEMS = [];
    for (let i = 0; i < 40; i++) {
        ITEMS.push({
            id: 'item-' + i,
            name: 'Película ' + i,
            type: i % 3 === 0 ? 'Series' : 'Movie',
            year: 1990 + (i % 35),
            overview: 'Sinopsis de prueba número ' + i,
            genres: ['Horror', 'Comedy'],
            communityRating: 5 + (i % 5),
            officialRating: 'PG-13',
            runTimeMinutes: 80 + i,
            hasPrimaryImage: true,
            played: false,
            isFavorite: i % 7 === 0
        });
    }

    const GENRES = [
        { name: 'Horror', movieCount: 30, seriesCount: 5, totalCount: 35 },
        { name: 'Comedy', movieCount: 80, seriesCount: 12, totalCount: 92 },
        { name: 'Action', movieCount: 55, seriesCount: 9, totalCount: 64 }
    ];

    const LIBS = [
        { id: 'lib-movies', name: 'Películas', collectionType: 'movies' },
        { id: 'lib-music', name: 'Música', collectionType: 'music' }
    ];

    w.__calls = [];

    w.ApiClient = {
        accessToken: () => 'FAKE-TOKEN',
        serverAddress: () => 'http://jellyfin.local',
        getImageUrl: (id, opts) => 'http://jellyfin.local/Items/' + id + '/Images/Primary?maxWidth=' + (opts && opts.maxWidth),
        ajax: (o) => {
            w.__calls.push(o.url);
            const u = new URL(o.url);
            const p = u.pathname;
            const q = u.searchParams;
            return new Promise((resolve) => {
                setTimeout(() => {
                    if (p.endsWith('/Genres')) return resolve(GENRES);
                    if (p.endsWith('/Libraries')) return resolve(LIBS);
                    if (p.endsWith('/Info')) {
                        return resolve({
                            name: 'JellyRoulette', version: '1.0.0', id: 'guid',
                            ui: { enableInjection: true, floatingButton: 'Auto', spinDurationMs: 900, candidates: 20 },
                            defaults: { mediaType: 'both', unwatchedOnly: false, maxGenreChips: 40 }
                        });
                    }
                    if (p.endsWith('/Spin')) {
                        const winnerIndex = 7;
                        const winner = Object.assign({}, ITEMS[winnerIndex]);
                        const candidates = ITEMS.filter((_, i) => i !== winnerIndex).slice(0, 20);
                        return resolve({
                            winner,
                            candidates,
                            totalMatches: 123,
                            filters: {
                                mediaType: q.get('mediaType') || 'both',
                                unwatchedOnly: q.get('unwatchedOnly') === 'true',
                                genres: (q.get('genres') || '').split(',').filter(Boolean),
                                libraryId: q.get('libraryId') || null,
                                includeItemTypes: ['Movie', 'Series']
                            }
                        });
                    }
                    resolve(null);
                }, 5);
            });
        }
    };

    return dom;
}

async function run(layout, mobile) {
    console.log('\n=== Layout: ' + layout + (mobile ? ' (móvil)' : ' (escritorio)') + ' ===');
    const dom = makeDom(layout, mobile);
    const w = dom.window;

    w.eval(loaderSrc);
    // El loader sondea la sesión; le damos tiempo.
    await new Promise((r) => setTimeout(r, 1200));

    check('loader arrancó', !!w.JellyRoulette && !!w.JellyRoulette.base, JSON.stringify(w.JellyRoulette && w.JellyRoulette.base));
    check('CSS inyectado', !!w.document.getElementById('jr-styles'));

    const entry = w.document.querySelector('.jr-header-button');
    check('botón de cabecera inyectado', !!entry);
    const drawerLink = w.document.querySelector('.jr-drawer-link');
    check('enlace en el cajón de navegación', layout === 'legacy' ? !!drawerLink : true);

    // Precarga de la interfaz
    await new Promise((r) => setTimeout(r, 1200));
    w.eval(uiSrc);
    check('interfaz cargada', w.JellyRoulette.uiLoaded === true);

    w.JellyRoulette.open();
    await new Promise((r) => setTimeout(r, 600));

    const overlay = w.document.getElementById('jr-overlay');
    check('overlay abierto', !!overlay && overlay.dataset.open === 'true');

    const chips = overlay.querySelectorAll('.jr-chip');
    check('chips de género pintados (4 = Todo + 3)', chips.length === 4, 'obtenidos: ' + chips.length);
    check('género traducido a español', /Terror/.test(chips[1].textContent), chips[1].textContent);

    const libOptions = overlay.querySelectorAll('.jr-select option');
    check('bibliotecas filtradas (Todas + Películas)', libOptions.length === 2, 'obtenidas: ' + libOptions.length);

    // Seleccionamos un género y comprobamos que viaja en la petición
    chips[1].dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
    await new Promise((r) => setTimeout(r, 300));

    w.JellyRoulette.spin();
    await new Promise((r) => setTimeout(r, 3500));

    const spinCall = w.__calls.filter((c) => c.indexOf('/Spin') !== -1).pop();
    check('la tirada envió el género seleccionado', /genres=Horror/.test(spinCall || ''), spinCall);

    const result = w.document.getElementById('jr-result');
    check('ficha de resultado visible', result.dataset.show === 'true');

    const title = result.querySelector('.jr-hero-title');
    check('título del ganador', title && title.textContent === 'Película 7', title && title.textContent);

    // --- comprobación matemática: la aguja debe señalar al ganador ---
    const ring = overlay.querySelector('.jr-ring');
    const angleRaw = ring.style.getPropertyValue('--jr-angle');
    const angle = parseFloat(angleRaw) || 0;
    const cards = ring.querySelectorAll('.jr-card');
    const step = 360 / cards.length;

    let winnerPos = -1;
    cards.forEach((c, i) => { if (c.dataset.id === 'item-7') winnerPos = i; });
    check('el ganador está en el anillo', winnerPos >= 0);

    // La carta i queda delante cuando angle ≡ i*step (mod 360)
    const expected = (winnerPos * step) % 360;
    const actual = ((angle % 360) + 360) % 360;
    const diff = Math.min(Math.abs(actual - expected), 360 - Math.abs(actual - expected));
    check('la ruleta se detuvo exactamente en el ganador', diff < 0.6,
        'angle=' + actual.toFixed(3) + ' esperado=' + expected.toFixed(3) + ' cartas=' + cards.length);

    check('pósters con ApiKey (auth en <img>)',
        Array.from(ring.querySelectorAll('img')).some((i) => i.src.indexOf('ApiKey=FAKE-TOKEN') !== -1));

    // Acciones del resultado
    const actions = result.querySelectorAll('.jr-hero-actions .jr-btn');
    check('3 acciones (Reproducir / Ver ficha / Girar otra vez)', actions.length === 3, 'obtenidas: ' + actions.length);

    actions[2].dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
    await new Promise((r) => setTimeout(r, 2600));
    check('«Girar otra vez» vuelve a mostrar resultado', w.document.getElementById('jr-result').dataset.show === 'true');

    // --- Prueba de estrés: el ganador debe quedar delante SIEMPRE, también
    // --- cuando el pool se recorta porque la pantalla es pequeña.
    let stressOk = true;
    let stressDetail = '';
    for (let round = 0; round < 6; round++) {
        w.JellyRoulette.spin();
        await new Promise((r) => setTimeout(r, 2400));

        const ring2 = overlay.querySelector('.jr-ring');
        const cards2 = ring2.querySelectorAll('.jr-card');
        const step2 = 360 / cards2.length;
        const ang2 = parseFloat(ring2.style.getPropertyValue('--jr-angle')) || 0;
        const winnerCard = ring2.querySelector('.jr-card--winner');
        const pos = winnerCard ? Array.prototype.indexOf.call(cards2, winnerCard) : -1;
        const exp = ((pos * step2) % 360 + 360) % 360;
        const act = ((ang2 % 360) + 360) % 360;
        const d = Math.min(Math.abs(act - exp), 360 - Math.abs(act - exp));
        if (pos < 0 || d >= 0.6) {
            stressOk = false;
            stressDetail = 'ronda ' + round + ': pos=' + pos + ' cartas=' + cards2.length +
                ' ángulo=' + act.toFixed(2) + ' esperado=' + exp.toFixed(2);
            break;
        }
    }
    check('6 tiradas seguidas paran en el ganador', stressOk, stressDetail);

    // Cierre con ESC
    w.document.dispatchEvent(new w.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await new Promise((r) => setTimeout(r, 200));
    check('ESC cierra el overlay', w.document.getElementById('jr-overlay').dataset.open === 'false');

    const fab = w.document.getElementById('jr-fab');
    check('botón flotante (Auto: solo en móvil o si falla la cabecera)',
        mobile ? !!fab : !fab,
        'presente=' + !!fab);

    // No cerramos el window: sus timers seguirían disparando callbacks con el
    // documento ya inutilizado y ensuciarían la salida.
}

(async () => {
    await run('legacy', false);
    await run('mui', false);
    await run('legacy', true);

    console.log('\n' + (failures === 0 ? 'TODO OK' : failures + ' FALLOS'));
    process.exit(failures === 0 ? 0 : 1);
})();
