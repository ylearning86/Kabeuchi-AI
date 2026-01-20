const chatLog = [];

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
    const message = input.value.trim();

    if (message === '') return;

    // ユーザーメッセージを表示
    appendMessage(message, 'user');
    input.value = '';
    input.disabled = true;

    try {
        // バックエンドAPIに送信
        const response = await fetch('/api/chat', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ message: message })
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();
        // Backward/forward compatibility: accept either { response } or { Response }
        const responseText = (data && (data.response ?? data.Response)) ?? '';
        const meta = data && (data.meta ?? data.Meta);
        appendMessage(responseText, 'assistant', meta);
    } catch (error) {
        appendMessage(`エラーが発生しました: ${error.message}`, 'assistant');
    } finally {
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

    // Prefer clipboard if available; otherwise download
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(md);
            appendMessage('チャットログをクリップボードにコピーしました（Markdown）。必要ならそのまま貼り付けてください。', 'system-message');
            return;
        } catch {
            // fall through to download
        }
    }

    downloadTextFile(filename, md, 'text/markdown;charset=utf-8');
    appendMessage('チャットログをMarkdownとしてエクスポートしました。', 'system-message');
}

function clearChat() {
    const chatBox = document.getElementById('chatBox');
    if (!chatBox) return;

    chatBox.innerHTML = '';
    chatLog.length = 0;

    appendMessage('こんにちは！何かお手伝いできることがあれば、お気軽にお話しください。', 'system-message');
}

window.addEventListener('DOMContentLoaded', () => {
    renderQuickPrompts();

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

    const clearBtn = document.getElementById('clearBtn');
    if (clearBtn) {
        clearBtn.addEventListener('click', clearChat);
    }
});
