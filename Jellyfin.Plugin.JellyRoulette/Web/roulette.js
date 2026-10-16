// ==========================================
// SISTEMA DE TRADUCCIÓN (I18N)
// ==========================================
let i18nDict = {};
let currentLang = 'en';

async function initI18n() {
    const browserLang = (navigator.language || navigator.userLanguage || 'en').split('-')[0].toLowerCase();
    currentLang = ['es', 'en'].includes(browserLang) ? browserLang : 'en';

    try {
        const response = await fetch(`/JellyRoulette/Translations?lang=${currentLang}`);
        if (response.ok) {
            i18nDict = await response.json();
        } else {
            const fallback = await fetch(`/JellyRoulette/Translations?lang=en`);
            i18nDict = await fallback.json();
        }
    } catch (e) {
        console.warn("JellyRoulette: Fallo al cargar traducciones, usando claves por defecto.", e);
    }
}

function t(key) {
    return i18nDict[key] || key;
}

initI18n();
// ==========================================

/* =========================================================================
   JellyRoulette — roulette.js (interfaz completa)
   ========================================================================= */
(function () {
    'use strict';

    var JR = (window.JellyRoulette = window.JellyRoulette || {});
    if (!JR.version || JR.version === '1') {
        JR.version = '1.0.1';
    }

    var LOG = '[JellyRoulette]';

    /* ==================================================================
       1. Textos y utilidades (Ahora usan getters para evaluar t() en tiempo real)
       ================================================================== */
    var STR = {
        get titleA() { return t('title_a'); },
        get titleB() { return t('title_b'); },
        get close() { return t('close'); },
        get genre() { return t('filter_genre'); },
        get genreAll() { return t('genre_all'); },
        get type() { return t('filter_type'); },
        get typeMovie() { return t('type_movie'); },
        get typeSeries() { return t('type_series'); },
        get typeBoth() { return t('type_both'); },
        get watched() { return t('filter_status'); },
        get watchedOnly() { return t('filter_unwatched'); },
        get library() { return t('filter_library'); },
        get libraryAll() { return t('library_all'); },
        get spin() { return t('spin_button'); },
        get spinning() { return t('spinning'); },
        get shuffling() { return t('shuffling'); },
        matches: function (n) {
            var locale = currentLang === 'es' ? 'es-ES' : 'en-US';
            return n === 1 ? t('matches_1') : t('matches_n').replace('{n}', n.toLocaleString(locale));
        },
        get winnerKicker() { return t('winner_kicker'); },
        get winnerMovie() { return t('winner_movie'); },
        get winnerSeries() { return t('winner_series'); },
        get openItem() { return t('view_details'); },
        get playItem() { return t('play'); },
        get spinAgain() { return t('spin_again'); },
        get emptyTitle() { return t('no_results'); },
        get emptyText() { return t('empty_text'); },
        get errorTitle() { return t('error_title'); },
        get errorText() { return t('error_text'); },
        get unauthorizedText() { return t('unauthorized_text'); },
        get loadingGenres() { return t('loading_genres'); },
        get noGenres() { return t('no_genres'); },
        get hint() { return t('hint'); },
        get minutes() { return t('minutes'); },
        get seen() { return t('seen'); },
        get retry() { return t('retry'); },        
        get favorite() { return t('favorite'); }
    };

    /* Metadatos de géneros: clave inglesa (TMDb) -> [texto ES, emoji].
       Construimos también un índice inverso por nombre en español, porque
       muchas bibliotecas tienen los metadatos directamente en español. */
    var GENRE_ES = {
        'action': ['Acción', '💥'], 'action & adventure': ['Acción y aventura', '🧭'],
        'adventure': ['Aventura', '🗺️'], 'animation': ['Animación', '🎨'],
        'anime': ['Anime', '🌸'], 'biography': ['Biografía', '📖'],
        'children': ['Infantil', '🧸'], 'comedy': ['Comedia', '😂'],
        'crime': ['Crimen', '🕵️'], 'documentary': ['Documental', '🎥'],
        'drama': ['Drama', '🎭'], 'family': ['Familiar', '👨‍👩‍👧'],
        'fantasy': ['Fantasía', '🐉'], 'film noir': ['Cine negro', '🎞️'],
        'game show': ['Concurso', '🎮'], 'history': ['Historia', '🏛️'],
        'holiday': ['Navideño', '🎄'], 'horror': ['Terror', '👻'],
        'kids': ['Infantil', '🧸'], 'music': ['Música', '🎵'],
        'musical': ['Musical', '🎶'], 'mystery': ['Misterio', '🔎'],
        'news': ['Noticias', '📰'], 'reality': ['Reality', '📺'],
        'romance': ['Romance', '💘'], 'sci-fi & fantasy': ['Ciencia ficción y fantasía', '🛸'],
        'science fiction': ['Ciencia ficción', '🚀'], 'soap': ['Telenovela', '🧼'],
        'sport': ['Deporte', '⚽'], 'sports': ['Deporte', '⚽'],
        'suspense': ['Suspense', '😰'], 'talk show': ['Entrevistas', '🎙️'],
        'thriller': ['Thriller', '🔪'], 'tv movie': ['Telefilme', '📺'],
        'war': ['Bélica', '⚔️'], 'war & politics': ['Guerra y política', '🕊️'],
        'western': ['Wéstern', '🤠']
    };

    // Nombres bonitos en inglés para los que la capitalización automática no vale
    var GENRE_EN_PRETTY = {
        'action & adventure': 'Action & Adventure',
        'sci-fi & fantasy': 'Sci-Fi & Fantasy',
        'war & politics': 'War & Politics',
        'film noir': 'Film Noir',
        'game show': 'Game Show',
        'talk show': 'Talk Show',
        'tv movie': 'TV Movie'
    };

    // Índice inverso: 'animación' -> 'animation', 'terror' -> 'horror', etc.
    var GENRE_BY_ES = {};
    Object.keys(GENRE_ES).forEach(function (k) {
        GENRE_BY_ES[String(GENRE_ES[k][0]).toLowerCase()] = k;
    });

    function genreLabel(name) {
        if (!name) return { text: '', emoji: '🎲' };
        var key = String(name).toLowerCase();
        var engKey = null;
        var hit = GENRE_ES[key];
        if (hit) {
            engKey = key;                                   // metadatos en inglés
        } else if (GENRE_BY_ES[key]) {
            engKey = GENRE_BY_ES[key];                      // metadatos en español
            hit = GENRE_ES[engKey];
        }
        if (!hit) return { text: name, emoji: '🎲' };

        // El texto se muestra en el idioma de la interfaz; el emoji siempre sale del mapa.
        var text = currentLang === 'es'
            ? hit[0]
            : (GENRE_EN_PRETTY[engKey] || engKey.replace(/(^|\s)\w/g, function (c) { return c.toUpperCase(); }));
        return { text: text, emoji: hit[1] };
    }

    function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

    function shuffle(arr) {
        for (var i = arr.length - 1; i > 0; i--) {
            var j = Math.floor(Math.random() * (i + 1));
            var t = arr[i]; arr[i] = arr[j]; arr[j] = t;
        }
        return arr;
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function svg(pathD, viewBox) {
        return '<svg viewBox="' + (viewBox || '0 0 24 24') + '" fill="currentColor" aria-hidden="true">' + pathD + '</svg>';
    }

    var ICON = {
        close: svg('<path d="M18.3 5.71a1 1 0 0 0-1.41 0L12 10.59 7.11 5.7A1 1 0 0 0 5.7 7.11L10.59 12 5.7 16.89a1 1 0 1 0 1.41 1.41L12 13.41l4.89 4.89a1 1 0 0 0 1.41-1.41L13.41 12l4.89-4.89a1 1 0 0 0 0-1.4z"/>'),
        dice: svg('<path d="M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zm3.5 4A1.5 1.5 0 1 0 8.5 10 1.5 1.5 0 0 0 8.5 7zm7 7a1.5 1.5 0 1 0 1.5 1.5 1.5 1.5 0 0 0-1.5-1.5zM12 10.5A1.5 1.5 0 1 0 13.5 12 1.5 1.5 0 0 0 12 10.5z"/>'),
        movie: svg('<path d="M4 4h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zm1 2v2h2V6zm4 0v2h6V6zm8 0v2h2V6zM5 10v8h14v-8z"/>'),
        tv: svg('<path d="M3 5h18a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zm1 2v9h16V7zM8 20h8v1.5H8z"/>'),
        both: svg('<path d="M4 4h9v9H4zm11 0h5v5h-5zM4 15h5v5H4zm7 0h9v5h-9z"/>'),
        play: svg('<path d="M8 5.14v13.72a1 1 0 0 0 1.52.85l11-6.86a1 1 0 0 0 0-1.7l-11-6.86A1 1 0 0 0 8 5.14z"/>'),
        info: svg('<path d="M11 9h2V7h-2zm1 12a10 10 0 1 1 0-20 10 10 0 0 1 0 20zm-1-6h2v-6h-2z"/>'),
        refresh: svg('<path d="M17.65 6.35A8 8 0 1 0 19.73 14h-2.08A6 6 0 1 1 12 6a5.94 5.94 0 0 1 4.22 1.78L13 11h7V4z"/>'),
        sad: svg('<path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-3.5 7A1.5 1.5 0 1 1 7 10.5 1.5 1.5 0 0 1 8.5 9zm7 0A1.5 1.5 0 1 1 14 10.5 1.5 1.5 0 0 1 15.5 9zM7.9 16.2a5.4 5.4 0 0 1 8.2 0l-1.3 1.2a3.6 3.6 0 0 0-5.6 0z"/>')
    };

    /* ==================================================================
       2. API
       ================================================================== */
    function apiClient() { return typeof JR.getApiClient === 'function' ? JR.getApiClient() : (window.ApiClient || null); }
    function serverAddress() {
        var c = apiClient();
        if (c) { try { if (c.serverAddress && typeof c.serverAddress === 'function') { var a = c.serverAddress(); if (a) return String(a).replace(/\/$/, ''); } if (c._serverAddress) return String(c._serverAddress).replace(/\/$/, ''); } catch (e) {} }
        if (JR.base) return JR.base.replace(/\/JellyRoulette$/, '');
        return window.location.origin;
    }
    function accessToken() {
        var c = apiClient(); if (!c) return null;
        try { if (typeof c.accessToken === 'function') return c.accessToken(); if (c._serverInfo && c._serverInfo.AccessToken) return c._serverInfo.AccessToken; } catch (e) {}
        return null;
    }
    function buildQuery(params) {
        var parts = [];
        Object.keys(params).forEach(function (k) {
            var v = params[k];
            if (v === null || v === undefined || v === '' || v === false) return;
            parts.push(encodeURIComponent(k) + '=' + encodeURIComponent(v));
        });
        return parts.length ? '?' + parts.join('&') : '';
    }
    function normalize(value) {
        if (Array.isArray(value)) return value.map(normalize);
        if (value && typeof value === 'object') {
            var out = {};
            Object.keys(value).forEach(function (key) {
                var camel = key.charAt(0).toLowerCase() + key.slice(1);
                out[camel] = normalize(value[key]);
            });
            return out;
        }
        return value;
    }
    function apiGet(path, params) {
        var url = serverAddress() + '/JellyRoulette/' + path + buildQuery(params || {});
        var c = apiClient();
        if (c && typeof c.ajax === 'function') return c.ajax({ type: 'GET', url: url, dataType: 'json' }).then(normalize);
        var headers = { Accept: 'application/json' };
        var token = accessToken();
        if (token) headers.Authorization = 'MediaBrowser Token="' + token + '"';
        return fetch(url, { headers: headers, credentials: 'same-origin' }).then(function (res) {
            if (res.status === 401) throw Object.assign(new Error('unauthorized'), { status: 401 });
            if (!res.ok) throw Object.assign(new Error('http ' + res.status), { status: res.status });
            return res.json();
        }).then(normalize);
    }
    function posterUrl(item, maxWidth) {
        if (!item || !item.hasPrimaryImage) return null;
        var c = apiClient(); var w = maxWidth || 320;
        if (c && typeof c.getImageUrl === 'function') {
            try {
                var u = c.getImageUrl(item.id, { type: 'Primary', maxWidth: w, quality: 90 });
                var token = accessToken();
                if (token && u.indexOf('ApiKey=') === -1) u += (u.indexOf('?') === -1 ? '?' : '&') + 'ApiKey=' + encodeURIComponent(token);
                return u;
            } catch (e) {}
        }
        var base = serverAddress() + '/Items/' + encodeURIComponent(item.id) + '/Images/Primary';
        return base + '?maxWidth=' + w + '&quality=90' + (accessToken() ? '&ApiKey=' + encodeURIComponent(accessToken()) : '');
    }

    /* ==================================================================
       3. Tema camaleón
       ================================================================== */
    function applyTheme(root) {
        var cs = getComputedStyle(document.documentElement);
        var hasJfVars = false;
        ['--jf-palette-primary-main', '--jf-palette-background-default'].forEach(function (v) { if (cs.getPropertyValue(v) && cs.getPropertyValue(v).trim()) hasJfVars = true; });
        if (hasJfVars) { root.classList.remove('jr-sampled'); return; }
        function sample(selector, prop, fallback) {
            var node = document.querySelector(selector); if (!node) return fallback;
            var value = getComputedStyle(node)[prop]; return value && value !== 'rgba(0, 0, 0, 0)' ? value : fallback;
        }
        root.style.setProperty('--jr-bg', sample('html', 'backgroundColor', '#101010'));
        root.style.setProperty('--jr-surface', sample('.backgroundContainer', 'backgroundColor', '#1c1c1c'));
        root.style.setProperty('--jr-surface-2', sample('.skinHeader', 'backgroundColor', '#242424'));
        root.style.setProperty('--jr-accent', sample('.button-submit', 'backgroundColor', '#00a4dc'));
        root.style.setProperty('--jr-text', sample('.skinHeader', 'color', '#ffffff'));
        var bg = getComputedStyle(root).getPropertyValue('--jr-bg').trim();
        if (isLight(bg)) {
            root.style.setProperty('--jr-veil', 'rgba(245, 247, 250, .82)'); root.style.setProperty('--jr-text-dim', 'rgba(0, 0, 0, .62)');
            root.style.setProperty('--jr-divider', 'rgba(0, 0, 0, .14)'); root.style.setProperty('--jr-hover', 'rgba(0, 0, 0, .06)'); root.style.setProperty('--jr-accent-contrast', '#ffffff');
        } else {
            root.style.setProperty('--jr-veil', 'rgba(6, 8, 12, .8)'); root.style.setProperty('--jr-text-dim', 'rgba(255, 255, 255, .72)');
        }
        root.classList.add('jr-sampled');
    }
    function isLight(color) {
        var m = String(color).match(/[\d.]+/g); if (!m || m.length < 3) return false;
        var r = +m[0], g = +m[1], b = +m[2]; return (0.299 * r + 0.587 * g + 0.114 * b) > 140;
    }

    /* ==================================================================
       4. Estado y DOM
       ================================================================== */
    var state = {
        built: false, open: false, mediaType: 'both', unwatchedOnly: false, genres: [], libraryId: '',
        genreList: [], libraries: [], pool: [], winner: null, winnerIndex: 0, total: 0,
        phase: 'idle', theta: 0, velocity: 0, lastFrame: 0, frontIndex: -1,
        spinDuration: 4200, candidates: 28, reducedMotion: false, lastFocus: null, userTouched: false
    };
    var els = {};
    var ringItems = [];
    var geometry = { n: 12, step: 30, cardW: 130, cardH: 195, radius: 300, scale: 1 };
    var MAX_GENRES = 48;

    function buildOverlay() {
        if (state.built) return;
        var overlay = el('div', 'jr-overlay jr-root'); overlay.id = 'jr-overlay';
        overlay.setAttribute('role', 'dialog'); overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-label', 'JellyRoulette'); overlay.dataset.open = 'false';

        var top = el('div', 'jr-top');
        var logo = el('div', 'jr-logo');
        logo.innerHTML = '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><rect x="2.5" y="2.5" width="19" height="19" rx="4.5" fill="currentColor" opacity=".16"/><rect x="2.5" y="2.5" width="19" height="19" rx="4.5" stroke="currentColor" stroke-width="1.6"/><circle cx="8" cy="8" r="1.7" fill="currentColor"/><circle cx="16" cy="16" r="1.7" fill="currentColor"/><circle cx="12" cy="12" r="1.7" fill="currentColor"/></svg>';
        var title = el('h2', 'jr-title'); title.innerHTML = STR.titleA + '<em>' + STR.titleB + '</em>';
        logo.appendChild(title);
        var spacer = el('div', 'jr-spacer');
        var closeBtn = el('button', 'jr-icon-btn'); closeBtn.type = 'button'; closeBtn.title = STR.close;
        closeBtn.setAttribute('aria-label', STR.close); closeBtn.innerHTML = ICON.close;
        closeBtn.addEventListener('click', close);
        top.appendChild(logo); top.appendChild(spacer); top.appendChild(closeBtn);

        var filters = el('div', 'jr-filters');
        var rowGenre = el('div', 'jr-filter-row');
        rowGenre.appendChild(el('span', 'jr-filter-label', STR.genre));
        var genres = el('div', 'jr-genres'); genres.id = 'jr-genres'; genres.setAttribute('role', 'group'); genres.setAttribute('aria-label', STR.genre);
        rowGenre.appendChild(genres); els.genres = genres;

        var rowType = el('div', 'jr-filter-row'); rowType.appendChild(el('span', 'jr-filter-label', STR.type));
        var segmented = el('div', 'jr-segmented'); segmented.setAttribute('role', 'group'); segmented.setAttribute('aria-label', STR.type);
        [['movie', STR.typeMovie, ICON.movie], ['series', STR.typeSeries, ICON.tv], ['both', STR.typeBoth, ICON.both]].forEach(function (def) {
            var b = el('button', 'jr-seg'); b.type = 'button'; b.dataset.value = def[0];
            b.setAttribute('aria-pressed', String(state.mediaType === def[0])); b.title = def[1];
            b.innerHTML = def[2] + '<span class="jr-seg-text">' + def[1] + '</span>';
            b.addEventListener('click', function () {
                if (state.mediaType === def[0]) return; state.userTouched = true; state.mediaType = def[0];
                syncSegmented(); onFiltersChanged(true);
            });
            segmented.appendChild(b);
        });
        rowType.appendChild(segmented); els.segmented = segmented;

        var watched = el('label', 'jr-switch'); var cb = document.createElement('input'); cb.type = 'checkbox'; cb.checked = state.unwatchedOnly;
        var track = el('span', 'jr-track'); watched.appendChild(cb); watched.appendChild(track); watched.appendChild(el('span', null, STR.watchedOnly));
        cb.addEventListener('change', function () { state.userTouched = true; state.unwatchedOnly = cb.checked; onFiltersChanged(true); });
        els.unwatched = cb; rowType.appendChild(watched);

        var libSelect = document.createElement('select'); libSelect.className = 'jr-select'; libSelect.setAttribute('aria-label', STR.library);
        libSelect.addEventListener('change', function () { state.userTouched = true; state.libraryId = libSelect.value || ''; onFiltersChanged(true); });
        els.library = libSelect; rowType.appendChild(libSelect);
        filters.appendChild(rowGenre); filters.appendChild(rowType);

        var stageWrap = el('div', 'jr-stage-wrap'); var stage = el('div', 'jr-stage');
        var pointer = el('div', 'jr-pointer'); var floor = el('div', 'jr-floor');
        var viewport = el('div', 'jr-ring-viewport'); var ring = el('div', 'jr-ring');
        viewport.appendChild(ring); stage.appendChild(floor); stage.appendChild(pointer); stage.appendChild(viewport); stageWrap.appendChild(stage);

        var result = el('div', 'jr-result'); result.dataset.show = 'false'; result.id = 'jr-result';
        stageWrap.appendChild(result); els.result = result;

        var notice = el('div', 'jr-notice'); notice.dataset.show = 'false'; stageWrap.appendChild(notice); els.notice = notice;

        var controls = el('div', 'jr-controls'); var spinBtn = el('button', 'jr-spin'); spinBtn.type = 'button';
        spinBtn.innerHTML = ICON.dice + '<span>' + STR.spin + '</span>';
        spinBtn.addEventListener('click', function () { spin(); });
        var status = el('div', 'jr-status'); status.setAttribute('aria-live', 'polite');
        var hint = el('div', 'jr-hint'); hint.innerHTML = STR.hint;
        controls.appendChild(spinBtn); controls.appendChild(status); stageWrap.appendChild(hint);

        overlay.appendChild(top); overlay.appendChild(filters); overlay.appendChild(stageWrap); overlay.appendChild(controls);

        overlay.addEventListener('click', function (ev) {
            if (ev.target === overlay || ev.target === stageWrap || ev.target === stage) {
                if (els.result.dataset.show === 'true' || ev.target === overlay) close();
            }
        });

        document.body.appendChild(overlay);
        els.overlay = overlay; els.ring = ring; els.viewport = viewport; els.stage = stage; els.pointer = pointer; els.spinBtn = spinBtn; els.status = status;

        var resizeTimer = null;
        window.addEventListener('resize', function () {
            window.clearTimeout(resizeTimer); resizeTimer = window.setTimeout(function () {
                if (!state.open) return; computeGeometry(); layoutRing(false);
            }, 140);
        });

        state.built = true; applyTheme(overlay); loadFilters();
    }

    function syncSegmented() {
        var buttons = els.segmented.querySelectorAll('.jr-seg');
        for (var i = 0; i < buttons.length; i++) buttons[i].setAttribute('aria-pressed', String(buttons[i].dataset.value === state.mediaType));
    }

    function renderGenres() {
        var box = els.genres; box.textContent = '';
        var all = el('button', 'jr-chip'); all.type = 'button'; all.dataset.genre = '';
        all.setAttribute('aria-pressed', String(state.genres.length === 0));
        all.innerHTML = '<span aria-hidden="true">🎲</span>' + STR.genreAll;
        all.addEventListener('click', function () { state.genres = []; syncChips(); onFiltersChanged(false); });
        box.appendChild(all);
        state.genreList.forEach(function (g) {
            var label = genreLabel(g.name); var chip = el('button', 'jr-chip'); chip.type = 'button'; chip.dataset.genre = g.name;
            chip.setAttribute('aria-pressed', String(state.genres.indexOf(g.name) !== -1)); chip.title = g.name + ' · ' + g.totalCount;
            chip.innerHTML = '<span aria-hidden="true">' + label.emoji + '</span><span>' + escapeHtml(label.text) + '</span><span class="jr-count">' + g.totalCount + '</span>';
            chip.addEventListener('click', function () {
                var idx = state.genres.indexOf(g.name);
                if (idx === -1) state.genres.push(g.name); else state.genres.splice(idx, 1);
                syncChips(); onFiltersChanged(false);
            });
            box.appendChild(chip);
        });
    }

    function syncChips() {
        var chips = els.genres.querySelectorAll('.jr-chip');
        for (var i = 0; i < chips.length; i++) {
            var g = chips[i].dataset.genre;
            var pressed = g === '' ? state.genres.length === 0 : state.genres.indexOf(g) !== -1;
            chips[i].setAttribute('aria-pressed', String(pressed));
        }
    }

    function renderGenreSkeletons() {
        var box = els.genres; box.textContent = '';
        for (var i = 0; i < 8; i++) { var s = el('div', 'jr-skeleton'); s.style.width = (70 + Math.round(Math.random() * 60)) + 'px'; box.appendChild(s); }
    }

    function renderLibraries() {
        var sel = els.library; sel.textContent = '';
        var all = document.createElement('option'); all.value = ''; all.textContent = STR.libraryAll; sel.appendChild(all);
        state.libraries.forEach(function (lib) {
            var type = (lib.collectionType || '').toLowerCase();
            if (['music', 'books', 'photos', 'musicvideos', 'boxsets', 'playlists', 'livetv'].indexOf(type) !== -1) return;
            var o = document.createElement('option'); o.value = lib.id; o.textContent = lib.name; sel.appendChild(o);
        });
        sel.value = state.libraryId || ''; sel.style.display = sel.options.length > 1 ? '' : 'none';
    }

    function escapeHtml(s) {
        return String(s === null || s === undefined ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* ==================================================================
       5. Motor de la ruleta
       ================================================================== */
    function computeGeometry() {
        var rect = els.stage.getBoundingClientRect();
        var W = Math.max(280, rect.width || window.innerWidth); var H = Math.max(200, rect.height || 320);
        var small = W < 620; var cardW = small ? 92 : (W < 900 ? 116 : 138); var cardH = Math.round(cardW * 1.5); var gap = small ? 1.14 : 1.26;
        var ideal = Math.round((0.92 * W * Math.PI) / (cardW * gap));
        var poolSize = Math.max(state.pool.length, 1); var n = clamp(ideal, 8, 28); n = Math.min(n, Math.max(8, poolSize)); n = clamp(n, 6, 28);
        var step = 360 / n; var radius = (cardW * gap / 2) / Math.tan((step * Math.PI) / 360); var tilt = small ? -6 : -8;
        var neededW = 2 * radius + cardW; var neededH = cardH + 2 * radius * Math.sin(Math.abs(tilt) * Math.PI / 180);
        var scale = Math.min(1.05, (W * 0.94) / neededW, (H * 0.9) / neededH); scale = clamp(scale, 0.32, 1.05);
        geometry = { n: n, step: step, cardW: cardW, cardH: cardH, radius: radius, tilt: tilt, scale: scale };
        var root = els.overlay;
        root.style.setProperty('--jr-card-w', cardW + 'px'); root.style.setProperty('--jr-card-h', cardH + 'px');
        root.style.setProperty('--jr-ring-radius', radius.toFixed(1) + 'px'); root.style.setProperty('--jr-ring-scale', scale.toFixed(3));
        root.style.setProperty('--jr-tilt', tilt + 'deg'); root.style.setProperty('--jr-step', step.toFixed(4) + 'deg');
    }

    function buildRing() {
        var ring = els.ring; ring.textContent = ''; ringItems = []; computeGeometry();
        var n = geometry.n; var items = state.pool.slice(0, n);
        var i = 0; while (items.length < n && state.pool.length > 0) { items.push(state.pool[i % state.pool.length]); i++; }
        if (state.winner) {
            if (items.indexOf(state.winner) === -1) {
                if (items.length < n) items.push(state.winner); else items[Math.floor(Math.random() * items.length)] = state.winner;
            }
            state.winnerIndex = Math.max(0, items.indexOf(state.winner));
        }
        var posterWidth = Math.round(geometry.cardW * (window.devicePixelRatio > 1.6 ? 1.6 : 1.25));
        items.forEach(function (item, index) {
            var card = el('div', 'jr-card'); card.style.setProperty('--jr-i', String(index)); card.dataset.index = String(index);
            if (state.winner && item === state.winner) card.classList.add('jr-card--winner');
            var url = posterUrl(item, posterWidth);
            if (url) {
                var img = document.createElement('img'); img.alt = ''; img.loading = index < 8 ? 'eager' : 'lazy'; img.decoding = 'async'; img.src = url;
                img.addEventListener('error', function () { if (img.parentNode) img.parentNode.removeChild(img); card.appendChild(placeholder(item)); });
                card.appendChild(img);
            } else { card.appendChild(placeholder(item)); }
            card.dataset.id = item.id; ring.appendChild(card); ringItems.push(card);
        });
        layoutRing(true);
    }

    function placeholder(item) { var ph = el('div', 'jr-ph'); ph.textContent = (item && item.name ? item.name.trim().charAt(0) : '?').toUpperCase(); return ph; }

    function layoutRing(force) {
        els.ring.style.setProperty('--jr-angle', state.theta.toFixed(2) + 'deg');
        if (geometry.n <= 0 || ringItems.length === 0) return;
        var front = Math.round(state.theta / geometry.step) % geometry.n; if (front < 0) front += geometry.n;
        if (!force && front === state.frontIndex) return; state.frontIndex = front;
        if (!state.reducedMotion) { els.pointer.classList.add('jr-tick'); window.setTimeout(function () { els.pointer.classList.remove('jr-tick'); }, 70); }
        for (var i = 0; i < ringItems.length; i++) {
            var raw = Math.abs(i - front); var d = Math.min(raw, ringItems.length - raw);
            var tier = d <= 0.5 ? 0 : (d <= 1.5 ? 1 : 2); ringItems[i].dataset.tier = String(tier);
        }
    }

    var rafId = null; var tween = null;
    function startLoop() {
        if (rafId !== null) return; state.lastFrame = performance.now();
        var step = function (now) {
            var dt = Math.min(64, now - state.lastFrame); state.lastFrame = now;
            if (tween) {
                var p = clamp((now - tween.start) / tween.duration, 0, 1); var eased = tween.ease(p);
                state.theta = tween.from + (tween.to - tween.from) * eased; layoutRing(false);
                if (p >= 1) { var done = tween.onDone; tween = null; state.theta = state.theta % 360; layoutRing(true); if (done) done(); }
            } else if (state.phase === 'shuffling') {
                state.theta = (state.theta + state.velocity * (dt / 1000)) % 360; layoutRing(false);
            }
            rafId = requestAnimationFrame(step);
        };
        rafId = requestAnimationFrame(step);
    }
    function stopLoop() { if (rafId !== null) { cancelAnimationFrame(rafId); rafId = null; } tween = null; }
    function easeOutQuint(p) { return 1 - Math.pow(1 - p, 5); }
    function easeInOutSine(p) { return -(Math.cos(Math.PI * p) - 1) / 2; }

    function settleOn(index, duration, onDone) {
        var step = geometry.step; var base = index * step;
        var minExtra = 360 * 3; var k = Math.ceil((state.theta + minExtra - base) / 360);
        var target = base + 360 * k; var overshoot = target + Math.min(9, step * 0.45);
        var mainMs = Math.round(duration * 0.86); var backMs = duration - mainMs;
        tween = {
            start: performance.now(), duration: mainMs, from: state.theta, to: overshoot, ease: easeOutQuint,
            onDone: function () {
                tween = { start: performance.now(), duration: Math.max(160, backMs), from: overshoot, to: target, ease: easeInOutSine, onDone: onDone };
            }
        };
        startLoop();
    }

    /* ==================================================================
       6. Lógica de la tirada
       ================================================================== */
    function setStatus(html) { els.status.innerHTML = html || ''; }
    function showNotice(kind, title, text, actionLabel, actionFn) {
        var box = els.notice; box.textContent = ''; var inner = el('div', 'jr-notice-inner');
        inner.innerHTML = (kind === 'error' ? ICON.sad : ICON.info);
        inner.appendChild(el('h3', null, title)); inner.appendChild(el('p', null, text));
        if (actionLabel) {
            var b = el('button', 'jr-btn jr-btn-primary'); b.type = 'button';
            b.innerHTML = ICON.refresh + '<span>' + escapeHtml(actionLabel) + '</span>';
            b.addEventListener('click', actionFn); inner.appendChild(b);
        }
        box.appendChild(inner); box.dataset.show = 'true';
    }
    function hideNotice() { els.notice.dataset.show = 'false'; }
    function currentFilters() {
        return { mediaType: state.mediaType, unwatchedOnly: state.unwatchedOnly ? 'true' : null, libraryId: state.libraryId || null, genres: state.genres.length ? state.genres.join(',') : null, candidates: state.candidates };
    }

    function loadFilters() {
        renderGenreSkeletons();
        Promise.all([
            apiGet('Genres', { mediaType: state.mediaType, libraryId: state.libraryId || null, limit: MAX_GENRES }).catch(function () { return []; }),
            apiGet('Libraries').catch(function () { return []; }),
            apiGet('Info').catch(function () { return null; })
        ]).then(function (res) {
            var genres = res[0] || []; var libs = res[1] || []; var info = res[2];
            var ui = info && info.ui ? info.ui : null; var defaults = info && info.defaults ? info.defaults : null;
            if (ui) { state.spinDuration = clamp(ui.spinDurationMs || 4200, 1200, 12000); state.candidates = clamp(ui.candidates || 28, 8, 60); }
            if (defaults && !state.userTouched) {
                if (defaults.mediaType) { state.mediaType = defaults.mediaType; syncSegmented(); }
                if (typeof defaults.unwatchedOnly === 'boolean' && els.unwatched) { state.unwatchedOnly = defaults.unwatchedOnly; els.unwatched.checked = defaults.unwatchedOnly; }
                if (defaults.maxGenreChips) MAX_GENRES = clamp(defaults.maxGenreChips, 5, 200);
            }
            state.genreList = Array.isArray(genres) ? genres : []; state.libraries = Array.isArray(libs) ? libs : [];
            renderGenres(); renderLibraries();
            if (state.genreList.length === 0) setStatus(STR.noGenres);
        });
    }

    var primeTimer = null;
    function schedulePrime() {
        window.clearTimeout(primeTimer);
        primeTimer = window.setTimeout(function () { primeRing(); }, 250);
    }

    /** Al cambiar tipo/biblioteca hay que recargar los géneros (los recuentos cambian). */
    function onFiltersChanged(reloadGenres) {
        hideResult();
        state.pool = [];
        state.winner = null;

        if (reloadGenres) {
            renderGenreSkeletons();
            apiGet('Genres', {
                mediaType: state.mediaType,
                libraryId: state.libraryId || null,
                limit: MAX_GENRES
            }).then(function (genres) {
                state.genreList = genres || [];
                state.genres = state.genres.filter(function (g) {
                    return state.genreList.some(function (x) { return x.name === g; });
                });
                renderGenres();
            }).catch(function () {
                renderGenres();
            });
        }

        buildRing();
        schedulePrime();   // ← NUEVO: rellena el anillo con pósters del nuevo filtro
    }

    function primeRing() {
        if (state.pool.length > 0) return Promise.resolve();
        return apiGet('Spin', currentFilters()).then(function (data) {
            state.total = data.totalMatches || 0; state.winner = null; state.pool = buildPool(data, true); buildRing();
            if (state.total > 0) setStatus(STR.matches(state.total));
        }).catch(function () {});
    }

    function buildPool(data, discardWinner) {
        var candidates = (data.candidates || []).filter(function (c) { return !!c; }); shuffle(candidates);
        if (discardWinner || !data.winner) return candidates;
        var pool = candidates.slice(); var at = pool.length ? Math.floor(Math.random() * pool.length) : 0;
        pool.splice(at, 0, data.winner); state.winnerIndex = at; return pool;
    }

    function spin() {
        if (state.phase === 'spinning' || state.phase === 'settling' || state.phase === 'shuffling') return;
        hideNotice(); hideResult();
        state.reducedMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
        els.spinBtn.disabled = true; els.spinBtn.innerHTML = '<span class="jr-spinner" style="width:18px;height:18px;border-width:2px"></span><span>' + STR.spinning + '</span>';
        setStatus(STR.shuffling); var startedAt = performance.now();

        if (state.reducedMotion) {
            state.phase = 'spinning';
            apiGet('Spin', currentFilters()).then(function (data) { finish(data, 0); }).catch(function (err) { fail(err); });
            return;
        }

        state.phase = 'shuffling'; state.velocity = 780; els.ring.classList.add('jr-spinning'); els.overlay.classList.add('jr-shuffling'); startLoop();
        if (state.pool.length === 0) {
            state.winner = null; state.pool = [];
            for (var i = 0; i < 14; i++) state.pool.push({ id: 'ph-' + i, name: '', hasPrimaryImage: false });
            buildRing();
        }

        apiGet('Spin', currentFilters()).then(function (data) {
            var elapsed = performance.now() - startedAt; var wait = Math.max(0, 620 - elapsed);
            window.setTimeout(function () { finish(data, wait); }, wait);
        }).catch(function (err) { fail(err); });
    }

    function finish(data, extraDelay) {
        state.total = data.totalMatches || 0;
        if (!data.winner) {
            stopSpinVisuals(); setStatus('');
            showNotice('empty', STR.emptyTitle, STR.emptyText, STR.spin, function () { hideNotice(); spin(); });
            resetSpinButton(); state.phase = 'idle'; return;
        }
        setStatus(STR.matches(state.total));
        state.winner = data.winner; state.pool = buildPool(data, false); buildRing();
        var duration = clamp(state.spinDuration, 1600, 9000); state.phase = 'settling'; els.ring.classList.remove('jr-spinning');
        settleOn(state.winnerIndex, duration, function () { window.setTimeout(function () { revealWinner(data.winner); }, 180); });
    }

    function fail(err) {
        stopSpinVisuals(); resetSpinButton(); state.phase = 'idle'; setStatus('');
        var is401 = err && (err.status === 401 || /unauthorized/i.test(String(err && err.message)));
        showNotice('error', STR.errorTitle, is401 ? STR.unauthorizedText : STR.errorText + ' (' + ((err && err.message) || 'error') + ')', STR.retry, function () { hideNotice(); spin(); });
    }

    function stopSpinVisuals() { stopLoop(); els.ring.classList.remove('jr-spinning'); els.overlay.classList.remove('jr-shuffling'); }
    function resetSpinButton() { els.spinBtn.disabled = false; els.spinBtn.innerHTML = ICON.dice + '<span>' + STR.spin + '</span>'; }

    function revealWinner(item) {
        stopLoop(); els.overlay.classList.remove('jr-shuffling');
        els.ring.style.transition = 'opacity .32s ease, transform .42s ease'; els.ring.style.opacity = '0';
        els.viewport.style.transition = 'opacity .32s ease, transform .42s ease'; els.viewport.style.opacity = '0'; els.viewport.style.transform = 'scale(.82)';
        els.pointer.style.opacity = '0';
        renderHero(item); els.result.dataset.show = 'true'; state.phase = 'done';
        resetSpinButton(); els.spinBtn.innerHTML = ICON.refresh + '<span>' + STR.spinAgain + '</span>';
        var focusTarget = els.result.querySelector('.jr-btn-primary') || els.result.querySelector('.jr-btn');
        if (focusTarget) window.setTimeout(function () { focusTarget.focus(); }, 60);
    }

    function hideResult() {
        if (!els.result) return; els.result.dataset.show = 'false'; els.result.textContent = '';
        if (els.ring) { els.ring.style.transition = ''; els.ring.style.opacity = ''; }
        if (els.viewport) { els.viewport.style.transition = ''; els.viewport.style.opacity = ''; els.viewport.style.transform = ''; }
        if (els.pointer) els.pointer.style.opacity = '';
        if (els.spinBtn) els.spinBtn.innerHTML = ICON.dice + '<span>' + STR.spin + '</span>';
        state.phase = 'idle';
    }

    function renderHero(item) {
        var hero = el('div', 'jr-hero');
        var poster = el('div', 'jr-hero-poster');
        var badge = el('span', 'jr-badge', item.type === 'Series' ? STR.winnerSeries : STR.winnerMovie);
        poster.appendChild(badge);
        var purl = posterUrl(item, 420);
        if (purl) {
            var img = document.createElement('img'); img.alt = item.name || ''; img.src = purl;
            img.addEventListener('error', function () { if (img.parentNode) img.parentNode.removeChild(img); poster.appendChild(placeholder(item)); });
            poster.appendChild(img);
        } else { poster.appendChild(placeholder(item)); }

        var body = el('div', 'jr-hero-body');
        body.appendChild(el('div', 'jr-hero-kicker', STR.winnerKicker));
        body.appendChild(el('h3', 'jr-hero-title', item.name || '—'));
        var meta = el('div', 'jr-meta');
        if (item.year) meta.appendChild(el('span', 'jr-pill', String(item.year)));
        if (item.runTimeMinutes) meta.appendChild(el('span', 'jr-pill', item.runTimeMinutes + ' ' + STR.minutes));
        if (item.communityRating) { var r = el('span', 'jr-pill jr-rating'); r.textContent = '★ ' + (Math.round(item.communityRating * 10) / 10).toFixed(1); meta.appendChild(r); }
        if (item.officialRating) meta.appendChild(el('span', 'jr-pill', item.officialRating));
        if (item.played) meta.appendChild(el('span', 'jr-pill', STR.seen));
        if (item.isFavorite) meta.appendChild(el('span', 'jr-pill', STR.favorite));
        body.appendChild(meta);

        if (item.genres && item.genres.length) {
            var gRow = el('div', 'jr-meta');
            item.genres.slice(0, 4).forEach(function (g) { var lab = genreLabel(g); gRow.appendChild(el('span', 'jr-pill', lab.emoji + ' ' + lab.text)); });
            body.appendChild(gRow);
        }
        if (item.overview) body.appendChild(el('p', 'jr-hero-overview', item.overview));

        var actions = el('div', 'jr-hero-actions');
        var playBtn = el('button', 'jr-btn jr-btn-primary'); playBtn.type = 'button'; playBtn.innerHTML = ICON.play + '<span>' + STR.playItem + '</span>';
        playBtn.addEventListener('click', function () { openItem(item.id, true); });
        var openBtn = el('button', 'jr-btn'); openBtn.type = 'button'; openBtn.innerHTML = ICON.info + '<span>' + STR.openItem + '</span>';
        openBtn.addEventListener('click', function () { openItem(item.id, false); });
        var againBtn = el('button', 'jr-btn'); againBtn.type = 'button'; againBtn.innerHTML = ICON.refresh + '<span>' + STR.spinAgain + '</span>';
        againBtn.addEventListener('click', function () { hideResult(); spin(); });
        actions.appendChild(playBtn); actions.appendChild(openBtn); actions.appendChild(againBtn);
        body.appendChild(actions);
        hero.appendChild(poster); hero.appendChild(body);
        els.result.textContent = ''; els.result.appendChild(hero);
    }

    function openItem(id, autoPlay) {
        close(); var target = 'details?id=' + encodeURIComponent(id);
        try { if (window.Dashboard && typeof window.Dashboard.navigate === 'function') window.Dashboard.navigate(target); else window.location.hash = '#/' + target; } catch (e) { window.location.hash = '#/' + target; }
        if (!autoPlay) return;
        var selectors = ['.btnPlayOrResume', '.detailPagePrimaryContainer .btnPlay', '.btnPlay']; var tries = 0;
        var timer = window.setInterval(function () {
            tries++;
            for (var i = 0; i < selectors.length; i++) {
                var nodes = document.querySelectorAll(selectors[i]);
                for (var j = 0; j < nodes.length; j++) {
                    var n = nodes[j];
                    if (n && n.offsetParent !== null && !n.classList.contains('hide')) { try { n.click(); } catch (e) {} window.clearInterval(timer); return; }
                }
            }
            if (tries > 40) window.clearInterval(timer);
        }, 100);
    }

    /* ==================================================================
       7. Apertura / cierre / foco
       ================================================================== */
    function open() {
        buildOverlay(); applyTheme(els.overlay);
        state.lastFocus = document.activeElement; state.open = true; els.overlay.dataset.open = 'true'; document.body.style.overflow = 'hidden';
        window.requestAnimationFrame(function () { computeGeometry(); buildRing(); primeRing(); var closeBtn = els.overlay.querySelector('.jr-icon-btn'); if (closeBtn) closeBtn.focus(); });
    }
    function close() {
        if (!state.open) return; state.open = false; stopSpinVisuals(); els.overlay.dataset.open = 'false'; document.body.style.overflow = '';
        hideResult(); hideNotice(); setStatus('');
        if (state.lastFocus && state.lastFocus.focus) { try { state.lastFocus.focus(); } catch (e) {} }
    }

    document.addEventListener('keydown', function (ev) {
        if (!state.open) return;
        if (ev.key === 'Escape') { ev.preventDefault(); close(); return; }
        if (ev.key === 'Tab') {
            var focusables = els.overlay.querySelectorAll('button:not([disabled]), select, input, a[href], [tabindex]:not([tabindex="-1"])');
            if (!focusables.length) return;
            var first = focusables[0]; var last = focusables[focusables.length - 1];
            if (ev.shiftKey && document.activeElement === first) { ev.preventDefault(); last.focus(); }
            else if (!ev.shiftKey && document.activeElement === last) { ev.preventDefault(); first.focus(); }
        }
    }, true);

    JR.open = open; JR.close = close; JR.spin = spin; JR.state = state;
    JR.api = { get: apiGet, serverAddress: serverAddress, posterUrl: posterUrl }; JR.uiLoaded = true;
    if (window.console && console.info) console.info(LOG, 'interfaz lista. Prueba en consola: JellyRoulette.open()');
})();