# ACS Debug Panel - Message Send Operation Testing

## Overview
The ACS Debug Panel now shows comprehensive verbose information about all Azure Communication Services operations, including detailed step-by-step tracking of chat message sending operations.

## Testing the Message Send Operation Tracking

### 1. Navigate to the Chat Page
- Go to `/chat` in the application
- If not authenticated, log in using one of the available authentication methods

### 2. Create or Select a Chat Thread
- Either select an existing thread from the sidebar
- Or create a new thread using the "➕ New Thread" button

### 3. Access the ACS Debug Panel
- Scroll down to see the **"🧭 ACS Debug Info"** panel at the bottom
- Expand it by clicking the triangle (►) if collapsed
- Look for the **"🔍 Operation History"** section and expand it

### 4. Send a Message to Trigger Operation Tracking
You can send a message in two ways:

#### Option A: Regular Message
- Type a message in the input field at the bottom of the chat
- Press Enter or click the "📤 Send" button

#### Option B: Test Button (Live Mode Only)
- Click the "🧪 Test ACS" button next to the Refresh button
- This sends a timestamped test message and demonstrates the full operation tracking

### 5. View Detailed Operation History
In the Operation History section, you should see a new **"📤 MessageSend"** operation with:

#### Operation Overview:
- **Operation Type**: MessageSend with 📤 icon
- **Description**: "Send message to thread {threadId}"
- **Status**: Success ✓ or Failed ✗ with color coding
- **Duration**: Time taken in milliseconds
- **User Info**: Shows which user (tenant) sent the message

#### Detailed Steps (click "Show Steps"):
1. **ValidateThread**: Confirms thread exists in local storage
2. **ValidateToken**: Checks if user has valid ACS access token
3. **EnsureToken**: If token missing, automatically exchanges Entra ID token for ACS token
4. **CreateChatClient**: Creates Azure Communication Services chat client
5. **SendToAcs**: Actually sends message to ACS service
6. **StoreLocally**: Stores message in local cache for UI display
7. **CrossTenantMessage**: (If applicable) Special step for cross-tenant scenarios

#### Each Step Shows:
- ✓ Success or ✗ Failure indicator
- Step description
- Timestamp
- Metadata (token lengths, IDs, etc.)
- Error messages for failed steps

### 6. Cross-Tenant Testing
If testing with a Fabrikam user (cross-tenant scenario):
- The operation will include a **"CrossTenantMessage"** step
- Special 🌐 indicators will appear
- Metadata will show source tenant (Fabrikam) and target tenant (Contoso)

## Real-Time Updates
- The Operation History auto-refreshes every 2 seconds when expanded
- Use the 🔄 Refresh button to manually update
- Use the 🧹 Clear History button to remove all tracked operations

## Troubleshooting Information
Failed operations will show:
- Red color coding and ✗ indicators
- Detailed error messages
- Exception types and details
- Context information for debugging

## What You Should See
A successful message send operation typically shows:
```
📤 MessageSend
Send message to thread abc123...
User: user-id (Contoso)
✓ Success | Duration: 234ms

Steps (7):
✓ ValidateThread - Thread found, proceeding with message send
✓ ValidateToken - ACS access token validation successful  
✓ CreateChatClient - Successfully created ACS chat client
✓ SendToAcs - Message successfully sent to ACS
✓ StoreLocally - Message stored in local cache
✓ CrossTenantMessage - (If cross-tenant) Cross-tenant message sent
```

This provides complete visibility into every step of the Azure Communication Services message sending process!