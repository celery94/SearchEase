const { app, BrowserWindow } = require('electron/main')
const path = require('node:path')
const { spawn } = require('child_process')
const net = require('net')

let serverProcess = null

function waitForPort(port) {
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
            reject(new Error('Timeout waiting for port'))
        }, 30000) // 30 seconds timeout

        function checkPort() {
            const client = new net.Socket()

            client.on('connect', () => {
                clearTimeout(timeout)
                client.destroy()
                resolve()
            })

            client.on('error', () => {
                client.destroy()
                setTimeout(checkPort, 100) // Try again after 100ms
            })

            client.connect(port, 'localhost')
        }

        checkPort()
    })
}

function startServer() {
    let currentBinPath = path.join(__dirname, './bin');

    serverProcess = spawn("SearchEase.Server.exe", [], {
        cwd: currentBinPath,
    })

    serverProcess.stdout.on('data', (data) => {
        console.log(`Server output: ${data}`)
    })

    serverProcess.stderr.on('data', (data) => {
        console.error(`Server error: ${data}`)
    })
}

async function createWindow() {
    const win = new BrowserWindow({
        width: 800,
        height: 600,
        webPreferences: {
            preload: path.join(__dirname, 'preload.js')
        },
        autoHideMenuBar: true,
        menuBarVisible: false
    })

    try {
        await waitForPort(5000)
        win.loadURL('http://localhost:5000')
    } catch (error) {
        console.error('Failed to connect to server:', error)
    }
}

app.whenReady().then(() => {
    startServer()
    createWindow()

    app.on('activate', () => {
        if (BrowserWindow.getAllWindows().length === 0) {
            createWindow()
        }
    })
})

app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
        if (serverProcess) {
            serverProcess.kill()
        }
        app.quit()
    }
})

app.on('before-quit', () => {
    if (serverProcess) {
        serverProcess.kill()
    }
})