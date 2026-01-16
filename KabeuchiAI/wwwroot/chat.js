// メッセージ送信機能
function sendMessage() {
    const input = document.getElementById('messageInput');
    const message = input.value.trim();

    if (message === '') return;

    // ユーザーメッセージを表示
    appendMessage(message, 'user');
    input.value = '';

    // エコーバック（壁打ち用）
    setTimeout(() => {
        appendMessage(`あなたのメッセージ: "${message}"\n\nこれについて、もっと詳しく教えてください。`, 'assistant');
    }, 500);
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
    if (event.key === 'Enter') {
        sendMessage();
    }
});

// ページロード時にフォーカス
window.addEventListener('load', function() {
    document.getElementById('messageInput').focus();
});
