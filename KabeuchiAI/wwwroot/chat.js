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
        appendMessage(data.response, 'assistant');
    } catch (error) {
        appendMessage(`エラーが発生しました: ${error.message}`, 'assistant');
    } finally {
        input.disabled = false;
        input.focus();
    }
}

// メッセージを追加
function appendMessage(text, sender) {
    const chatBox = document.getElementById('chatBox');
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${sender}`;

    const p = document.createElement('p');
    p.textContent = text;

    messageDiv.appendChild(p);
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
