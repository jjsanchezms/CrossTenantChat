// Improved ACS UI Library Call Composite bootstrap for Blazor
// This version handles library loading more robustly

export const acsCallingUI = (() => {
  let compositeInstance;
  let librariesLoaded = false;

  // Function to dynamically load scripts if they're not available
  async function loadScript(src) {
    return new Promise((resolve, reject) => {
      if (document.querySelector(`script[src="${src}"]`)) {
        resolve();
        return;
      }
      
      const script = document.createElement('script');
      script.src = src;
      script.onload = resolve;
      script.onerror = (event) => {
        const errorMessage = `Failed to load script: ${src} - ${event.type || 'unknown error'}`;
        console.error(errorMessage, event);
        reject(new Error(errorMessage));
      };
      document.head.appendChild(script);
    });
  }

  // Function to load all required libraries
  async function ensureLibrariesLoaded() {
    if (librariesLoaded) return;
    
    console.log('🔄 Loading Azure Communication Services libraries...');
    
    try {
      // Load libraries in the correct order
      const libraries = [
        { url: 'https://cdn.jsdelivr.net/npm/react@18.2.0/umd/react.production.min.js', name: 'React' },
        { url: 'https://cdn.jsdelivr.net/npm/react-dom@18.2.0/umd/react-dom.production.min.js', name: 'ReactDOM' },
        { url: 'https://cdn.jsdelivr.net/npm/%40azure/communication-common@2.5.1/dist/communication-common.min.js', name: 'Azure Communication Common' }
      ];
      
      for (const lib of libraries) {
        try {
          await loadScript(lib.url);
          console.log(`✅ Loaded: ${lib.name}`);
        } catch (error) {
          console.error(`❌ Failed to load ${lib.name}:`, error);
          throw new Error(`Failed to load required library: ${lib.name}. ${error.message}`);
        }
      }
      
      // Note: Azure Communication React UI Library does not provide a UMD build
      // that can be loaded via CDN. This is a known limitation.
      // For production use, this library should be bundled using a build tool like webpack or Vite.
      console.warn('⚠️ Azure Communication React UI Library cannot be loaded via CDN - no UMD build available');
      throw new Error('Azure Communication Services UI Library does not provide a UMD build for direct browser usage. Please use a bundled approach with webpack, Vite, or similar build tools.');
      
      // Wait for globals to be available (this won't be reached due to the error above)
      await waitForGlobals();
      librariesLoaded = true;
      console.log('🎉 All ACS libraries loaded successfully!');
      
    } catch (error) {
      console.error('❌ Failed to load libraries:', error);
      throw error;
    }
  }

  // Function to wait for global variables to be available
  async function waitForGlobals(maxAttempts = 100) {
    for (let i = 0; i < maxAttempts; i++) {
      if (checkGlobalsAvailable()) {
        return;
      }
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    
    // Generate detailed error message about what's missing
    const react = window.React;
    const reactDOM = window.ReactDOM;
    const missingLibraries = [];
    if (!react) missingLibraries.push('React');
    if (!reactDOM) missingLibraries.push('ReactDOM');
    
    // Check for communication libraries
    const commReactCandidates = [
      'AzureCommunicationUiSdk',
      'communicationReact',
      'CommunicationReact',
      'azureCommunicationReact',
      'AzureCommunicationReact'
    ];
    const commReact = commReactCandidates.find(name => window[name]);
    if (!commReact) missingLibraries.push('Azure Communication React');
    
    const commonCandidates = [
      'AzureCommunication',
      'azureCommunication',
      'AzureCommunicationCommon'
    ];
    const common = commonCandidates.find(name => window[name]);
    if (!common) missingLibraries.push('Azure Communication Common');
    
    const errorMessage = `Required global variables not available after ${maxAttempts} attempts (${maxAttempts * 100}ms). Missing: ${missingLibraries.join(', ')}`;
    throw new Error(errorMessage);
  }

  // Function to check if all required globals are available
  function checkGlobalsAvailable() {
    const react = window.React;
    const reactDOM = window.ReactDOM;
    
    // Check for Azure Communication React global
    const commReactCandidates = [
      'AzureCommunicationUiSdk',
      'communicationReact',
      'CommunicationReact',
      'azureCommunicationReact',
      'AzureCommunicationReact',
      'AzureCommunicationUI',
      'azureCommunicationUI'
    ];
    
    const commReact = commReactCandidates.find(name => window[name]) ? 
      window[commReactCandidates.find(name => window[name])] : null;
    
    // Check for Azure Communication Common global  
    const commonCandidates = [
      'AzureCommunication',
      'azureCommunication',
      'AzureCommunicationCommon', 
      'azureCommunicationCommon',
      'AzureCommunicationSDK',
      'azureCommunicationSDK'
    ];
    
    const common = commonCandidates.find(name => window[name]) ? 
      window[commonCandidates.find(name => window[name])] : null;
    
    const available = react && reactDOM && commReact && common;
    
    if (!available) {
      console.log(`Waiting for globals... React: ${!!react}, ReactDOM: ${!!reactDOM}, CommReact: ${!!commReact}, Common: ${!!common}`);
      if (!commReact) {
        console.log('Available communication globals:', 
          Object.keys(window).filter(k => k.toLowerCase().includes('communication') || k.toLowerCase().includes('azure')));
      }
    }
    
    return available;
  }

  // Function to get the correct global references
  function getGlobals() {
    const React = window.React;
    const ReactDOM = window.ReactDOM;
    
    // Find Azure Communication React
    const commReactCandidates = [
      'AzureCommunicationUiSdk',
      'communicationReact', 
      'CommunicationReact',
      'azureCommunicationReact',
      'AzureCommunicationReact',
      'AzureCommunicationUI',
      'azureCommunicationUI'
    ];
    
    const commReactGlobal = commReactCandidates.find(name => window[name]);
    const commReact = commReactGlobal ? window[commReactGlobal] : null;
    
    // Find Azure Communication Common
    const commonCandidates = [
      'AzureCommunication',
      'azureCommunication',
      'AzureCommunicationCommon',
      'azureCommunicationCommon', 
      'AzureCommunicationSDK',
      'azureCommunicationSDK'
    ];
    
    const commonGlobal = commonCandidates.find(name => window[name]);
    const common = commonGlobal ? window[commonGlobal] : null;
    
    console.log(`Using globals - React: React, ReactDOM: ReactDOM, CommReact: ${commReactGlobal}, Common: ${commonGlobal}`);
    
    return { React, ReactDOM, commReact, common };
  }

  async function getToken(tokenEndpoint) {
    const resp = await fetch(tokenEndpoint, { credentials: 'include' });
    if (!resp.ok) {
      throw new Error(`Failed to get token: ${resp.status} ${resp.statusText}`);
    }
    return await resp.json();
  }

  return {
    start: async (containerId, tokenEndpoint, groupId, displayName) => {
      console.log('🚀 Starting ACS Call Composite...');
      
      try {
        // Ensure all libraries are loaded
        await ensureLibrariesLoaded();
        
        // Get global references
        const { React, ReactDOM, commReact, common } = getGlobals();
        
        if (!commReact?.CallComposite || !commReact?.createAzureCommunicationCallAdapter) {
          throw new Error('CallComposite or createAzureCommunicationCallAdapter not found in communication library');
        }
        
        if (!common?.AzureCommunicationTokenCredential) {
          throw new Error('AzureCommunicationTokenCredential not found in common library');
        }
        
        const { CallComposite, createAzureCommunicationCallAdapter } = commReact;
        const { AzureCommunicationTokenCredential } = common;

        const container = document.getElementById(containerId);
        if (!container) {
          throw new Error(`Container with ID '${containerId}' not found`);
        }

        console.log('🔑 Getting authentication token...');
        const { token, userId } = await getToken(tokenEndpoint);
        
        console.log('🔐 Creating credentials and adapter...');
        const credential = new AzureCommunicationTokenCredential(token);
        const adapter = await createAzureCommunicationCallAdapter({
          userId: { communicationUserId: userId },
          displayName: displayName || 'ACS User',
          credential,
          locator: { groupId }
        });

        console.log('🎨 Rendering Call Composite...');
        const app = React.createElement(CallComposite, { adapter });
        ReactDOM.render(app, container);
        compositeInstance = { adapter, container };
        
        console.log('✅ Call Composite started successfully!');
        
      } catch (error) {
        console.error('❌ Failed to start Call Composite:', error);
        throw error;
      }
    },

    dispose: async () => {
      if (!compositeInstance) return;
      
      console.log('🧹 Disposing Call Composite...');
      const { adapter, container } = compositeInstance;
      
      try { 
        await adapter.dispose(); 
        console.log('✅ Adapter disposed');
      } catch (error) {
        console.warn('⚠️ Error disposing adapter:', error);
      }
      
      try { 
        window.ReactDOM.unmountComponentAtNode(container); 
        console.log('✅ Component unmounted');
      } catch (error) {
        console.warn('⚠️ Error unmounting component:', error);
      }
      
      compositeInstance = undefined;
      console.log('✅ Call Composite disposed successfully');
    },

    // Utility method to check if libraries are ready
    checkLibraries: async () => {
      try {
        await ensureLibrariesLoaded();
        return { success: true, message: 'All libraries loaded successfully' };
      } catch (error) {
        const errorMessage = error?.message || error?.toString() || 'Unknown error occurred';
        console.error('checkLibraries failed:', error);
        return { success: false, message: errorMessage };
      }
    }
  };
})();