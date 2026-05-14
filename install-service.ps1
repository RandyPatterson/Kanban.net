#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install, start, stop, or uninstall the KanbanBoard Windows Service.

.PARAMETER Action
    One of: publish, install, start, stop, uninstall, reinstall

.EXAMPLE
    .\install-service.ps1 -Action publish    # Build publish output
    .\install-service.ps1 -Action install    # Publish + install service
    .\install-service.ps1 -Action start      # Start the service
    .\install-service.ps1 -Action stop       # Stop the service
    .\install-service.ps1 -Action uninstall  # Stop + remove the service
    .\install-service.ps1 -Action reinstall  # Uninstall + install
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet("publish", "install", "start", "stop", "uninstall", "reinstall")]
    [string]$Action
)

$ServiceName   = "KanbanBoard"
$DisplayName   = "Kanban Board"
$Description   = "ASP.NET Core Kanban Board web application"
$PublishDir    = Join-Path $PSScriptRoot "publish"
$ExePath       = Join-Path $PublishDir "kanban.net.exe"

function Invoke-Publish {
    Write-Host "Publishing application to $PublishDir ..." -ForegroundColor Cyan
    dotnet publish "$PSScriptRoot\kanban.net.csproj" `
        --configuration Release `
        --output $PublishDir `
        --self-contained false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
    Write-Host "Published successfully." -ForegroundColor Green
}

function Install-KanbanService {
    Invoke-Publish

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Service '$ServiceName' already exists. Use -Action reinstall to replace it." -ForegroundColor Yellow
        return
    }

    Write-Host "Creating Windows Service '$ServiceName' ..." -ForegroundColor Cyan
    New-Service -Name $ServiceName `
        -BinaryPathName $ExePath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic

    Write-Host "Service installed. Run with -Action start to start it." -ForegroundColor Green
}

function Start-KanbanService {
    Write-Host "Starting service '$ServiceName' ..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Write-Host "Service started. Browse to http://localhost:5100" -ForegroundColor Green
}

function Stop-KanbanService {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Write-Host "Stopping service '$ServiceName' ..." -ForegroundColor Cyan
        Stop-Service -Name $ServiceName -Force
        Write-Host "Service stopped." -ForegroundColor Green
    } else {
        Write-Host "Service is not running." -ForegroundColor Yellow
    }
}

function Uninstall-KanbanService {
    Stop-KanbanService
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Removing service '$ServiceName' ..." -ForegroundColor Cyan
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service removed." -ForegroundColor Green
    } else {
        Write-Host "Service '$ServiceName' not found." -ForegroundColor Yellow
    }
}

switch ($Action) {
    "publish"   { Invoke-Publish }
    "install"   { Install-KanbanService }
    "start"     { Start-KanbanService }
    "stop"      { Stop-KanbanService }
    "uninstall" { Uninstall-KanbanService }
    "reinstall" {
        Uninstall-KanbanService
        Install-KanbanService
    }
}
