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

    const p = document.createElement('p');
    p.textContent = text;

    messageDiv.appendChild(p);

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
