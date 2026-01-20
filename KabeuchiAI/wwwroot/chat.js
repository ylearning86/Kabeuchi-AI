const chatLog = [];
let currentAbortController = null;
let currentThinkingMessageEl = null;
let isComposing = false;

const quickPrompts = [
    {
        label: 'アイデア整理',
        text: 'ビジネスアイデアを考え始めました。まず何から整理すればいい？'
    },
    {
        label: 'ターゲット',
        text: 'このアイデアのターゲット顧客を一緒に決めたい。質問して。'
    },
    {
        label: '競合',
        text: '競合や代替手段を洗い出したい。どう考えればいい？'
    },
    {
        label: '収益モデル',
        text: '収益モデル（どうやってお金を稼ぐか）を一緒に考えて。'
    }
];

// メッセージ送信機能
async function sendMessage() {
    const input = document.getElementById('messageInput');
    const message = (input.value ?? '').trim();

    if (message === '') return;

    // If a request is already running, ignore.
    if (currentAbortController) return;

    // ユーザーメッセージを表示
    appendMessage(message, 'user');

    // After first user message, collapse quick prompts to prioritize chat
    hideQuickPrompts();

    input.value = '';
    input.disabled = true;
    autosizeInput();

    setBusyState(true);
    showThinkingMessage();

    currentAbortController = new AbortController();

    try {
        // バックエンドAPIに送信
        const response = await fetch('/api/chat', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ message: message }),
            signal: currentAbortController.signal
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();
        // Backward/forward compatibility: accept either { response } or { Response }
        const responseText = (data && (data.response ?? data.Response)) ?? '';
        const meta = data && (data.meta ?? data.Meta);

        removeThinkingMessage();
        appendMessage(responseText, 'assistant', meta);
    } catch (error) {
        removeThinkingMessage();
        if (error && (error.name === 'AbortError' || String(error).includes('AbortError'))) {
            appendMessage('キャンセルしました。', 'system-message');
        } else {
            appendMessage(`エラーが発生しました: ${error.message}`, 'assistant');
        }
    } finally {
        currentAbortController = null;
        setBusyState(false);
        input.disabled = false;
        input.focus();
    }
}

// メッセージを追加
function appendMessage(text, sender, meta) {
    const chatBox = document.getElementById('chatBox');
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${sender}`;

    const content = document.createElement('div');
    content.className = 'message-content';

    if (sender === 'assistant') {
        const formatted = formatAssistantText(text);
        // If markdown libs are available, render markdown safely; otherwise fall back to plain text.
        if (window.marked && window.DOMPurify) {
            const html = window.marked.parse(formatted, { breaks: true, gfm: true });
            content.innerHTML = window.DOMPurify.sanitize(html);
        } else {
            content.textContent = formatted;
        }
    } else {
        content.textContent = text;
    }

    messageDiv.appendChild(content);

    // Keep a lightweight log for export
    chatLog.push({
        sender,
        text: String(text ?? ''),
        createdAt: new Date().toISOString()
    });

    // Assistant details panel (B): show model + tools actually used
    if (sender === 'assistant' && meta) {
        const model = meta.model ?? meta.Model;
        const toolsUsed = meta.toolsUsed ?? meta.ToolsUsed;
        const toolsList = Array.isArray(toolsUsed) ? toolsUsed.filter(Boolean) : [];

        const details = document.createElement('details');
        details.className = 'meta-details';

        const summary = document.createElement('summary');
        summary.textContent = '詳細';
        details.appendChild(summary);

        const metaDiv = document.createElement('div');
        metaDiv.className = 'meta-panel';

        const modelRow = document.createElement('div');
        modelRow.className = 'meta-row';
        modelRow.textContent = `モデル: ${model || '不明'}`;
        metaDiv.appendChild(modelRow);

        const toolsRow = document.createElement('div');
        toolsRow.className = 'meta-row';
        toolsRow.textContent = `使用ツール: ${toolsList.length ? toolsList.join(', ') : 'なし'}`;
        metaDiv.appendChild(toolsRow);

        details.appendChild(metaDiv);
        messageDiv.appendChild(details);
    }
    chatBox.appendChild(messageDiv);

    // スクロール下部へ
    chatBox.scrollTop = chatBox.scrollHeight;
}

function setBusyState(isBusy) {
    const input = document.getElementById('messageInput');
    const sendBtn = document.getElementById('sendBtn');
    const cancelBtn = document.getElementById('cancelBtn');
    if (sendBtn) sendBtn.disabled = !!isBusy;
    if (cancelBtn) cancelBtn.disabled = !isBusy;
    if (input) input.disabled = !!isBusy;
}

function showThinkingMessage() {
    const chatBox = document.getElementById('chatBox');
    if (!chatBox) return;

    const messageDiv = document.createElement('div');
    messageDiv.className = 'message assistant thinking';

    const content = document.createElement('div');
    content.className = 'message-content';
    content.textContent = 'AIが考え中…';

    const dots = document.createElement('span');
    dots.className = 'thinking-dots';
    dots.setAttribute('aria-hidden', 'true');
    dots.textContent = '…';
    content.appendChild(dots);

    messageDiv.appendChild(content);
    chatBox.appendChild(messageDiv);
    chatBox.scrollTop = chatBox.scrollHeight;

    currentThinkingMessageEl = messageDiv;
}

function removeThinkingMessage() {
    if (currentThinkingMessageEl && currentThinkingMessageEl.parentNode) {
        currentThinkingMessageEl.parentNode.removeChild(currentThinkingMessageEl);
    }
    currentThinkingMessageEl = null;
}

// Assistant応答の“読みやすさ”を上げる軽い整形
// - 単一行に潰れがちな「番号付きステップ」を改行して見やすく
// - 既に整形されている場合は極力崩さない
function formatAssistantText(raw) {
    if (raw == null) return '';
    let text = String(raw).replace(/\r\n/g, '\n').trim();
    if (text.length === 0) return '';

    // 例: 「...でしょう。 1. ... 2. ...」を段落 + リストに見えるように
    text = text.replace(/([。！？])\s+(\d+)\.\s+/g, '$1\n\n$2. ');

    // 文中の連番も、前が改行でない場合は改行に寄せる
    text = text.replace(/\s+(\d+)\.\s+(?=\*\*|[^\n])/g, '\n$1. ');

    // 過剰な空行を抑制
    text = text.replace(/\n{3,}/g, '\n\n');
    return text;
}

// Enterキーで送信
document.getElementById('messageInput').addEventListener('keypress', function(event) {
    if (event.key === 'Enter' && !this.disabled) {
        sendMessage();
    }
});

// ページロード時にフォーカス
window.addEventListener('load', function() {
    document.getElementById('messageInput').focus();
});

function setInputAndSend(text) {
    const input = document.getElementById('messageInput');
    input.value = text;
    input.focus();
    autosizeInput();
    sendMessage();
}

function renderQuickPrompts() {
    const container = document.getElementById('quickPrompts');
    if (!container) return;

    container.innerHTML = '';
    for (const p of quickPrompts) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'prompt-chip';
        btn.textContent = p.label;
        btn.title = p.text;
        btn.addEventListener('click', () => setInputAndSend(p.text));
        container.appendChild(btn);
    }
}

function hideQuickPrompts() {
    const panel = document.getElementById('quickPromptsPanel');
    if (panel) panel.classList.add('is-collapsed');
}

function showQuickPrompts() {
    const panel = document.getElementById('quickPromptsPanel');
    if (panel) panel.classList.remove('is-collapsed');
}

function escapeMarkdownText(text) {
    // Light escaping to avoid unintended headings/listing in export
    return String(text ?? '').replace(/\r\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
}

function buildChatMarkdown() {
    const createdAt = new Date().toISOString();
    const lines = [];
    lines.push('# 壁打ちAIエージェント チャットログ');
    lines.push('');
    lines.push(`- exportedAt: ${createdAt}`);
    lines.push('');

    for (const item of chatLog) {
        const role = item.sender === 'user' ? 'ユーザー' : item.sender === 'assistant' ? 'アシスタント' : 'システム';
        lines.push(`## ${role}`);
        lines.push('');
        lines.push(escapeMarkdownText(item.text));
        lines.push('');
    }

    return lines.join('\n');
}

function downloadTextFile(filename, content, mimeType = 'text/plain;charset=utf-8') {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}

async function exportChat() {
    const md = buildChatMarkdown();
    const ymd = new Date().toISOString().slice(0, 10);
    const filename = `kabeuchi-chat-${ymd}.md`;

    downloadTextFile(filename, md, 'text/markdown;charset=utf-8');
    appendMessage('チャットログをMarkdownとしてエクスポートしました。', 'system-message');
}

function newChat() {
    const chatBox = document.getElementById('chatBox');
    if (!chatBox) return;

    chatBox.innerHTML = '';
    chatLog.length = 0;

    showQuickPrompts();

    appendMessage('こんにちは！何かお手伝いできることがあれば、お気軽にお話しください。', 'system-message');
}

function cancelRequest() {
    if (currentAbortController) {
        currentAbortController.abort();
    }
}

function autosizeInput() {
    const input = document.getElementById('messageInput');
    if (!input) return;
    // reset
    input.style.height = 'auto';
    // cap at ~6 lines
    const max = 6 * 20 + 24;
    input.style.height = Math.min(input.scrollHeight, max) + 'px';
}

window.addEventListener('DOMContentLoaded', () => {
    renderQuickPrompts();

    // Show app version automatically (single source of truth: backend assembly)
    updateAppVersionLabel();

    // Capture the initial system greeting (rendered in HTML) into the export log once.
    if (chatLog.length === 0) {
        const initial = document.querySelector('#chatBox .message.system-message .message-content');
        const text = initial && initial.textContent ? initial.textContent.trim() : '';
        if (text) {
            chatLog.push({ sender: 'system-message', text, createdAt: new Date().toISOString() });
        }
    }

    const exportBtn = document.getElementById('exportBtn');
    if (exportBtn) {
        exportBtn.addEventListener('click', exportChat);
    }

    const newChatBtn = document.getElementById('newChatBtn');
    if (newChatBtn) {
        newChatBtn.addEventListener('click', newChat);
    }

    const cancelBtn = document.getElementById('cancelBtn');
    if (cancelBtn) {
        cancelBtn.addEventListener('click', cancelRequest);
    }

    const input = document.getElementById('messageInput');
    if (input) {
        input.addEventListener('compositionstart', () => { isComposing = true; });
        input.addEventListener('compositionend', () => { isComposing = false; });

        input.addEventListener('input', autosizeInput);
        autosizeInput();

        input.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter') return;
            if (isComposing) return;
            if (event.shiftKey) return; // allow newline

            // Enter sends
            event.preventDefault();
            if (!input.disabled) {
                sendMessage();
            }
        });
    }

    setBusyState(false);
});

async function updateAppVersionLabel() {
    const el = document.getElementById('appVersion');
    if (!el) return;

    try {
        const res = await fetch('/api/diag', { cache: 'no-store' });
        if (!res.ok) return;
        const data = await res.json();
        const v = data && (data.appVersion ?? data.AppVersion);
        if (v) {
            el.textContent = `v${String(v).replace(/^v/i, '')}`;
        }
    } catch {
        // ignore
    }
}
