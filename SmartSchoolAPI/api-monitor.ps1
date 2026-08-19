param(
  [string]$BaseUrl = "http://127.0.0.1:5197",
  [int]$TimeoutSec = 5,
  [int]$SlowThresholdMs = 2000,
  [string]$DashboardIngestUrl = $env:SMARTSCHOOL_DASHBOARD_INGEST_URL,
  [string]$DashboardToken = $env:SMARTSCHOOL_DASHBOARD_INGEST_TOKEN
)

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $dir "api-monitor.log"
$stateFile = Join-Path $dir "api-monitor-state.json"

function Log([string]$level, [string]$msg) {
  Add-Content -Path $log -Value ("{0} [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $level, $msg) -Encoding UTF8
}

function Send-Telegram([string]$title, [string]$message) {
  $botToken = $env:SMARTSCHOOL_TELEGRAM_BOT_TOKEN
  $chatId = $env:SMARTSCHOOL_TELEGRAM_CHAT_ID
  if (-not $botToken -or -not $chatId) { return }
  try {
    $body = @{ chat_id = $chatId; text = "[$title]`n$message" } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$botToken/sendMessage" -ContentType "application/json" -Body $body -TimeoutSec 10 | Out-Null
    Log "NOTIFY" "Telegram delivery accepted"
  } catch {
    Log "NOTIFY_FAIL" "Telegram delivery failed: $($_.Exception.Message)"
  }
}

function Send-Email([string]$title, [string]$message) {
  $hostName = $env:SMARTSCHOOL_SMTP_HOST
  $to = $env:SMARTSCHOOL_SMTP_TO
  $from = $env:SMARTSCHOOL_SMTP_FROM
  if (-not $hostName -or -not $to -or -not $from) { return }
  try {
    $params = @{ SmtpServer = $hostName; To = $to; From = $from; Subject = "[$title]"; Body = $message; ErrorAction = "Stop" }
    if ($env:SMARTSCHOOL_SMTP_PORT) { $params.Port = [int]$env:SMARTSCHOOL_SMTP_PORT }
    if ($env:SMARTSCHOOL_SMTP_SSL -eq "true") { $params.UseSsl = $true }
    if ($env:SMARTSCHOOL_SMTP_USER -and $env:SMARTSCHOOL_SMTP_PASSWORD) {
      $secure = ConvertTo-SecureString $env:SMARTSCHOOL_SMTP_PASSWORD -AsPlainText -Force
      $params.Credential = New-Object System.Management.Automation.PSCredential($env:SMARTSCHOOL_SMTP_USER, $secure)
    }
    Send-MailMessage @params
    Log "NOTIFY" "Email delivery accepted"
  } catch {
    Log "NOTIFY_FAIL" "Email delivery failed: $($_.Exception.Message)"
  }
}

function Alert([string]$title, [string]$message) {
  Log "ALERT" ("$title - $message")
  try { & msg.exe * "$title`n$message" 2>$null | Out-Null } catch {}
  Send-Telegram $title $message
  Send-Email $title $message
}

function Forward-Dashboard([string]$status, [int]$httpStatus, [int]$latencyMs, [string]$note) {
  if (-not $DashboardIngestUrl -or -not $DashboardToken) { return }
  try {
    $payload = @{ serviceName = "SmartSchoolAPI"; status = $status; httpStatus = $httpStatus; latencyMs = $latencyMs; note = $note } | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri $DashboardIngestUrl -Headers @{ "x-monitor-token" = $DashboardToken } -ContentType "application/json" -Body $payload -TimeoutSec 10 | Out-Null
    Log "FORWARD" "Dashboard event forwarded"
  } catch {
    Log "FORWARD_FAIL" "Dashboard forward failed: $($_.Exception.Message)"
  }
}

$state = [pscustomobject]@{ IsHealthy = $true; ConsecutiveFailures = 0 }
if (Test-Path $stateFile) { try { $state = Get-Content $stateFile -Raw | ConvertFrom-Json } catch {} }

$httpStatus = 0
$errorMessage = ""
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
  $response = Invoke-WebRequest "$BaseUrl/" -UseBasicParsing -TimeoutSec $TimeoutSec -ErrorAction Stop
  $httpStatus = [int]$response.StatusCode
} catch { $errorMessage = $_.Exception.Message }
$stopwatch.Stop()
$latencyMs = [int]$stopwatch.ElapsedMilliseconds
$isHealthy = ($httpStatus -ge 200 -and $httpStatus -lt 300 -and $latencyMs -le $SlowThresholdMs)
$status = if ($isHealthy) { "healthy" } elseif ($httpStatus -gt 0) { "degraded" } else { "down" }
$note = if ($errorMessage) { $errorMessage } else { "status=$httpStatus latency=${latencyMs}ms" }

if ($isHealthy) {
  if (-not [bool]$state.IsHealthy) { Alert "SmartSchoolAPI RECOVERED" $note }
  Log "OK" $note
  $state.IsHealthy = $true
  $state.ConsecutiveFailures = 0
} else {
  $state.ConsecutiveFailures = [int]$state.ConsecutiveFailures + 1
  if ([bool]$state.IsHealthy -or $state.ConsecutiveFailures % 5 -eq 0) { Alert "SmartSchoolAPI ALERT" "failures=$($state.ConsecutiveFailures) detail=$note" } else { Log "FAIL" "failures=$($state.ConsecutiveFailures) detail=$note" }
  $state.IsHealthy = $false
}

if (-not ($state.PSObject.Properties.Name -contains "LastStatus")) { $state | Add-Member -NotePropertyName LastStatus -NotePropertyValue 0 }
if (-not ($state.PSObject.Properties.Name -contains "LastLatencyMs")) { $state | Add-Member -NotePropertyName LastLatencyMs -NotePropertyValue 0 }
$state.LastStatus = $httpStatus
$state.LastLatencyMs = $latencyMs
$state | ConvertTo-Json | Set-Content $stateFile -Encoding UTF8
Forward-Dashboard $status $httpStatus $latencyMs $note
