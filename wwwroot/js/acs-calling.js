// Azure Communication Services Calling SDK integration
// This module provides direct calling functionality without requiring the UI library

export const acsCall = (() => {
    let callAgent = null;
    let currentCall = null;
    let deviceManager = null;
    let localVideoStream = null;
    let callStateChangedHandler = null;

    // Get available globals from the ACS SDK
    function getACSGlobals() {
        // Check various possible global names for ACS Common
        const commonCandidates = [
            'AzureCommuncation', // Note: there's a typo in some CDN versions
            'AzureCommuncationSDK',
            'AzureCommunication',
            'AzureCommunicationCommon',
            'Azure.Communication'
        ];

        // Check for ACS Calling SDK
        const callingCandidates = [
            'AzureCommunicationCalling',
            'AzureCommuncationCalling', // Note: typo variant
            'ACS.Calling',
            'Azure.Communication.Calling'
        ];

        let common = null;
        let calling = null;

        // Find the common library
        for (const name of commonCandidates) {
            const lib = getNestedProperty(window, name);
            if (lib && lib.AzureCommunicationTokenCredential) {
                common = lib;
                console.log(`Found ACS Common library: ${name}`);
                break;
            }
        }

        // Find the calling library
        for (const name of callingCandidates) {
            const lib = getNestedProperty(window, name);
            if (lib && lib.CallClient) {
                calling = lib;
                console.log(`Found ACS Calling library: ${name}`);
                break;
            }
        }

        if (!common) {
            // Try to find it in the global scope directly
            if (window.AzureCommunicationTokenCredential) {
                common = window;
                console.log('Found ACS Common in global scope');
            }
        }

        if (!calling) {
            // Try to find it in the global scope directly
            if (window.CallClient) {
                calling = window;
                console.log('Found ACS Calling in global scope');
            }
        }

        return { common, calling };
    }

    // Helper to get nested properties from objects
    function getNestedProperty(obj, path) {
        return path.split('.').reduce((current, prop) => current && current[prop], obj);
    }

    // Initialize the calling client
    async function initialize(token, userId) {
        const { common, calling } = getACSGlobals();

        if (!common || !calling) {
            throw new Error('Azure Communication Services libraries not loaded. Please ensure the CDN scripts are included.');
        }

        const { AzureCommunicationTokenCredential } = common;
        const { CallClient, VideoDeviceInfo, LocalVideoStream } = calling;

        console.log('Initializing ACS calling with token and userId:', { tokenLength: token?.length, userId });

        // Create call client
        const callClient = new CallClient();
        
        // Create token credential
        const tokenCredential = new AzureCommunicationTokenCredential(token);

        // Create call agent
        callAgent = await callClient.createCallAgent(tokenCredential, {
            displayName: 'ACS User'
        });

        // Get device manager for camera/microphone access
        deviceManager = await callClient.getDeviceManager();

        console.log('ACS calling initialized successfully');
        return { callAgent, deviceManager };
    }

    // Generate a new Group ID for the call
    function generateGroupId() {
        // Generate a GUID-like string for the group call
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0;
            const v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // Start a new call
    async function startCall(options) {
        if (!callAgent) {
            throw new Error('Call agent not initialized');
        }

        const { displayName, video, audio, callType } = options;
        
        // Update call agent display name
        callAgent.displayName = displayName || 'ACS User';

        // Generate a new group ID for this call
        const groupId = generateGroupId();
        
        console.log('Starting new call:', { groupId, displayName, video, audio, callType });

        // Prepare call options
        const callOptions = {
            videoOptions: video ? await getVideoOptions() : undefined,
            audioOptions: { muted: !audio }
        };

        // Start the group call with the new group ID
        const groupCallLocator = { groupId };
        currentCall = callAgent.startCall([], groupCallLocator, callOptions);

        // Set up call event handlers
        setupCallEventHandlers(currentCall);

        // Render local video if enabled
        if (video && localVideoStream) {
            await renderLocalVideo();
        }

        return {
            callId: groupId,
            call: currentCall,
            localVideo: video,
            audio: audio
        };
    }

    // Get video options for the call
    async function getVideoOptions() {
        if (!deviceManager) {
            throw new Error('Device manager not initialized');
        }

        const cameras = await deviceManager.getCameras();
        if (cameras.length > 0) {
            localVideoStream = new (getACSGlobals().calling.LocalVideoStream)(cameras[0]);
            return {
                localVideoStreams: [localVideoStream]
            };
        }
        return undefined;
    }

    // Set up event handlers for the call
    function setupCallEventHandlers(call) {
        // Handle call state changes
        callStateChangedHandler = () => {
            console.log('Call state changed:', call.state);
            
            // Trigger custom events that Blazor can listen to
            const event = new CustomEvent('acsCallStateChanged', {
                detail: {
                    state: call.state,
                    callId: call.id,
                    participants: call.remoteParticipants.length
                }
            });
            window.dispatchEvent(event);
        };

        call.on('stateChanged', callStateChangedHandler);

        // Handle remote participants
        call.on('remoteParticipantsUpdated', (e) => {
            console.log('Remote participants updated:', e);
            
            e.added.forEach(participant => {
                console.log('Participant joined:', participant.displayName);
                subscribeToParticipant(participant);
            });

            e.removed.forEach(participant => {
                console.log('Participant left:', participant.displayName);
            });

            // Trigger custom event
            const event = new CustomEvent('acsParticipantsUpdated', {
                detail: {
                    participantCount: call.remoteParticipants.length,
                    participants: call.remoteParticipants.map(p => ({
                        id: p.identifier.communicationUserId,
                        displayName: p.displayName
                    }))
                }
            });
            window.dispatchEvent(event);
        });
    }

    // Subscribe to a remote participant's video streams
    function subscribeToParticipant(participant) {
        participant.on('videoStreamsUpdated', (e) => {
            e.added.forEach(stream => {
                renderRemoteVideo(stream, participant);
            });
        });

        // Subscribe to existing video streams
        participant.videoStreams.forEach(stream => {
            renderRemoteVideo(stream, participant);
        });
    }

    // Render local video
    async function renderLocalVideo() {
        if (!localVideoStream) return;

        const videoGrid = document.getElementById('video-grid');
        if (!videoGrid) return;

        // Create video container
        const videoContainer = document.createElement('div');
        videoContainer.className = 'video-container local-video';
        videoContainer.id = 'local-video-container';

        // Create video element
        const videoElement = document.createElement('video');
        videoElement.id = 'local-video';
        videoElement.autoplay = true;
        videoElement.muted = true;
        videoElement.playsInline = true;

        // Create label
        const label = document.createElement('div');
        label.className = 'video-label';
        label.textContent = 'You';

        videoContainer.appendChild(videoElement);
        videoContainer.appendChild(label);
        videoGrid.appendChild(videoContainer);

        // Render the video stream
        const renderer = new (getACSGlobals().calling.VideoStreamRenderer)(localVideoStream);
        const view = await renderer.createView();
        videoElement.srcObject = view.mediaStream;
    }

    // Render remote participant video
    async function renderRemoteVideo(stream, participant) {
        const videoGrid = document.getElementById('video-grid');
        if (!videoGrid) return;

        const participantId = participant.identifier.communicationUserId;

        // Create video container
        const videoContainer = document.createElement('div');
        videoContainer.className = 'video-container remote-video';
        videoContainer.id = `remote-video-${participantId}`;

        // Create video element
        const videoElement = document.createElement('video');
        videoElement.autoplay = true;
        videoElement.playsInline = true;

        // Create label
        const label = document.createElement('div');
        label.className = 'video-label';
        label.textContent = participant.displayName || 'Remote User';

        videoContainer.appendChild(videoElement);
        videoContainer.appendChild(label);
        videoGrid.appendChild(videoContainer);

        // Render the video stream
        const { calling } = getACSGlobals();
        const renderer = new calling.VideoStreamRenderer(stream);
        const view = await renderer.createView();
        videoElement.srcObject = view.mediaStream;
    }

    // Toggle audio mute/unmute
    async function toggleAudio(enabled) {
        if (!currentCall) return;

        try {
            if (enabled) {
                await currentCall.unmute();
            } else {
                await currentCall.mute();
            }
            console.log('Audio toggled:', enabled ? 'unmuted' : 'muted');
        } catch (error) {
            console.error('Error toggling audio:', error);
        }
    }

    // Toggle video on/off
    async function toggleVideo(enabled) {
        if (!currentCall) return;

        try {
            if (enabled) {
                // Start video
                if (!localVideoStream) {
                    const videoOptions = await getVideoOptions();
                    if (videoOptions) {
                        await currentCall.startVideo(videoOptions.localVideoStreams[0]);
                        await renderLocalVideo();
                    }
                } else {
                    await currentCall.startVideo(localVideoStream);
                }
            } else {
                // Stop video
                if (localVideoStream) {
                    await currentCall.stopVideo(localVideoStream);
                    
                    // Remove local video element
                    const localVideoContainer = document.getElementById('local-video-container');
                    if (localVideoContainer) {
                        localVideoContainer.remove();
                    }
                }
            }
            console.log('Video toggled:', enabled ? 'on' : 'off');
        } catch (error) {
            console.error('Error toggling video:', error);
        }
    }

    // End the current call
    async function endCall() {
        if (!currentCall) return;

        try {
            await currentCall.hangUp();
            currentCall = null;
            localVideoStream = null;

            // Clear video grid
            const videoGrid = document.getElementById('video-grid');
            if (videoGrid) {
                videoGrid.innerHTML = '';
            }

            console.log('Call ended successfully');
        } catch (error) {
            console.error('Error ending call:', error);
        }
    }

    // Get calling token from the server
    async function getToken(tokenEndpoint) {
        const response = await fetch(tokenEndpoint, { 
            credentials: 'include',
            headers: {
                'Accept': 'application/json'
            }
        });
        
        if (!response.ok) {
            throw new Error(`Failed to get token: ${response.status} ${response.statusText}`);
        }
        
        const tokenData = await response.json();
        console.log('Retrieved token:', { userId: tokenData.userId, expiresOn: tokenData.expiresOn });
        
        // Initialize the calling client with the token
        await initialize(tokenData.token, tokenData.userId);
        
        return tokenData;
    }

    // Public API
    return {
        getToken,
        startCall,
        endCall,
        toggleAudio,
        toggleVideo
    };
})();
