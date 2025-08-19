# Cross-Tenant Chat Demo - Setup Script
Write-Host "🌐 Cross-Tenant Chat Demo Setup" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

# Check if .NET is installed
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "❌ .NET is not installed. Please install .NET 9.0 SDK" -ForegroundColor Red
    exit 1
}

# Build the project
Write-Host "🔨 Building project..." -ForegroundColor Yellow
dotnet build --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Build successful!" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Create deployment package
Write-Host "📦 Creating deployment package..." -ForegroundColor Yellow
dotnet publish --configuration Release --output ./publish

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Deployment package created in ./publish/" -ForegroundColor Green
} else {
    Write-Host "❌ Deployment package creation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🎉 Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Update appsettings.Production.json with your Azure configuration"
Write-Host "2. Run: dotnet ./publish/CrossTenantChat.dll"
Write-Host "3. Navigate to http://localhost:5068/chat"
Write-Host ""
Write-Host "For more details, see README.md"
