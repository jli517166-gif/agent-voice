$address = 'http://127.0.0.1:7860'
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync('127.0.0.1', 7860)
        if ($task.Wait(1000) -and $client.Connected) { Start-Process $address; exit 0 }
    } catch {} finally { $client.Dispose() }
    Start-Sleep -Seconds 1
}
