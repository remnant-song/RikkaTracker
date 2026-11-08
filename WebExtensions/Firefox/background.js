// Firefox port of the Chrome extension service-worker.js.
// Talks the same WebSocket protocol to RikkaTracker (ws://127.0.0.1:8910):
// outgoing JSON {Url, Title, Icon, Duration, ActiveTime, Browser}, incoming
// plain-text "ping"/"sleep"/"wake".
//
// Uses Manifest V2 on purpose: a persistent background page keeps the
// WebSocket alive indefinitely, while Firefox MV3 event pages are suspended
// after ~30s idle and the suspension tears the socket down (Firefox, unlike
// Chrome 116+, does not count an open WebSocket as a keep-alive).

const TEN_SECONDS_MS = 10 * 1000;
const RECONNECT_FAIL_SLEEP = 5;
const WSURL = 'ws://127.0.0.1:8910';

const actionApi = browser.action || browser.browserAction;

let webSocket = null;
let isConnected = false;
let isFirefoxFocused = true;
let autoReConnectIntervalId = null;
let isSleep = false;
let activePage = null;
let reconnectFail = 0;
let notifyFailList = [];
let browserName = '';

init();

async function init() {
    detectBrowser();
    connect();
    startRenotify();
}

// Distinguish Zen Browser / Firefox / other Gecko forks for the tracker.
// Zen keeps navigator.userAgent and runtime.getBrowserInfo() identical to
// Firefox, so its bundled "Zen ..." system extensions are the reliable cue.
async function detectBrowser() {
    try {
        const info = await browser.runtime.getBrowserInfo();
        let name = info && info.name ? info.name : 'Firefox';
        try {
            const all = await browser.management.getAll();
            const zenBuiltin = all.some((m) =>
                m.type === 'extension' && (
                    /^zen(\s|-|$)/i.test(m.name || '') ||
                    (m.creator && m.creator.name && /^zen/i.test(m.creator.name)) ||
                    m.id === '{3c078156-979c-498b-8990-85f7987dd929}'));
            if (zenBuiltin) name = 'Zen Browser';
        } catch (e) { /* management unavailable: keep Gecko name */ }
        browserName = name;
    } catch (e) {
        const ua = navigator.userAgent;
        browserName = /zen/i.test(ua) ? 'Zen Browser'
            : /firefox/i.test(ua) ? 'Firefox'
            : 'Firefox';
    }
}

function connect() {
    webSocket = new WebSocket(WSURL);
    webSocket.onopen = () => {
        isConnected = true;
        isSleep = false;
        reconnectFail = 0;
        clearInterval(autoReConnectIntervalId);
        actionApi.setIcon({ path: 'icons/active.png' });
        keepAlive();
        console.log('[RikkaTracker] Connected');
    };
    webSocket.onmessage = (event) => {
        if (event.data === 'sleep') {
            isSleep = true;
            calDuration();
        } else if (event.data === 'wake') {
            isSleep = false;
        }
    };
    webSocket.onclose = () => {
        isConnected = false;
        actionApi.setIcon({ path: 'icons/inactive.png' });
        webSocket = null;
        startAutoReConnect();
    };
}

function startAutoReConnect() {
    clearInterval(autoReConnectIntervalId);
    autoReConnectIntervalId = setInterval(() => {
        if (!isConnected) {
            console.log('[RikkaTracker] Reconnecting...');
            connect();
            reconnectFail++;
            if (reconnectFail >= RECONNECT_FAIL_SLEEP && !isSleep) {
                isSleep = true;
            }
        }
    }, TEN_SECONDS_MS);
}

function keepAlive() {
    const keepAliveIntervalId = setInterval(() => {
        if (isConnected && webSocket) {
            webSocket.send('ping');
        } else {
            clearInterval(keepAliveIntervalId);
        }
    }, TEN_SECONDS_MS);
}

browser.tabs.onActivated.addListener(async (e) => {
    const tab = await browser.tabs.get(e.tabId);
    onActivePage(tab);
});

browser.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    if (changeInfo.status === 'complete' && tab.active) {
        onActivePage(tab);
    }
});

async function getCurrentTab() {
    const tabs = await browser.tabs.query({ active: true });
    return tabs && tabs.length > 0 ? tabs[0] : null;
}

// Event-driven focus tracking (cheaper and more precise than the 1s polling
// the Chrome version uses); the persistent page never misses the events.
browser.windows.onFocusChanged.addListener((windowId) => {
    handleFocus(windowId !== browser.windows.WINDOW_ID_NONE);
});

function handleFocus(focused) {
    if (focused) {
        if (!isFirefoxFocused) {
            isFirefoxFocused = true;
            getCurrentTab().then((tab) => {
                if (tab) onActivePage(tab);
            });
        }
    } else {
        if (isFirefoxFocused) {
            isFirefoxFocused = false;
            calDuration();
        }
    }
}

function onActivePage(tab) {
    if (isSleep) return;

    if (activePage && activePage.url) {
        if (activePage.url !== tab.url) {
            calDuration();
            setActive(tab);
        }
    } else {
        setActive(tab);
    }
}

function setActive(tab) {
    const { url, title, favIconUrl } = tab;
    if (url) {
        activePage = {
            url,
            title: title || '',
            icon: favIconUrl || '',
            startTime: new Date().getTime()
        };
    } else {
        activePage = null;
    }
}

function calDuration() {
    if (activePage && activePage.url) {
        const now = new Date().getTime();
        const duration = parseInt((now - activePage.startTime) / 1000);
        const activeTime = parseInt(activePage.startTime / 1000);
        const data = {
            Url: activePage.url,
            Title: activePage.title,
            Icon: activePage.icon,
            Duration: duration,
            ActiveTime: activeTime,
            Browser: browserName
        };
        activePage = null;
        notifyTai(data);
    }
}

function notifyTai(data) {
    if (isConnected && webSocket) {
        webSocket.send(JSON.stringify(data));
    } else {
        notifyFailList.push(data);
    }
}

function renotify() {
    if (isConnected && webSocket) {
        while (notifyFailList.length > 0) {
            notifyTai(notifyFailList.shift());
        }
    }
}

function startRenotify() {
    setInterval(() => {
        renotify();
    }, TEN_SECONDS_MS);
}
