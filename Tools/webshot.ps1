# Screenshot a page in headless Chrome after a delay, via the DevTools protocol.
# usage: powershell -File Tools/webshot.ps1 <url> <seconds> <out.png>
param([string]$Url, [int]$Seconds = 40, [string]$Out = "webshot.png")
$profileDir = Join-Path $env:TEMP "salvo_webshot_profile"
Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
$chrome = Start-Process -FilePath "C:\Program Files\Google\Chrome\Application\chrome.exe" -PassThru -ArgumentList @(
    "--headless=new", "--user-data-dir=$profileDir", "--use-angle=d3d11", "--enable-gpu", "--ignore-gpu-blocklist",
    "--remote-debugging-port=9333", "--window-size=1280,720", "--autoplay-policy=no-user-gesture-required", $Url)
try {
    Start-Sleep -Seconds $Seconds
    $targets = Invoke-RestMethod -Uri "http://127.0.0.1:9333/json"
    $page = $targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $ws.Options.KeepAliveInterval = [TimeSpan]::FromSeconds(30)
    $ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
    $msg = [Text.Encoding]::UTF8.GetBytes('{"id":1,"method":"Page.captureScreenshot","params":{"format":"png"}}')
    $ws.SendAsync([ArraySegment[byte]]$msg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    $buf = New-Object byte[] 1048576
    $sb = New-Object Text.StringBuilder
    do {
        $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result
        [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
    } while (-not $r.EndOfMessage)
    $json = $sb.ToString() | ConvertFrom-Json
    [IO.File]::WriteAllBytes($Out, [Convert]::FromBase64String($json.result.data))
    Write-Output "saved $Out"
}
finally {
    Stop-Process -Id $chrome.Id -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -match "salvo_webshot_profile" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
