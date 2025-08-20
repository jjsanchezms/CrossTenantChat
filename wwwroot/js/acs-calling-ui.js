// ACS UI Library Call Composite bootstrap for Blazor using UMD globals

export const acsCallingUI = (() => {
  let compositeInstance;

  function getCommReactGlobal() {
    // Check all possible global variable names for Azure Communication React
    const candidates = [
      'communicationReact',
      'CommunicationReact', 
      'azureCommunicationReact',
      'AzureCommunicationReact',
      'AzureCommunicationUI',
      'azureCommunicationUI'
    ];
    
    for (const name of candidates) {
      if (window[name]) {
        console.log(`Found Azure Communication React global: ${name}`);
        return window[name];
      }
    }
    
    // Log what globals are actually available
    console.log('Available window properties (filtered for Azure/Communication):', 
      Object.keys(window).filter(k => 
        k.toLowerCase().includes('azure') || 
        k.toLowerCase().includes('communication') ||
        k.toLowerCase().includes('react')
      ));
    
    return null;
  }

  function getCommonGlobal() {
    // Check all possible global variable names for Azure Communication Common
    const candidates = [
      'AzureCommunication',
      'azureCommunication', 
      'AzureCommunicationCommon',
      'azureCommunicationCommon',
      'AzureCommunicationSDK',
      'azureCommunicationSDK'
    ];
    
    for (const name of candidates) {
      if (window[name]) {
        console.log(`Found Azure Communication Common global: ${name}`);
        return window[name];
      }
    }
    
    return null;
  }

  async function ensureGlobals(timeoutMs = 10000) {
    const start = Date.now();
    console.log('Waiting for required libraries to load...');
    
    // Check if basic libraries are available
    const react = window.React;
    const reactDOM = window.ReactDOM;
    const common = getCommonGlobal();
    
    if (!react) {
      throw new Error('React library not loaded. Please ensure React CDN script is included and loaded.');
    }
    
    if (!reactDOM) {
      throw new Error('ReactDOM library not loaded. Please ensure ReactDOM CDN script is included and loaded.');
    }
    
    if (!common) {
      throw new Error('Azure Communication Common library not loaded. Please ensure the CDN script is included and loaded.');
    }
    
    // Check for Azure Communication React (the problematic one)
    const commReact = getCommReactGlobal();
    if (!commReact) {
      const availableGlobals = Object.keys(window).filter(k => 
        k.toLowerCase().includes('azure') || 
        k.toLowerCase().includes('communication') ||
        k.toLowerCase().includes('react')
      );
      
      const error = `❌ Azure Communication Services UI Library Issue:

The Azure Communication React library (@azure/communication-react) does not provide 
a UMD build that can be loaded via CDN script tags. This is a known limitation.

Available globals: ${availableGlobals.join(', ') || 'None found'}

SOLUTIONS:
1. Use a bundler like webpack, Vite, or Create React App
2. Use the Azure Communication Services calling SDK directly
3. Consider alternative UI libraries that provide UMD builds

For production applications, it's recommended to use proper module bundling.`;

      console.error(error);
      throw new Error('Azure Communication Services UI Library is not available for direct CDN usage');
    }
    
    console.log('✅ All required libraries loaded successfully');
    return;
  }

  async function getToken(tokenEndpoint) {
    const resp = await fetch(tokenEndpoint, { credentials: 'include' });
    if (!resp.ok) throw new Error('Failed to get token');
    return await resp.json();
  }

  return {
    start: async (containerId, tokenEndpoint, groupId, displayName) => {
      await ensureGlobals();
      const React = window.React;
      const ReactDOM = window.ReactDOM;
      const commReact = getCommReactGlobal();
      const { CallComposite, createAzureCommunicationCallAdapter } = commReact;
      const common = getCommonGlobal();
      const { AzureCommunicationTokenCredential } = common;

      const container = document.getElementById(containerId);
      if (!container) throw new Error('Container not found');

      const { token, userId } = await getToken(tokenEndpoint);
      const credential = new AzureCommunicationTokenCredential(token);
      const adapter = await createAzureCommunicationCallAdapter({
        userId: { communicationUserId: userId },
        displayName: displayName || 'ACS User',
        credential,
        locator: { groupId }
      });

      const app = React.createElement(CallComposite, { adapter });
      ReactDOM.render(app, container);
      compositeInstance = { adapter, container };
    },
    dispose: async () => {
      if (!compositeInstance) return;
      const { adapter, container } = compositeInstance;
      try { await adapter.dispose(); } catch {}
      try { window.ReactDOM.unmountComponentAtNode(container); } catch {}
      compositeInstance = undefined;
    }
  };
})();
