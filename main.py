import webview
import threading
from app import app
import sys
import time
import socket

def is_port_in_use(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        return s.connect_ex(('127.0.0.1', port)) == 0

def start_server(port):
    app.run(host='127.0.0.1', port=port, debug=False, use_reloader=False)

if __name__ == '__main__':
    port = 5000
    # Find an open port just in case 5000 is in use
    while is_port_in_use(port):
        port += 1
        
    server_thread = threading.Thread(target=start_server, args=(port,), daemon=True)
    server_thread.start()

    # Give Flask a moment to start
    time.sleep(1)

    window = webview.create_window(
        'Switch Handover Generator', 
        f'http://127.0.0.1:{port}',
        width=1200, 
        height=800,
        min_size=(800, 600)
    )
    
    webview.start()
    sys.exit()
