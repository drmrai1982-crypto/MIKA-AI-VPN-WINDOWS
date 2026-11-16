$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = Join-Path $root "MIKA-AI-VPN.exe"
if (!(Test-Path $app)) { throw "MIKA-AI-VPN.exe was not found beside this script." }

# Install the x64 MSVC runtime only if the native FCAE DLL dependencies are absent.
$system32 = Join-Path $env:WINDIR "System32"
$runtimeFiles = @("MSVCP140.dll", "VCRUNTIME140.dll")
$missing = $runtimeFiles | Where-Object { !(Test-Path (Join-Path $system32 $_)) }
if ($missing.Count -gt 0) {
    Write-Host "پیش‌نیاز Microsoft Visual C++ نصب نیست؛ دریافت نسخه رسمی x64..." -ForegroundColor Yellow
    $installer = Join-Path $env:TEMP "MIKA-AI-VPN-vc_redist.x64.exe"
    Invoke-WebRequest -Uri "https://aka.ms/vc14/vc_redist.x64.exe" -OutFile $installer
    $signature = Get-AuthenticodeSignature -FilePath $installer
    if ($signature.Status -ne "Valid" -or $signature.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
        Remove-Item $installer -Force -ErrorAction SilentlyContinue
        throw "امضای دیجیتال نصب‌کننده Microsoft معتبر نیست؛ نصب متوقف شد."
    }
    $process = Start-Process -FilePath $installer -ArgumentList "/install", "/quiet", "/norestart" -Verb RunAs -Wait -PassThru
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
    if ($process.ExitCode -notin @(0, 1638, 3010)) {
        throw "نصب Microsoft Visual C++ Redistributable ناموفق بود. کد: $($process.ExitCode)"
    }
}

$stillMissing = $runtimeFiles | Where-Object { !(Test-Path (Join-Path $system32 $_)) }
if ($stillMissing.Count -gt 0) {
    throw "پیش‌نیاز C++ کامل نصب نشده است: $($stillMissing -join ', ')"
}
Start-Process -FilePath $app -WorkingDirectory $root
