// =========================================================
// 1. LOGIC HIỂN THỊ DỮ LIỆU (RENDERER)
// File connection.js sẽ gọi các hàm này khi nhận dữ liệu từ Server
// =========================================================

// Hiển thị danh sách Process/App
window.renderProcessList = function(json) {
    const list = JSON.parse(json);
    const contentArea = document.getElementById('content-area');
    if (!contentArea) return;

    if (list.length === 0) {
        contentArea.innerHTML = '<p class="text-gray-400 mt-10">Không tìm thấy dữ liệu.</p>';
        return;
    }

    let html = `
        <div class="overflow-x-auto">
            <table class="w-full text-left text-sm text-gray-300 border-collapse">
                <thead class="bg-gray-700 text-gray-100 sticky top-0">
                    <tr>
                        <th class="p-3">ID</th>
                        <th class="p-3">Name</th>
                        <th class="p-3">Title</th>
                        <th class="p-3">RAM (MB)</th>
                        <th class="p-3 text-center">Action</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-gray-700">`;

    list.forEach(p => {
        const ram = (p.memoryUsage / 1024 / 1024).toFixed(1);
        html += `
            <tr class="hover:bg-gray-700/50 transition">
                <td class="p-3 font-mono text-xs">${p.id}</td>
                <td class="p-3 font-bold text-green-400">${p.name}</td>
                <td class="p-3 text-xs text-gray-400 truncate max-w-[200px]" title="${p.title}">${p.title || '-'}</td>
                <td class="p-3 text-xs">${ram}</td>
                <td class="p-3 text-center">
                    <button class="text-red-400 hover:text-red-200 hover:bg-red-900/30 px-2 py-1 rounded transition" 
                            onclick="window.appActions.killProcess(${p.id})">
                        <i class="fas fa-times"></i> Kill
                    </button>
                </td>
            </tr>`;
    });

    html += `</tbody></table></div>`;
    contentArea.innerHTML = html;
};

// Hiển thị Hình ảnh (Screenshot / Webcam)
window.renderImage = function(type, base64Data) {
    const contentArea = document.getElementById('content-area');
    if (!contentArea) return;

    const imgTag = `<img src="data:image/jpeg;base64,${base64Data}" class="max-w-full h-auto max-h-[500px] border border-gray-600 rounded shadow-lg mx-auto">`;
    
    // Nếu là Webcam Frame thì cập nhật liên tục, không xóa khung cũ để đỡ nháy
    if (type === 'WEBCAM_FRAME') {
        const feed = document.getElementById('webcam-feed');
        if (feed) {
            feed.src = `data:image/jpeg;base64,${base64Data}`;
        } else {
            contentArea.innerHTML = `<div class="flex flex-col items-center">
                <h3 class="text-green-400 font-bold mb-2">🔴 Live Webcam</h3>
                <img id="webcam-feed" src="data:image/jpeg;base64,${base64Data}" class="max-w-full h-auto border rounded shadow-lg">
                <button onclick="window.appActions.closeWebcam()" class="mt-4 bg-red-600 text-white px-4 py-2 rounded">Tắt Webcam</button>
            </div>`;
        }
    } else {
        // Screenshot
        contentArea.innerHTML = `<div class="flex flex-col items-center">
            <h3 class="text-blue-400 font-bold mb-2">📸 Screenshot</h3>
            ${imgTag}
        </div>`;
    }
};

// Hiển thị Keylogger
window.renderKeyLog = function(key) {
    const contentArea = document.getElementById('content-area');
    if (!contentArea) return;

    // Nếu chưa có khung log thì tạo mới
    let logBox = document.getElementById('keylog-box');
    if (!logBox) {
        contentArea.innerHTML = `
            <div class="flex flex-col h-full text-left">
                <div class="flex justify-between items-center mb-2">
                    <h3 class="text-yellow-400 font-bold">⌨ Keylogger Live</h3>
                    <button onclick="window.appActions.stopKeylog()" class="text-xs bg-red-600 px-2 py-1 rounded">Dừng</button>
                </div>
                <div id="keylog-box" class="flex-1 bg-black font-mono text-green-500 p-4 rounded border border-gray-700 overflow-auto whitespace-pre-wrap break-all shadow-inner text-sm"></div>
            </div>`;
        logBox = document.getElementById('keylog-box');
    }

    // Thêm ký tự mới vào
    // Xử lý các phím đặc biệt để hiển thị đẹp hơn
    if (key === '\n') key = '\n'; 
    if (key === '[BACK]') { 
        logBox.textContent = logBox.textContent.slice(0, -1); 
        return; 
    }

    logBox.textContent += key;
    logBox.scrollTop = logBox.scrollHeight; // Tự cuộn xuống dưới
};


// =========================================================
// 2. LOGIC CHATBOX & NGƯỜI TUYẾT
// =========================================================

// Gán trực tiếp vào window để HTML gọi được onclick="toggleChat()"
window.toggleChat = function(forceOpen = undefined) {
    const chatWindow = document.getElementById('chat-window');
    
    if (!chatWindow) return;

    // Kiểm tra trạng thái hiện tại
    const currentStyle = window.getComputedStyle(chatWindow);
    const isHidden = (currentStyle.display === 'none');

    let shouldOpen = forceOpen !== undefined ? forceOpen : isHidden;

    if (shouldOpen) {
        chatWindow.style.display = 'flex';
        // Ẩn bong bóng chào nếu đang mở chat
        const bubble = document.getElementById("snowmanBubble");
        if(bubble) bubble.style.display = 'none';

        // Focus vào ô nhập
        setTimeout(() => {
            const input = document.getElementById('chat-input');
            if (input) input.focus();
        }, 100);
    } else {
        chatWindow.style.display = 'none';
        const bubble = document.getElementById("snowmanBubble");
        if(bubble) bubble.style.display = 'block';
    }
};

// Hàm thêm tin nhắn vào khung chat
function addChatMessageToUI(message, sender) {
    const chatBody = document.getElementById('chat-messages');
    if (!chatBody) return;

    const msgDiv = document.createElement('div');
    msgDiv.className = sender === 'user' ? 'msg-user' : 'msg-bot';
    msgDiv.textContent = message;
    
    chatBody.appendChild(msgDiv);
    chatBody.scrollTop = chatBody.scrollHeight;

    // Nếu là bot trả lời, tự động mở khung chat
    if (sender === 'bot') {
        window.toggleChat(true);
    }
}

// Hàm gửi tin nhắn từ UI
window.sendChatUI = function() {
    const input = document.getElementById('chat-input');
    if (!input) return;
    
    const msg = input.value.trim();
    if (msg) {
        addChatMessageToUI(msg, 'user'); // Hiện tin nhắn người dùng
        
        // Gửi lên server
        if (window.appActions && window.appActions.sendChat) {
            window.appActions.sendChat(msg);
        } else {
            setTimeout(() => addChatMessageToUI("Chưa kết nối Server!", "bot"), 500);
        }
        
        input.value = '';
    }
}

// Bắt sự kiện Enter trong ô chat
const chatInput = document.getElementById('chat-input');
if(chatInput) {
    chatInput.addEventListener('keypress', (e) => {
        if(e.key === 'Enter') window.sendChatUI();
    });
}

// Expose object `ui` để connection.js gọi lại khi nhận tin nhắn
window.ui = {
    addChatMessage: addChatMessageToUI,
    showTyping: (show) => { /* Placeholder */ }
};


// =========================================================
// 3. LOGIC HIỆU ỨNG (MỞ CỬA & TUYẾT)
// =========================================================

// Hàm xử lý mở cửa (Transition) - Được gọi khi Login thành công
window.openDoorAndGo = function() {
    const bell = document.getElementById("bellSound");
    if(bell) { bell.currentTime = 0; bell.play().catch(()=>{}); }
    
    document.body.classList.add("door-open");
    document.body.classList.add("in-dashboard");

    setTimeout(() => {
        const auth = document.getElementById("auth-screen");
        const main = document.getElementById("main-screen");
        const snowContainer = document.getElementById("snowman-container");

        // Ẩn Login, Hiện Dashboard
        if(auth) auth.style.display = 'none';
        if(main) main.classList.add("active");
        
        // Hiện Người Tuyết
        if(snowContainer) {
            snowContainer.style.display = "flex";
            snowContainer.style.zIndex = "99999"; 
        }
        
        addChatMessageToUI("Ho ho ho! Chào mừng bạn vào hệ thống! 🎅", "bot");
    }, 800);
}

// Khởi tạo ngay khi load
document.addEventListener("DOMContentLoaded", () => {
    // Đảm bảo container người tuyết hiện ngay lập tức (trên màn hình Login)
    const snowman = document.getElementById("snowman-container");
    if (snowman) {
        snowman.style.display = "flex";
        snowman.style.zIndex = "99999"; 
    }
    
    // Hiệu ứng Tuyết rơi
    (function createSnow() {
        const numFlakes = 30;
        const w = window.innerWidth;
        const h = window.innerHeight;
        
        for (let i = 0; i < numFlakes; i++) {
            const img = document.createElement("img");
            img.src = "image/snow.png"; 
            img.className = "snowflake";
            img.style.width = (Math.random() * 25 + 10) + "px";
            img.style.position = 'fixed';
            img.style.top = Math.random() * h + "px";
            img.style.left = Math.random() * w + "px";
            img.style.opacity = (Math.random() * 0.7 + 0.3).toString();
            img.style.pointerEvents = 'none';
            img.style.zIndex = '1';
            
            const duration = Math.random() * 5 + 5;
            img.style.animation = `snow-spin ${duration}s linear infinite`;
            
            document.body.appendChild(img);
        }
        
        if (!document.getElementById('snow-keyframes')) {
            const style = document.createElement('style');
            style.id = 'snow-keyframes';
            style.innerHTML = `@keyframes snow-spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`;
            document.head.appendChild(style);
        }
    })();
});