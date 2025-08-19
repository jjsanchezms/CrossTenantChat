#!/bin/bash

# Cross-Tenant Chat Demo - Setup Script
echo "🌐 Cross-Tenant Chat Demo Setup"
echo "=================================="

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET is not installed. Please install .NET 9.0 SDK"
    exit 1
fi

echo "✅ .NET found: $(dotnet --version)"

# Build the project
echo "🔨 Building project..."
dotnet build --configuration Release

if [ $? -eq 0 ]; then
    echo "✅ Build successful!"
else
    echo "❌ Build failed!"
    exit 1
fi

# Create deployment package
echo "📦 Creating deployment package..."
dotnet publish --configuration Release --output ./publish

if [ $? -eq 0 ]; then
    echo "✅ Deployment package created in ./publish/"
else
    echo "❌ Deployment package creation failed!"
    exit 1
fi

echo ""
echo "🎉 Setup complete!"
echo ""
echo "Next steps:"
echo "1. Update appsettings.Production.json with your Azure configuration"
echo "2. Run: dotnet ./publish/CrossTenantChat.dll"
echo "3. Navigate to http://localhost:5068/chat"
echo ""
echo "For more details, see README.md"
