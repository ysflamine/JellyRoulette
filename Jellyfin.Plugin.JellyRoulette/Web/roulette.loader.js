/* =========================================================================
   JellyRoulette — loader.js
   =========================================================================
   Este es el ÚNICO archivo que el middleware inserta en /web/index.html
   (como <script defer src=".../JellyRoulette/loader.js">).

   Su trabajo es pequeño y deliberadamente aburrido:
     1. Averiguar la URL base del plugin (para respetar instalaciones bajo
        una subcarpeta, p. ej. https://srv/jellyfin/web/).
     2. Inyectar la hoja de estilos.
     3. Esperar a que haya una sesión iniciada (no hacemos nada en la pantalla
        de login, donde no existe ApiClient).
     4. Cargar roulette.js (diferido: no ralentiza el arranque de Jellyfin).
     5. Colocar los puntos de entrada (botón de cabecera / cajón / botón
        flotante) y VOLVER a colocarlos cuando React re-renderice el DOM.

   Todo está envuelto en un IIFE con try/catch: si algo falla, Jellyfin sigue
   funcionando exactamente igual que sin el plugin.
   ========================================================================= */
(function () {
    'use strict';

    /* Idempotencia: si por lo que sea el script se carga dos veces, salimos. */
    if (window.__jellyRouletteBooted) {
        return;
    }
    window.__jellyRouletteBooted = true;

    var LOG_PREFIX = '[JellyRoulette]';

    /* ------------------------------------------------------------------
       1) URL base del plugin
       ------------------------------------------------------------------ */
    function detectBase() {
        // currentScript existe durante la ejecución de scripts clásicos (incluso defer).
        var src = document.currentScript && document.currentScript.src;

        if (!src) {
            var all = document.getElementsByTagName('script');
            for (var i = all.length - 1; i >= 0; i--) {
                if (all[i].src && all[i].src.indexOf('/JellyRoulette/loader.js') !== -1) {
                    src = all[i].src;
                    break;
                }
            }
        }

        if (src) {
            // El middleware genera la URL como /JellyRoulette/loader.js?v=<versión del plugin>,
            // así que de aquí sacamos la versión exacta sin esperar a /Info. Esto evita una
            // carrera: sin ello, el CSS y el JS se pedirían con ?v=1 y luego con ?v=1.0.1,
            // descargándose dos veces.
            var m = /[?&]v=([^&#]+)/.exec(src);
            if (m) {
                try { JR_VERSION_FROM_URL = decodeURIComponent(m[1]); } catch (e) { /* ignorable */ }
            }
            // Recortamos "/loader.js" (y el ?v=x.y.z) para quedarnos con la carpeta.
            return src.split('?')[0].replace(/\/loader\.js$/, '');
        }

        // Respaldo: misma carpeta donde está la web + /JellyRoulette
        var loc = window.location;
        var pathBase = loc.pathname.replace(/\/web\/?(index\.html)?$/, '');
        return loc.origin + pathBase + '/JellyRoulette';
    }

    var JR_VERSION_FROM_URL = null;
    var BASE = detectBase();

    /* Espacio de nombres compartido con roulette.js */
    var JR = (window.JellyRoulette = window.JellyRoulette || {});
    JR.base = BASE;
    JR.ready = JR.ready || false;
    // La versión exacta del plugin viaja en nuestra propia URL (?v=), puesta por el
    // middleware. Se usa como cache-buster de roulette.css y roulette.js.
    JR.version = JR_VERSION_FROM_URL || JR.version || '1';

    /* ------------------------------------------------------------------
       2) Utilidades mínimas
       ------------------------------------------------------------------ */
    function log() {
        if (window.console && console.info) {
            console.info.apply(console, [LOG_PREFIX].concat([].slice.call(arguments)));
        }
    }

    function warn() {
        if (window.console && console.warn) {
            console.warn.apply(console, [LOG_PREFIX].concat([].slice.call(arguments)));
        }
    }

    /** Devuelve el ApiClient de Jellyfin si existe (o null). */
    function apiClient() {
        if (window.ApiClient && typeof window.ApiClient.accessToken === 'function') {
            return window.ApiClient;
        }
        var sc = window.ServerConnections;
        if (sc && typeof sc.currentApiClient === 'function') {
            try {
                return sc.currentApiClient();
            } catch (e) {
                return null;
            }
        }
        return null;
    }

    /** ¿Hay sesión iniciada? */
    function isLoggedIn() {
        var c = apiClient();
        if (!c) {
            return false;
        }
        try {
            return !!c.accessToken();
        } catch (e) {
            return false;
        }
    }

    JR.getApiClient = apiClient;

    /* ------------------------------------------------------------------
       3) Inyección de CSS (una sola vez)
       ------------------------------------------------------------------ */
    function injectStyles() {
        if (document.getElementById('jr-styles')) {
            return;
        }
        var link = document.createElement('link');
        link.id = 'jr-styles';
        link.rel = 'stylesheet';
        link.type = 'text/css';
        link.href = BASE + '/roulette.css?v=' + encodeURIComponent(JR.version || '1');
        // El CSS no bloquea nada y no depende del orden de carga de Jellyfin.
        (document.head || document.documentElement).appendChild(link);
    }

    /* ------------------------------------------------------------------
       4) Carga diferida de la interfaz (roulette.js)
       ------------------------------------------------------------------ */
    var uiLoading = false;

    function loadUi(callback) {
        if (JR.uiLoaded) {
            if (callback) callback();
            return;
        }
        if (uiLoading) {
            return;
        }
        uiLoading = true;

        var s = document.createElement('script');
        s.src = BASE + '/roulette.js?v=' + encodeURIComponent(JR.version || '1');
        s.async = true;
        s.onload = function () {
            JR.uiLoaded = true;
            uiLoading = false;
            log('interfaz cargada');
            if (callback) callback();
        };
        s.onerror = function () {
            uiLoading = false;
            warn('no se pudo cargar roulette.js desde', s.src);
        };
        document.body.appendChild(s);
    }

    JR.ensureUi = function (callback) {
        loadUi(callback);
    };

    /* ------------------------------------------------------------------
       5) Puntos de entrada en la interfaz de Jellyfin
       ------------------------------------------------------------------
       Jellyfin 12 tiene dos layouts conviviendo:
         · "moderno" (React + MUI)  → <header class="MuiAppBar-root"> .MuiToolbar-root
         · "clásico" (legacy)       → <div class="skinHeader"> .headerRight
       Probamos ambos y nos quedamos con el primero que exista. Un
       MutationObserver vuelve a intentarlo cuando React sustituye el DOM.
       ------------------------------------------------------------------ */

    var DICE_SVG =
        '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
        '<rect x="3" y="3" width="18" height="18" rx="4" fill="currentColor" opacity=".18"/>' +
        '<rect x="3" y="3" width="18" height="18" rx="4" stroke="currentColor" stroke-width="1.7"/>' +
        '<circle cx="8.5" cy="8.5" r="1.6" fill="currentColor"/>' +
        '<circle cx="15.5" cy="15.5" r="1.6" fill="currentColor"/>' +
        '<circle cx="12" cy="12" r="1.6" fill="currentColor"/>' +
        '</svg>';

    var entryPoints = [];

    function openRoulette() {
        loadUi(function () {
            if (JR.open) {
                JR.open();
            }
        });
    }

    JR.openFromAnywhere = openRoulette;

    /** Crea un botón de cabecera con el aspecto nativo de cada layout. */
    function makeHeaderButton(kind) {
        var btn = null;

        if (kind === 'legacy') {
            // En el layout clásico Jellyfin usa <button is="paper-icon-button-light">,
            // un "customized built-in element". Si el navegador lo soporta, lo creamos
            // así para heredar exactamente el mismo hover/focus que el resto de botones.
            try {
                btn = document.createElement('button', { is: 'paper-icon-button-light' });
            } catch (e) {
                btn = null;
            }
        }

        if (!btn) {
            btn = document.createElement('button');
        }

        btn.type = 'button';
        btn.className = kind === 'legacy'
            ? 'jr-header-button headerButton headerButtonRight jr-root'
            : 'jr-header-button jr-mui-button MuiIconButton-root MuiIconButton-sizeLarge jr-root';
        btn.setAttribute('aria-label', 'JellyRoulette');
        btn.title = 'JellyRoulette — ¿qué vemos hoy?';
        btn.innerHTML =
            (kind === 'legacy'
                ? '<span class="material-icons" aria-hidden="true">casino</span>'
                : DICE_SVG);

        btn.addEventListener('click', function (ev) {
            ev.preventDefault();
            ev.stopPropagation();
            openRoulette();
        });

        return btn;
    }

    /** Botón flotante (respaldo universal, sobre todo en móvil). */
    function makeFab() {
        var fab = document.createElement('button');
        fab.type = 'button';
        fab.className = 'jr-fab jr-root';
        fab.id = 'jr-fab';
        fab.setAttribute('aria-label', 'Abrir JellyRoulette');
        fab.title = 'JellyRoulette';
        fab.innerHTML = DICE_SVG;
        fab.addEventListener('click', openRoulette);
        return fab;
    }

    /** Enlace para el cajón de navegación clásico. */
    function makeDrawerLink() {
        var a = document.createElement('a');
        a.className = 'navMenuOption lnkMediaFolder jr-drawer-link jr-root';
        a.href = '#';
        a.innerHTML =
            '<span class="material-icons navMenuOptionIcon casino" aria-hidden="true"></span>' +
            '<span class="navMenuOptionText">JellyRoulette</span>';
        a.addEventListener('click', function (ev) {
            ev.preventDefault();
            openRoulette();
            // Cierra el cajón como lo haría un enlace nativo.
            var closeBtn = document.querySelector('.mainDrawer .btnCloseDrawer');
            if (closeBtn) closeBtn.click();
            document.body.classList.remove('drawer-open');
        });
        return a;
    }

    /**
     * Una "receta" = cómo encontrar un anclaje y qué meter ahí.
     * Cada receta es idempotente: si ya colocamos nuestro elemento, no toca nada.
     */
    var recipes = [
        {
            // Layout moderno (MUI) → caja derecha de la barra de herramientas
            id: 'mui-toolbar',
            find: function () {
                var toolbar = document.querySelector('.MuiAppBar-root .MuiToolbar-root');
                if (!toolbar) return null;
                // La caja que agrupa los botones de la derecha es la penúltima hija.
                var kids = toolbar.children;
                var anchor = kids.length >= 2 ? kids[kids.length - 2] : toolbar;
                return { anchor: anchor, first: true };
            },
            create: function () { return makeHeaderButton('mui'); }
        },
        {
            // Layout clásico → zona derecha de la cabecera
            id: 'legacy-header',
            find: function () {
                var right = document.querySelector('.skinHeader .headerRight');
                if (!right) return null;
                var search = right.querySelector('.headerSearchButton');
                return { anchor: right, before: search || null };
            },
            create: function () { return makeHeaderButton('legacy'); }
        },
        {
            // Cajón de navegación clásico
            id: 'legacy-drawer',
            find: function () {
                var drawer = document.querySelector('.mainDrawer .drawerContent, .mainDrawer');
                if (!drawer) return null;
                var home = drawer.querySelector('.navMenuOption[href="#/home"]');
                if (!home || !home.parentNode) return null;
                return { anchor: home.parentNode, after: home };
            },
            create: makeDrawerLink
        },
        {
            // Cajón moderno (solo se renderiza en pantallas pequeñas)
            id: 'mui-drawer',
            find: function () {
                var list = document.querySelector('.MuiDrawer-root .MuiList-root');
                if (!list) return null;
                return { anchor: list, last: true };
            },
            create: function () {
                var a = makeDrawerLink();
                a.className = 'navMenuOption jr-drawer-link jr-root MuiButtonBase-root MuiListItemButton-root';
                return a;
            }
        }
    ];

    function applyRecipes() {
        var placedAny = false;

        for (var i = 0; i < recipes.length; i++) {
            var recipe = recipes[i];
            try {
                var found = recipe.find();
                if (!found || !found.anchor || !found.anchor.isConnected) {
                    continue;
                }

                var existing = found.anchor.querySelector('.jr-header-button, .jr-drawer-link');
                if (existing) {
                    placedAny = true;
                    continue;
                }

                var el = recipe.create();
                if (found.before && found.before.parentNode === found.anchor) {
                    found.anchor.insertBefore(el, found.before);
                } else if (found.after && found.after.nextSibling) {
                    found.anchor.insertBefore(el, found.after.nextSibling);
                } else if (found.after) {
                    found.anchor.appendChild(el);
                } else if (found.first) {
                    found.anchor.insertBefore(el, found.anchor.firstChild);
                } else if (found.last) {
                    found.anchor.appendChild(el);
                } else {
                    found.anchor.appendChild(el);
                }

                entryPoints.push(el);
                placedAny = true;
            } catch (e) {
                warn('fallo al inyectar el punto de entrada', recipe.id, e);
            }
        }

        return placedAny;
    }

    function ensureFab(force) {
        // JR.config llega de GET /JellyRoulette/Info (ajuste «Botón flotante»).
        var mode = (JR.config && JR.config.floatingButton) || 'Auto';
        if (force === true) {
            mode = 'Always';
        }
        if (mode === 'Never') {
            var fab0 = document.getElementById('jr-fab');
            if (fab0 && fab0.parentNode) {
                fab0.parentNode.removeChild(fab0);
            }
            return;
        }

        if (document.getElementById('jr-fab')) {
            return;
        }

        if (mode === 'Auto') {
            // En pantallas pequeñas el FAB es la vía más cómoda; en escritorio solo
            // lo sacamos si no conseguimos colocar el botón en la cabecera.
            var isSmall = window.matchMedia && window.matchMedia('(max-width: 700px)').matches;
            if (!isSmall && entryPoints.length > 0) {
                return;
            }
        }

        document.body.appendChild(makeFab());
    }

    /* Observador: React reconstruye la cabecera al navegar, así que reintentamos.
       Usamos "childList + subtree" pero con un throttle para no gastar CPU. */
    var pending = false;
    var scheduled = false;

    function scheduleApply() {
        pending = true;
        if (scheduled) return;
        scheduled = true;
        window.requestAnimationFrame(function () {
            scheduled = false;
            if (!pending) return;
            pending = false;
            // Guardia barata: si el documento ya no es utilizable (p. ej. navegación
            // en curso), no tocamos nada.
            if (!document || !document.body || !document.body.isConnected) return;
            try {
                applyRecipes();
                ensureFab();
            } catch (e) {
                /* nunca romper la web */
            }
        });
    }

    var observer = null;

    function startObserving() {
        if (observer) return;
        try {
            observer = new MutationObserver(scheduleApply);
            observer.observe(document.body, { childList: true, subtree: true });
        } catch (e) {
            warn('MutationObserver no disponible', e);
        }
    }

    /* ------------------------------------------------------------------
       6) Atajo de teclado (Ctrl/⌘ + Shift + J)
       ------------------------------------------------------------------ */
    function bindShortcut() {
        document.addEventListener('keydown', function (ev) {
            // Evitamos robar atajos dentro de campos de texto.
            var t = ev.target;
            if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) {
                return;
            }
            if ((ev.ctrlKey || ev.metaKey) && ev.shiftKey && (ev.key === 'J' || ev.key === 'j')) {
                ev.preventDefault();
                openRoulette();
            }
        }, true);
    }

    /* ------------------------------------------------------------------
       7) Arranque
       ------------------------------------------------------------------ */
    function boot() {
        injectStyles();
        startObserving();
        bindShortcut();

        var ok = applyRecipes();
        ensureFab(false);

        if (!ok) {
            // No encontramos cabecera conocida (p. ej. un tema muy custom):
            // el FAB garantiza que el plugin siga siendo accesible.
            ensureFab(true);
        }

        // Refresco periódico barato por si React tarda en montar la cabecera.
        var tries = 0;
        var timer = window.setInterval(function () {
            tries++;
            applyRecipes();
            ensureFab();
            if (tries > 12) {
                window.clearInterval(timer);
            }
        }, 500);

        // Precargamos la interfaz cuando el navegador está ocioso: abrir la
        // ruleta será instantáneo sin penalizar el arranque de Jellyfin.
        var prewarm = function () { loadUi(null); };
        if (window.requestIdleCallback) {
            window.requestIdleCallback(prewarm, { timeout: 6000 });
        } else {
            window.setTimeout(prewarm, 4000);
        }

        loadConfig();

        log('listo. Atajo: Ctrl/Cmd + Shift + J');
    }

    /**
     * Lee los ajustes del plugin desde el servidor para que lo que configures en
     * Panel → Plugins → JellyRoulette se note en la web sin recompilar nada.
     */
    function loadConfig() {
        var c = apiClient();
        if (!c || typeof c.ajax !== 'function') {
            return;
        }

        c.ajax({
            type: 'GET',
            url: BASE + '/Info',
            dataType: 'json'
        }).then(function (info) {
            if (!info) return;
            JR.config = {
                floatingButton: info.ui ? (info.ui.floatingButton || 'Auto') : 'Auto',
                spinDurationMs: info.ui ? info.ui.spinDurationMs : 4200,
                candidates: info.ui ? info.ui.candidates : 28,
                defaults: info.defaults || null
            };
            JR.version = info.version || JR.version;
            applyRecipes();
            ensureFab();
        }).catch(function (err) {
            warn('no se pudieron leer los ajustes del plugin', err);
        });
    }

    function waitForSession() {
        if (isLoggedIn()) {
            boot();
            return;
        }

        // Escuchamos el evento de Jellyfin si está disponible…
        var sc = window.ServerConnections;
        if (window.Events && sc && window.Events.on) {
            try {
                window.Events.on(sc, 'localusersignedin', function () {
                    window.setTimeout(boot, 250);
                });
            } catch (e) { /* ignorable */ }
        }

        // …y además sondeamos por si acaso (la pantalla de login no emite eventos
        // si el usuario ya tenía sesión guardada).
        var attempts = 0;
        var poll = window.setInterval(function () {
            attempts++;
            if (isLoggedIn()) {
                window.clearInterval(poll);
                boot();
            } else if (attempts > 600) {
                // ~5 minutos sin sesión: dejamos de molestar.
                window.clearInterval(poll);
            }
        }, 500);
    }

    try {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', waitForSession);
        } else {
            waitForSession();
        }
    } catch (e) {
        warn('error durante el arranque', e);
    }
})();
