mergeInto(LibraryManager.library, {
  SnakeSocketOpen: function (objectNamePtr, urlPtr) {
    var objectName = UTF8ToString(objectNamePtr);
    var url = UTF8ToString(urlPtr);
    var emit = function (type, data) {
      SendMessage(objectName, 'OnSocketEvent', JSON.stringify({ type: type, data: data || '' }));
    };
    try {
      if (Module.snakeRoomSocket) Module.snakeRoomSocket.close();
      var ws = new WebSocket(url);
      Module.snakeRoomSocket = ws;
      ws.onopen = function () { emit('open', ''); };
      ws.onmessage = function (event) { emit('message', String(event.data)); };
      ws.onerror = function () { emit('error', '连接失败，请检查服务地址和网络'); };
      ws.onclose = function () { emit('close', '连接已关闭'); };
    } catch (error) { emit('error', String(error)); }
  },
  SnakeSocketSend: function (messagePtr) {
    var ws = Module.snakeRoomSocket;
    if (ws && ws.readyState === 1) ws.send(UTF8ToString(messagePtr));
  },
  SnakeSocketClose: function () {
    var ws = Module.snakeRoomSocket;
    Module.snakeRoomSocket = null;
    if (ws) { ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null; ws.close(); }
  }
});
